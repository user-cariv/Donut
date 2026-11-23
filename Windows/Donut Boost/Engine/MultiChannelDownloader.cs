using System.Diagnostics;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using DonutBoost.Models;
using MonoTorrent;
using MonoTorrent.Client;

namespace DonutBoost.Engine;

public class MultiChannelDownloader
{
    private readonly NetworkBondingManager _bondingManager;
    private readonly LicenseManager? _licenseManager;
    private readonly BandwidthThrottler _throttler = new();
    private CancellationTokenSource? _cts;
    private DownloadSession? _currentSession;
    private FileStream? _fileStream;
    private readonly object _fileLock = new();
    private readonly SemaphoreSlim _downloadGate = new(1, 1);
    private volatile bool _isPaused = false;
    private ClientEngine? _torrentEngine;
    private TorrentManager? _torrentManager;

    public DownloadSession? CurrentSession => _currentSession;
    public bool IsPaused => _isPaused;
    public BandwidthThrottler Throttler => _throttler;

    public event Action<DownloadSession>? ProgressUpdated;
    public event Action<DownloadSession>? StatusChanged;
    public event Action<string>? LogReceived;

    public MultiChannelDownloader(NetworkBondingManager bondingManager, LicenseManager? licenseManager = null)
    {
        _bondingManager = bondingManager;
        _licenseManager = licenseManager;
    }

    public void Pause()
    {
        if (_currentSession != null && _currentSession.Status == DownloadStatus.Downloading)
        {
            _isPaused = true;
            _currentSession.Status = DownloadStatus.Paused;
            _currentSession.CombinedSpeedBps = 0;
            if (_torrentManager != null)
                _ = PauseTorrentSafelyAsync(_torrentManager);
            // Physical adapter rates keep updating while the file transfer is paused.
            StatusChanged?.Invoke(_currentSession);
            ProgressUpdated?.Invoke(_currentSession);
            LogReceived?.Invoke("⏸ Download paused by user. Sockets suspended safely.");
        }
    }

    public void Resume()
    {
        if (_currentSession != null && _currentSession.Status == DownloadStatus.Paused)
        {
            _isPaused = false;
            _currentSession.Status = DownloadStatus.Downloading;
            if (_torrentManager != null)
                _ = ResumeTorrentSafelyAsync(_torrentManager);
            StatusChanged?.Invoke(_currentSession);
            LogReceived?.Invoke("▶ Resuming download pipeline...");
        }
    }

    private async Task PauseTorrentSafelyAsync(TorrentManager manager)
    {
        try { await manager.PauseAsync(); }
        catch (Exception ex) { LogReceived?.Invoke($"Could not pause torrent cleanly: {ex.Message}"); }
    }

    private async Task ResumeTorrentSafelyAsync(TorrentManager manager)
    {
        try { await manager.StartAsync(); }
        catch (Exception ex) { LogReceived?.Invoke($"Could not resume torrent cleanly: {ex.Message}"); }
    }

    public async Task StartDownloadAsync(string url, string outputFolder, CancellationToken externalToken = default)
    {
        await _downloadGate.WaitAsync(externalToken);
        try
        {
            await StartDownloadCoreAsync(url, outputFolder, externalToken);
        }
        finally
        {
            _downloadGate.Release();
        }
    }

    private async Task StartDownloadCoreAsync(string url, string outputFolder, CancellationToken externalToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
        var token = _cts.Token;
        _isPaused = false;

        var session = new DownloadSession
        {
            Url = url,
            Status = DownloadStatus.Probing
        };
        _currentSession = session;
        StatusChanged?.Invoke(session);

        try
        {
            // Security verification: Valid active session token required to open sockets
            if (_licenseManager != null && !_licenseManager.ValidateActiveSessionToken())
            {
                LogReceived?.Invoke("🔒 Security: Active valid license token required to boost downloads.");
                session.Status = DownloadStatus.Failed;
                session.ErrorMessage = "Active valid license required.";
                StatusChanged?.Invoke(session);
                return;
            }

            if (IsTorrentInput(url))
            {
                await StartTorrentDownloadAsync(url, outputFolder, session, token);
                return;
            }

            // 1. Check enabled adapters
            var allAdapters = _bondingManager.Adapters.Where(a => a.IsOnline && a.HasDownloadAddress).ToList();
            var enabledAdapters = allAdapters.Where(a => a.IsEnabled).ToList();
            if (enabledAdapters.Count == 0)
            {
                throw new InvalidOperationException("No active network adapters selected! Please ensure Wi-Fi is connected or plug in your phone with USB Tethering.");
            }

            LogReceived?.Invoke($"Found {allAdapters.Count} network connection(s) available for download ({enabledAdapters.Count} enabled):");
            foreach (var adp in allAdapters)
            {
                LogReceived?.Invoke($"  → [{adp.IconGlyph} {adp.Name}] ({adp.TypeLabel}) IP: {adp.LocalIp ?? adp.LocalIPv6} - Enabled: {adp.IsEnabled}");
            }

            // Reset adapter metrics for fresh download
            foreach (var adp in allAdapters)
            {
                adp.TotalBytesDownloaded = 0;
            }

            string normalizedUrl = NormalizeDownloadUrl(url);
            session.Url = normalizedUrl;
            // Check each adapter against this host. A failed Wi-Fi probe must not
            // force a range-capable mobile connection into single-stream mode.
            var probes = await Task.WhenAll(enabledAdapters.OrderByDescending(a => a.IsWifi)
                .Select(async adapter =>
                {
                    using var adapterTransfer = new AdapterTransferCancellation(adapter, token);
                    using var probeTimeout = CancellationTokenSource.CreateLinkedTokenSource(adapterTransfer.Token);
                    probeTimeout.CancelAfter(TimeSpan.FromSeconds(8));
                    try
                    {
                        var probe = await ProbeUrlAsync(_bondingManager.GetOrCreateClient(adapter), normalizedUrl, probeTimeout.Token);
                        if (!probe.Reachable)
                            LogReceived?.Invoke($"[{adapter.Name}] The server did not accept the download probe.");
                        return (Adapter: adapter, Probe: probe);
                    }
                    catch (OperationCanceledException) when (!token.IsCancellationRequested)
                    {
                        if (!adapterTransfer.Token.IsCancellationRequested)
                            LogReceived?.Invoke($"[{adapter.Name}] Download probe timed out.");
                        return (Adapter: adapter, Probe: (FileName: string.Empty, TotalSize: -1L, SupportsRanges: false, Reachable: false));
                    }
                }));
            var reachable = probes.Where(p => p.Probe.Reachable).ToList();
            if (reachable.Count == 0)
                throw new IOException("The download server could not be reached on any selected adapter.");

            var selected = reachable.FirstOrDefault(p => p.Probe.SupportsRanges && p.Probe.TotalSize > 0);
            if (selected.Adapter == null)
                selected = reachable[0];
            var (fileName, totalSize, supportsRanges, _) = selected.Probe;
            var rangeAdapters = reachable
                .Where(p => p.Probe.SupportsRanges && p.Probe.TotalSize == totalSize)
                .Select(p => p.Adapter)
                .ToList();
            foreach (var probe in reachable.Where(p => supportsRanges && !rangeAdapters.Contains(p.Adapter)))
                LogReceived?.Invoke($"[{probe.Adapter.Name}] The server did not confirm compatible byte ranges on this connection.");

            session.FileName = fileName;
            session.TotalBytes = totalSize;
            session.SupportsRangeRequests = supportsRanges;
            // Pre-allocate destination file
            Directory.CreateDirectory(outputFolder);
            lock (_fileLock)
            {
                _fileStream = OpenUniqueDestinationFile(outputFolder, fileName, out string destinationPath);
                session.DestinationPath = destinationPath;
                session.FileName = Path.GetFileName(destinationPath);
                try
                {
                    if (totalSize > 0)
                        _fileStream.SetLength(totalSize);
                }
                catch
                {
                    _fileStream.Dispose();
                    _fileStream = null;
                    try { File.Delete(destinationPath); } catch { }
                    throw;
                }
            }
            LogReceived?.Invoke($"File: {session.FileName} | Size: {session.FormattedTotalSize} | Supports Range Slicing: {supportsRanges}");

            session.Status = DownloadStatus.Downloading;
            StatusChanged?.Invoke(session);

            // Only enabled adapters may receive chunks. Passing allAdapters here
            // caused Wi-Fi to keep downloading in Mobile Data Only mode.
            if (supportsRanges && totalSize > 0)
            {
                await DownloadMultiChannelAsync(session, rangeAdapters, enabledAdapters, token);
            }
            else
            {
                LogReceived?.Invoke($"Server did not confirm byte-range support. Using a single stream on {selected.Adapter.Name}.");
                await DownloadSingleStreamAsync(session, selected.Adapter, token);
            }

            if (session.Status == DownloadStatus.Failed)
            {
                StatusChanged?.Invoke(session);
            }
            else if (token.IsCancellationRequested || session.Status == DownloadStatus.Cancelled)
            {
                session.Status = DownloadStatus.Cancelled;
                session.CombinedSpeedBps = 0;
                // Do not overwrite live NIC counters when a download ends.
                StatusChanged?.Invoke(session);
                ProgressUpdated?.Invoke(session);
                LogReceived?.Invoke("Download cancelled. Pipeline stopped.");
            }
            else
            {
                session.Status = DownloadStatus.Completed;
                StatusChanged?.Invoke(session);
                LogReceived?.Invoke($"✓ Download completed successfully! File saved to {session.DestinationPath}");
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            if (session.Status != DownloadStatus.Failed)
                session.Status = DownloadStatus.Cancelled;
            StatusChanged?.Invoke(session);
            if (session.Status == DownloadStatus.Cancelled)
                LogReceived?.Invoke("Download was cancelled.");
        }
        catch (Exception ex)
        {
            session.Status = DownloadStatus.Failed;
            session.ErrorMessage = ex.Message;
            StatusChanged?.Invoke(session);
            LogReceived?.Invoke($"✕ Error: {ex.Message}");
        }
        finally
        {
            lock (_fileLock)
            {
                _fileStream?.Flush();
                _fileStream?.Dispose();
                _fileStream = null;
            }
            _cts?.Dispose();
            _cts = null;
        }
    }

    private static bool IsTorrentInput(string input)
    {
        if (input.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
            return true;
        if (File.Exists(input))
            return string.Equals(Path.GetExtension(input), ".torrent", StringComparison.OrdinalIgnoreCase);
        return Uri.TryCreate(input, UriKind.Absolute, out var uri) &&
               string.Equals(Path.GetExtension(uri.AbsolutePath), ".torrent", StringComparison.OrdinalIgnoreCase);
    }

    private async Task StartTorrentDownloadAsync(string input, string outputFolder, DownloadSession session, CancellationToken token)
    {
        Directory.CreateDirectory(outputFolder);
        var torrentId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();
        var torrentSavePath = Path.Combine(outputFolder, "Torrents", torrentId);
        Directory.CreateDirectory(torrentSavePath);
        var cacheDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Donut", "TorrentCache");
        Directory.CreateDirectory(cacheDirectory);
        _torrentEngine = new ClientEngine(new EngineSettingsBuilder
        {
            CacheDirectory = cacheDirectory,
            AllowPortForwarding = false
        }.ToSettings());

        try
        {
            if (input.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
            {
                var magnet = MagnetLink.Parse(input);
                _torrentManager = await _torrentEngine.AddAsync(magnet, torrentSavePath);
                await _torrentManager.StartAsync();
                session.FileName = "Resolving magnet metadata...";
                StatusChanged?.Invoke(session);
                LogReceived?.Invoke("Magnet added. Waiting for torrent metadata from peers...");
                using var metadataTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                metadataTimeout.CancelAfter(TimeSpan.FromMinutes(5));
                try
                {
                    await _torrentManager.WaitForMetadataAsync(metadataTimeout.Token);
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    throw new TimeoutException("Torrent metadata was not available from peers within five minutes.");
                }
            }
            else
            {
                Torrent torrent;
                if (File.Exists(input))
                {
                    var torrentPath = Path.GetFullPath(input);
                    if (new FileInfo(torrentPath).Length > 10_485_760)
                        throw new InvalidDataException("Torrent metadata is larger than the 10 MB safety limit.");
                    torrent = await Torrent.LoadAsync(torrentPath);
                }
                else if (Uri.TryCreate(input, UriKind.Absolute, out var metadataUri) &&
                         (metadataUri.Scheme == Uri.UriSchemeHttp || metadataUri.Scheme == Uri.UriSchemeHttps))
                {
                    using var metadataClient = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
                    using var response = await metadataClient.GetAsync(metadataUri, HttpCompletionOption.ResponseHeadersRead, token);
                    response.EnsureSuccessStatusCode();
                    if (response.Content.Headers.ContentLength is > 10_485_760)
                        throw new InvalidDataException("Torrent metadata is larger than the 10 MB safety limit.");
                    var metadata = await response.Content.ReadAsByteArrayAsync(token);
                    if (metadata.Length == 0 || metadata.Length > 10_485_760)
                        throw new InvalidDataException("Torrent metadata is empty or larger than the 10 MB safety limit.");
                    torrent = await Torrent.LoadAsync(new Memory<byte>(metadata));
                }
                else
                {
                    throw new ArgumentException("Enter a magnet link, an HTTP(S) .torrent link, or the path to a .torrent file.");
                }

                _torrentManager = await _torrentEngine.AddAsync(torrent, torrentSavePath);
                await _torrentManager.StartAsync();
            }

            token.ThrowIfCancellationRequested();
            var managerReady = _torrentManager ?? throw new InvalidOperationException("Torrent manager did not initialize.");
            var torrentMetadata = managerReady.Torrent ?? throw new InvalidDataException("Torrent metadata could not be read.");
            session.FileName = torrentMetadata.Name;
            session.TotalBytes = torrentMetadata.Size;
            session.SupportsRangeRequests = false;
            session.DestinationPath = torrentSavePath;
            session.Status = DownloadStatus.Downloading;
            StatusChanged?.Invoke(session);
            LogReceived?.Invoke($"Torrent ready: {session.FileName} | verified payload size: {session.FormattedTotalSize} | peer connections use the Windows network route.");

            while (!token.IsCancellationRequested && !_torrentManager.Complete)
            {
                if (_isPaused)
                {
                    await Task.Delay(250, token);
                    continue;
                }

                var manager = _torrentManager ?? throw new InvalidOperationException("Torrent manager stopped unexpectedly.");
                var progress = Math.Clamp(manager.Progress / 100d, 0d, 1d);
                session.DownloadedBytes = Math.Min(session.TotalBytes, (long)(session.TotalBytes * progress));
                session.UpdateSpeed(manager.Monitor.DownloadRate);
                ProgressUpdated?.Invoke(session);
                await Task.Delay(500, token);
            }

            token.ThrowIfCancellationRequested();
            session.DownloadedBytes = session.TotalBytes;
            session.UpdateSpeed(0);
            await _torrentManager.StopAsync(TimeSpan.FromSeconds(2));
            session.Status = DownloadStatus.Completed;
            StatusChanged?.Invoke(session);
            ProgressUpdated?.Invoke(session);
            LogReceived?.Invoke("Torrent download complete. Donut Boost stopped the torrent and is not seeding in the background.");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            session.Status = DownloadStatus.Cancelled;
            session.CombinedSpeedBps = 0;
            StatusChanged?.Invoke(session);
        }
        catch (Exception ex)
        {
            session.Status = DownloadStatus.Failed;
            session.ErrorMessage = ex.Message;
            StatusChanged?.Invoke(session);
            LogReceived?.Invoke($"Torrent download failed: {ex.Message}");
        }
        finally
        {
            if (_torrentManager != null)
            {
                try { await _torrentManager.StopAsync(TimeSpan.FromSeconds(2)); } catch { }
                try { await _torrentEngine!.RemoveAsync(_torrentManager); } catch { }
            }
            _torrentManager = null;
            _torrentEngine?.Dispose();
            _torrentEngine = null;
        }
    }

    private static string NormalizeDownloadUrl(string input)
    {
        string value = input.Trim();
        if (!value.Contains("://", StringComparison.Ordinal)) value = "https://" + value;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrWhiteSpace(uri.Host))
            throw new InvalidOperationException("Enter a valid HTTP or HTTPS download link.");
        return uri.AbsoluteUri;
    }

    private static FileStream OpenUniqueDestinationFile(string folder, string fileName, out string destinationPath)
    {
        string name = Path.GetFileNameWithoutExtension(fileName);
        string extension = Path.GetExtension(fileName);
        for (int suffix = 0; ; suffix++)
        {
            string candidateName = suffix == 0 ? fileName : $"{name} ({suffix}){extension}";
            string candidate = Path.Combine(folder, candidateName);
            try
            {
                var stream = new FileStream(candidate, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 64 * 1024, useAsync: true);
                destinationPath = candidate;
                return stream;
            }
            catch (IOException) when (File.Exists(candidate))
            {
                // Another download or process created this name after our check; try the next suffix.
            }
        }
    }

    private async Task DownloadMultiChannelAsync(DownloadSession session, List<NetworkAdapterInfo> adapters,
        List<NetworkAdapterInfo> probedAdapters, CancellationToken token)
    {
        // Compute chunk size: between 2 MB and 16 MB depending on total size
        long chunkSize = Math.Clamp(session.TotalBytes / (adapters.Count * 12), 2 * 1024 * 1024, 16 * 1024 * 1024);
        int chunkIndex = 0;
        long currentOffset = 0;

        var chunks = new List<DownloadChunk>();
        while (currentOffset < session.TotalBytes)
        {
            long end = Math.Min(currentOffset + chunkSize - 1, session.TotalBytes - 1);
            chunks.Add(new DownloadChunk
            {
                Index = chunkIndex++,
                StartOffset = currentOffset,
                EndOffset = end,
                BytesDownloaded = 0,
                IsCompleted = false
            });
            currentOffset = end + 1;
        }

        session.Chunks = chunks;
        var pendingQueue = new Queue<DownloadChunk>(chunks);
        var queueLock = new object();

        LogReceived?.Invoke($"⚡ Bonding pipeline active: {chunks.Count} chunks of {DownloadSession.FormatBytes(chunkSize)} bonded across {adapters.Count} connection(s).");

        // Speed measurement trackers
        long lastDownloadedBytes = 0;
        var stopwatch = Stopwatch.StartNew();

        // Background monitor for live speed calculation
        var monitorTask = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested && !chunks.All(c => c.IsCompleted) &&
                   session.Status != DownloadStatus.Completed && session.Status != DownloadStatus.Failed && session.Status != DownloadStatus.Cancelled)
            {
                await Task.Delay(250);
                if (token.IsCancellationRequested) break;
                
                if (_isPaused)
                {
                    session.UpdateSpeed(0);
                    ProgressUpdated?.Invoke(session);
                    continue;
                }

                double elapsedSeconds = stopwatch.Elapsed.TotalSeconds;
                if (elapsedSeconds >= 0.25)
                {
                    stopwatch.Restart();
                    long currentDownloaded = session.DownloadedBytes;
                    long bytesDelta = Math.Max(0, currentDownloaded - lastDownloadedBytes);
                    lastDownloadedBytes = currentDownloaded;

                    double instantSpeed = bytesDelta / elapsedSeconds;
                    session.UpdateSpeed(instantSpeed);

                    ProgressUpdated?.Invoke(session);
                }
            }
        }, token);

        // Worker tasks per network adapter (12 workers for Mobile 5G, 8 for Wi-Fi for maximum 300+ Mbps throughput)
        var workerTasks = new List<Task>();
        var trackedAdapterIds = new HashSet<string>(probedAdapters.Select(a => a.Id));
        var workerLock = new object();

        void SpawnWorkersForAdapter(NetworkAdapterInfo adp)
        {
            var client = _bondingManager.GetOrCreateClient(adp);
            int workersCount = adp.IsMobilePhone ? 12 : 8;
            for (int w = 0; w < workersCount; w++)
            {
                lock (workerLock)
                {
                    workerTasks.Add(Task.Run(async () =>
                    {
                        while (!token.IsCancellationRequested)
                        {
                            // Handle Pause gracefully
                            while (_isPaused && !token.IsCancellationRequested)
                            {
                                await Task.Delay(200, token);
                            }

                            // Support live mid-download mode switching (Wi-Fi Only, Mobile Data Only, or Combine Both)
                            // If phone is unplugged or disabled mid-download, gracefully release chunks to remaining connections
                            if (!adp.IsEnabled || !adp.IsOnline)
                            {
                                await Task.Delay(300, token);
                                if (chunks.All(c => c.IsCompleted)) break;
                                continue;
                            }

                            DownloadChunk? chunk = null;
                            lock (queueLock)
                            {
                                if (pendingQueue.Count > 0)
                                {
                                    chunk = pendingQueue.Dequeue();
                                    chunk.AssignedAdapterId = adp.Id;
                                }
                            }

                            if (chunk == null)
                            {
                                if (chunks.All(c => c.IsCompleted)) break;
                                await Task.Delay(200, token);
                                continue;
                            }

                            bool success = false;
                            bool interrupted = false;
                            bool attemptThrew = false;
                            long bytesBeforeAttempt = chunk.BytesDownloaded;
                            using var adapterTransfer = new AdapterTransferCancellation(adp, token);
                            try
                            {
                                interrupted = await DownloadChunkWithClientAsync(client, session.Url, chunk, adp, session, adapterTransfer.Token);
                                success = chunk.IsCompleted;
                            }
                            catch (Exception) when (adapterTransfer.Token.IsCancellationRequested)
                            {
                                interrupted = true;
                            }
                            catch (Exception ex)
                            {
                                attemptThrew = true;
                                if (!token.IsCancellationRequested && !_isPaused && adp.IsEnabled && adp.IsOnline)
                                {
                                    string msg = $"[{adp.Name}] Notice on chunk {chunk.Index}: {ex.Message}";
                                    LogReceived?.Invoke(msg);
                                    if (chunk.BytesDownloaded == bytesBeforeAttempt)
                                        chunk.ConsecutiveFailures++;
                                    else
                                        chunk.ConsecutiveFailures = 0;

                                    if (IsPermanentHttpFailure(ex) || chunk.ConsecutiveFailures >= 8)
                                    {
                                        session.ErrorMessage = IsPermanentHttpFailure(ex)
                                            ? "The download server rejected a byte-range request. Check that the link is valid and accessible."
                                            : "The connection repeatedly failed without making progress. Please retry the download.";
                                        session.Status = DownloadStatus.Failed;
                                        _cts?.Cancel();
                                    }
                                    else
                                    {
                                        int delayMs = Math.Min(4000, 250 * (1 << Math.Min(chunk.ConsecutiveFailures, 4)));
                                        await Task.Delay(delayMs, token);
                                    }
                                }
                            }

                            if (success || chunk.BytesDownloaded > bytesBeforeAttempt)
                            {
                                chunk.ConsecutiveFailures = 0;
                            }
                            else if (!attemptThrew && !interrupted && !chunk.IsCompleted && !token.IsCancellationRequested && session.Status != DownloadStatus.Failed)
                            {
                                chunk.ConsecutiveFailures++;
                                if (chunk.ConsecutiveFailures >= 32)
                                {
                                    session.ErrorMessage = "The server repeatedly ended a byte-range response early. Please retry or use another download link.";
                                    session.Status = DownloadStatus.Failed;
                                    _cts?.Cancel();
                                }
                                else
                                {
                                    await Task.Delay(Math.Min(4000, 250 * (1 << Math.Min(chunk.ConsecutiveFailures, 4))), token);
                                }
                            }

                            // Seamless Handover: If chunk is not completed (e.g. paused, errored, or connection turned OFF mid-download),
                            // return it to pendingQueue immediately so an active connection picks it up right where it left off!
                            if (!chunk.IsCompleted && !token.IsCancellationRequested && session.Status != DownloadStatus.Failed)
                            {
                                lock (queueLock)
                                {
                                    pendingQueue.Enqueue(chunk);
                                }
                            }

                            if (!success && token.IsCancellationRequested) break;
                        }
                    }, token));
                }
            }
        }

        foreach (var adapter in adapters)
        {
            SpawnWorkersForAdapter(adapter);
        }

        // Dynamically spawn workers if a phone or secondary connection is plugged in mid-download!
        Action onAdaptersChanged = () =>
        {
            var activeAdapters = _bondingManager.GetActiveEnabledAdapters();
            foreach (var adp in activeAdapters)
            {
                bool isNew = false;
                lock (workerLock)
                {
                    if (trackedAdapterIds.Add(adp.Id))
                    {
                        isNew = true;
                    }
                }

                if (isNew)
                {
                    LogReceived?.Invoke($"⚡ New connection joined download pipeline: [{adp.IconGlyph} {adp.Name}] ({adp.TypeLabel})");
                    SpawnWorkersForAdapter(adp);
                }
            }
        };

        _bondingManager.AdaptersChanged += onAdaptersChanged;

        try
        {
            while (!token.IsCancellationRequested && !chunks.All(c => c.IsCompleted))
            {
                Task[] currentTasks;
                lock (workerLock)
                {
                    currentTasks = workerTasks.ToArray();
                }
                await Task.WhenAny(Task.WhenAll(currentTasks), Task.Delay(500, token));
            }
        }
        finally
        {
            _bondingManager.AdaptersChanged -= onAdaptersChanged;
            try { await Task.WhenAll(workerTasks); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        }

        try { await monitorTask; }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }

        session.UpdateSpeed(0);
        foreach (var adp in adapters)
        {
            adp.NotifyMetricsChanged();
        }
        ProgressUpdated?.Invoke(session);
    }

    private async Task<bool> DownloadChunkWithClientAsync(HttpClient client, string url, DownloadChunk chunk, NetworkAdapterInfo adapter, DownloadSession session, CancellationToken token)
    {
        // Support resuming chunk from exact downloaded offset
        long startFrom = chunk.StartOffset + chunk.BytesDownloaded;
        if (startFrom > chunk.EndOffset)
        {
            chunk.IsCompleted = true;
            return false;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Range = new RangeHeaderValue(startFrom, chunk.EndOffset);

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();

        if (response.StatusCode != System.Net.HttpStatusCode.PartialContent)
        {
            throw new InvalidOperationException($"Server returned status {response.StatusCode} instead of 206 Partial Content for byte-range download.");
        }

        var contentRange = response.Content.Headers.ContentRange;
        if (contentRange?.From != startFrom || contentRange.To is null || contentRange.To > chunk.EndOffset ||
            (session.TotalBytes > 0 && contentRange.Length != session.TotalBytes) ||
            (response.Content.Headers.ContentLength.HasValue &&
             response.Content.Headers.ContentLength.Value != contentRange.To.Value - contentRange.From.Value + 1))
        {
            throw new InvalidOperationException("Server returned a mismatched Content-Range; refusing to write potentially corrupt download data.");
        }

        using var contentStream = await response.Content.ReadAsStreamAsync(token);
        byte[] buffer = new byte[256 * 1024]; // 256 KB high-throughput buffer for 300+ Mbps 5G & gigabit streams
        long writeOffset = startFrom;
        long responseEnd = contentRange.To.Value;
        using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);

        while (writeOffset <= responseEnd)
        {
            if (_isPaused || !adapter.IsEnabled || !adapter.IsOnline)
                return true;

            int length = (int)Math.Min(buffer.Length, responseEnd - writeOffset + 1);
            readTimeout.CancelAfter(TimeSpan.FromSeconds(20));
            int bytesRead = await contentStream.ReadAsync(buffer.AsMemory(0, length), readTimeout.Token);
            readTimeout.CancelAfter(Timeout.InfiniteTimeSpan);
            if (bytesRead == 0) break;

            lock (_fileLock)
            {
                token.ThrowIfCancellationRequested();
                if (_fileStream != null)
                {
                    _fileStream.Seek(writeOffset, SeekOrigin.Begin);
                    _fileStream.Write(buffer, 0, bytesRead);
                }
            }

            writeOffset += bytesRead;
            chunk.BytesDownloaded += bytesRead;

            adapter.AddDownloadedBytes(bytesRead);
            session.AddDownloadedBytes(bytesRead);
            if (chunk.BytesDownloaded == chunk.TotalBytes)
            {
                chunk.IsCompleted = true;
                return false;
            }

            // Apply real-time bandwidth throttling if configured by user
            // Speed limit applies ONLY to Home Wi-Fi / LAN — Mobile 5G runs 100% full unthrottled speed!
            if (!adapter.IsMobilePhone)
            {
                await _throttler.ThrottleAsync(bytesRead, token);
            }

        }

        return false;
    }

    private async Task DownloadSingleStreamAsync(DownloadSession session, NetworkAdapterInfo preferredAdapter, CancellationToken token)
    {
        int consecutiveFailures = 0;
        NetworkAdapterInfo? lastAdapter = null;
        bool waitingForAdapter = false;

        while (!token.IsCancellationRequested && (session.TotalBytes <= 0 || session.DownloadedBytes < session.TotalBytes))
        {
            // Support pausing and resuming single stream from exact byte position
            while (_isPaused && !token.IsCancellationRequested)
            {
                await Task.Delay(200, token);
            }

            if (token.IsCancellationRequested) break;

            var active = _bondingManager.GetActiveEnabledAdapters();
            var adapter = active.Contains(preferredAdapter) ? preferredAdapter :
                active.OrderByDescending(a => a.IsWifi).FirstOrDefault();
            if (adapter == null)
            {
                if (!waitingForAdapter)
                {
                    LogReceived?.Invoke("Download is waiting for an enabled network adapter.");
                    waitingForAdapter = true;
                }
                await Task.Delay(300, token);
                continue;
            }
            waitingForAdapter = false;
            if (lastAdapter != null && lastAdapter != adapter)
            {
                LogReceived?.Invoke($"Switching the download to {adapter.Name}.");
                consecutiveFailures = 0;
            }
            lastAdapter = adapter;

            using var adapterTransfer = new AdapterTransferCancellation(adapter, token);
            var transferToken = adapterTransfer.Token;
            var client = _bondingManager.GetOrCreateClient(adapter);

            using var request = new HttpRequestMessage(HttpMethod.Get, session.Url);
            if (session.DownloadedBytes > 0)
            {
                request.Headers.Range = new RangeHeaderValue(session.DownloadedBytes, null);
            }

            try
            {
                long bytesBeforeAttempt = session.DownloadedBytes;
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, transferToken);
                if (IsPermanentHttpFailure(response.StatusCode))
                    throw new HttpRequestException($"The download server returned HTTP {(int)response.StatusCode}.", null, response.StatusCode);
                response.EnsureSuccessStatusCode();

                long requestedOffset = session.DownloadedBytes;
                if (requestedOffset > 0)
                {
                    var contentRange = response.Content.Headers.ContentRange;
                    if (response.StatusCode != System.Net.HttpStatusCode.PartialContent || contentRange?.From != requestedOffset)
                    {
                        throw new InvalidOperationException("This server cannot resume the interrupted single-stream download safely. The partial file was not extended.");
                    }
                    if (contentRange.Length.HasValue && session.TotalBytes > 0 && contentRange.Length.Value != session.TotalBytes)
                    {
                        throw new InvalidOperationException("Server returned a different file size while resuming; refusing to corrupt the partial file.");
                    }
                }
                else if (response.StatusCode == System.Net.HttpStatusCode.PartialContent && response.Content.Headers.ContentRange?.Length is long rangedLength)
                {
                    session.TotalBytes = rangedLength;
                }
                else if (response.Content.Headers.ContentLength is long responseLength)
                {
                    session.TotalBytes = responseLength;
                }

                using var contentStream = await response.Content.ReadAsStreamAsync(transferToken);
                byte[] buffer = new byte[256 * 1024]; // 256 KB high-throughput buffer for 300+ Mbps 5G & gigabit streams
                int bytesRead;
                var stopwatch = Stopwatch.StartNew();
                long lastBytes = session.DownloadedBytes;

                while (true)
                {
                    // Keep the response open while paused. A server that ignored
                    // the range probe cannot be expected to accept a resumed GET.
                    while (_isPaused)
                        await Task.Delay(200, transferToken);
                    bytesRead = await contentStream.ReadAsync(buffer.AsMemory(0, buffer.Length), transferToken);
                    if (bytesRead == 0) break;
                    lock (_fileLock)
                    {
                        transferToken.ThrowIfCancellationRequested();
                        if (_fileStream != null)
                        {
                            _fileStream.Seek(session.DownloadedBytes, SeekOrigin.Begin);
                            _fileStream.Write(buffer, 0, bytesRead);
                        }
                    }

                    adapter.AddDownloadedBytes(bytesRead);
                    session.AddDownloadedBytes(bytesRead);

                    // Apply real-time bandwidth throttling if configured by user
                    // Speed limit applies ONLY to Home Wi-Fi / LAN — Mobile 5G runs 100% full unthrottled speed!
                    if (!adapter.IsMobilePhone)
                    {
                        await _throttler.ThrottleAsync(bytesRead, transferToken);
                    }

                    if (stopwatch.ElapsedMilliseconds >= 250)
                    {
                        double sec = stopwatch.Elapsed.TotalSeconds;
                        double instant = (session.DownloadedBytes - lastBytes) / sec;
                        session.UpdateSpeed(instant);
                        lastBytes = session.DownloadedBytes;
                        stopwatch.Restart();
                        ProgressUpdated?.Invoke(session);
                    }

                }

                // When the server does not provide a length, end at EOF instead
                // of issuing another GET forever after the first full response.
                if (session.TotalBytes <= 0) break;
                if (session.DownloadedBytes == bytesBeforeAttempt && session.DownloadedBytes < session.TotalBytes)
                {
                    consecutiveFailures++;
                    if (consecutiveFailures >= 8)
                        throw new IOException("The server repeatedly ended the response before the download completed.");
                    await Task.Delay(Math.Min(4000, 250 * (1 << Math.Min(consecutiveFailures, 4))), token);
                }
                else
                {
                    consecutiveFailures = 0;
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception) when (transferToken.IsCancellationRequested && !token.IsCancellationRequested)
            {
                // The adapter was turned off or disconnected. Retry with the
                // currently selected adapters and the last saved byte offset.
            }
            catch (Exception ex)
            {
                if (ex is InvalidOperationException && ex.Message.StartsWith("This server cannot resume", StringComparison.Ordinal))
                    throw;
                if (!_isPaused && !token.IsCancellationRequested)
                {
                    if (ex is HttpRequestException httpError && httpError.StatusCode is { } status && IsPermanentHttpFailure(status))
                        throw new InvalidOperationException($"The download server returned HTTP {(int)status}.");
                    consecutiveFailures++;
                    if (consecutiveFailures >= 8)
                        throw new IOException("The connection repeatedly failed without making progress. Please retry the download.");
                    LogReceived?.Invoke($"Stream notice ({adapter.Name}): {ex.Message}. Retrying...");
                    await Task.Delay(Math.Min(4000, 250 * (1 << Math.Min(consecutiveFailures, 4))), token);
                }
            }
        }
    }

    private sealed class AdapterTransferCancellation : IDisposable
    {
        private readonly NetworkAdapterInfo _adapter;
        private readonly CancellationTokenSource _source;
        public CancellationToken Token { get; }

        public AdapterTransferCancellation(NetworkAdapterInfo adapter, CancellationToken sessionToken)
        {
            _adapter = adapter;
            _source = CancellationTokenSource.CreateLinkedTokenSource(sessionToken);
            Token = _source.Token;
            _adapter.PropertyChanged += OnAdapterChanged;
            CancelIfUnavailable();
        }

        private void OnAdapterChanged(object? sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName is nameof(NetworkAdapterInfo.IsEnabled) or nameof(NetworkAdapterInfo.IsOnline))
                CancelIfUnavailable();
        }

        private void CancelIfUnavailable()
        {
            if (_adapter.IsEnabled && _adapter.IsOnline) return;
            try { _source.Cancel(); }
            catch (ObjectDisposedException) { }
        }

        public void Dispose()
        {
            _adapter.PropertyChanged -= OnAdapterChanged;
            _source.Dispose();
        }
    }

    private async Task<(string FileName, long TotalSize, bool SupportsRanges, bool Reachable)> ProbeUrlAsync(HttpClient client, string url, CancellationToken token)
    {
        string rawFileName = string.Empty;
        try
        {
            rawFileName = Path.GetFileName(new Uri(url).LocalPath);
        }
        catch { }

        if (string.IsNullOrWhiteSpace(rawFileName))
        {
            rawFileName = "downloaded_file.bin";
        }
        string fileName = SanitizeFileName(rawFileName);

        long totalSize = -1;
        bool supportsRanges = false;
        bool reachable = false;

        // Use a single-byte range probe to confirm range support without reading a large test body.
        try
        {
            using var rangeReq = new HttpRequestMessage(HttpMethod.Get, url);
            rangeReq.Headers.Range = new RangeHeaderValue(0, 0);
            using var rangeRes = await client.SendAsync(rangeReq, HttpCompletionOption.ResponseHeadersRead, token);
            reachable = rangeRes.IsSuccessStatusCode;

            if (rangeRes.StatusCode == System.Net.HttpStatusCode.PartialContent)
            {
                supportsRanges = rangeRes.Content.Headers.ContentRange is { From: 0, To: 0 };
                if (rangeRes.Content.Headers.ContentRange?.Length.HasValue == true)
                {
                    totalSize = rangeRes.Content.Headers.ContentRange.Length.Value;
                }

                if (rangeRes.Content.Headers.ContentDisposition?.FileName != null)
                {
                    fileName = SanitizeFileName(rangeRes.Content.Headers.ContentDisposition.FileName.Trim('"', '\''));
                }
            }
            else if (rangeRes.IsSuccessStatusCode)
            {
                if (rangeRes.Content.Headers.ContentLength.HasValue)
                {
                    totalSize = rangeRes.Content.Headers.ContentLength.Value;
                }
                // The server ignored the actual range request (200 OK). Do not
                // infer support from Accept-Ranges; this can corrupt chunks.
                supportsRanges = false;
                if (rangeRes.Content.Headers.ContentDisposition?.FileName != null)
                {
                    fileName = SanitizeFileName(rangeRes.Content.Headers.ContentDisposition.FileName.Trim('"', '\''));
                }
            }
        }
        catch (HttpRequestException ex)
        {
            LogReceived?.Invoke($"[Notice] Range probe check: {ex.Message}");
        }

        // Secondary fallback if size still not known: send HEAD request
        if (totalSize <= 0)
        {
            try
            {
                using var headReq = new HttpRequestMessage(HttpMethod.Head, url);
                using var headRes = await client.SendAsync(headReq, token);
                if (headRes.IsSuccessStatusCode)
                {
                    reachable = true;
                    if (headRes.Content.Headers.ContentLength.HasValue)
                    {
                        totalSize = headRes.Content.Headers.ContentLength.Value;
                    }
                    // HEAD may reveal total size, but only a ranged GET returning
                    // 206 proves byte-range support.
                    if (headRes.Content.Headers.ContentDisposition?.FileName != null)
                    {
                        fileName = SanitizeFileName(headRes.Content.Headers.ContentDisposition.FileName.Trim('"', '\''));
                    }
                }
            }
            catch (HttpRequestException) { }
        }

        return (fileName, totalSize, supportsRanges, reachable);
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = string.Concat(name.Split(invalid, StringSplitOptions.RemoveEmptyEntries));
        return string.IsNullOrWhiteSpace(clean) ? "downloaded_file.bin" : clean;
    }

    private static bool IsPermanentHttpFailure(Exception exception) =>
        exception is HttpRequestException { StatusCode: { } status } && IsPermanentHttpFailure(status);

    private static bool IsPermanentHttpFailure(System.Net.HttpStatusCode status) =>
        (int)status >= 400 && (int)status < 500 &&
        status != System.Net.HttpStatusCode.RequestTimeout &&
        (int)status != 429;

    public void Cancel()
    {
        _isPaused = false;
        if (_currentSession != null)
        {
            _currentSession.Status = DownloadStatus.Cancelled;
            _currentSession.CombinedSpeedBps = 0;
            // Physical adapter rates are independent of the file transfer.
            StatusChanged?.Invoke(_currentSession);
            ProgressUpdated?.Invoke(_currentSession);
            LogReceived?.Invoke("🛑 Download cancelled by user.");
        }
        _cts?.Cancel();
    }
}
