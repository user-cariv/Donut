using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using DonutBoost.Models;

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
    private volatile bool _isPaused = false;

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
            foreach (var adp in _bondingManager.Adapters)
            {
                adp.CurrentSpeedBps = 0;
            }
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
            StatusChanged?.Invoke(_currentSession);
            LogReceived?.Invoke("▶ Resuming download pipeline...");
        }
    }

    public async Task StartDownloadAsync(string url, string outputFolder, CancellationToken externalToken = default)
    {
        _cts?.Cancel();
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

            // 1. Check enabled adapters
            var allAdapters = _bondingManager.Adapters.Where(a => a.IsOnline && a.LocalIp != null).ToList();
            var enabledAdapters = allAdapters.Where(a => a.IsEnabled).ToList();
            if (enabledAdapters.Count == 0)
            {
                throw new InvalidOperationException("No active network adapters selected! Please ensure Wi-Fi is connected or plug in your phone with USB Tethering.");
            }

            LogReceived?.Invoke($"Found {allAdapters.Count} network connection(s) available for download ({enabledAdapters.Count} enabled):");
            foreach (var adp in allAdapters)
            {
                LogReceived?.Invoke($"  → [{adp.IconGlyph} {adp.Name}] ({adp.TypeLabel}) IP: {adp.LocalIp} - Enabled: {adp.IsEnabled}");
            }

            // Reset adapter metrics for fresh download
            foreach (var adp in allAdapters)
            {
                adp.TotalBytesDownloaded = 0;
                adp.CurrentSpeedBps = 0;
            }

            // Prefer enabled Wi-Fi or first active adapter for metadata probe
            var probeAdapter = enabledAdapters.FirstOrDefault(a => a.IsWifi) ?? enabledAdapters[0];
            var probeClient = _bondingManager.GetOrCreateClient(probeAdapter);

            var (fileName, totalSize, supportsRanges) = await ProbeUrlAsync(probeClient, url, token);

            session.FileName = fileName;
            session.TotalBytes = totalSize;
            session.SupportsRangeRequests = supportsRanges;
            session.DestinationPath = Path.Combine(outputFolder, fileName);

            LogReceived?.Invoke($"File: {fileName} | Size: {session.FormattedTotalSize} | Supports Range Slicing: {supportsRanges}");

            // Pre-allocate destination file
            Directory.CreateDirectory(outputFolder);
            lock (_fileLock)
            {
                _fileStream = new FileStream(session.DestinationPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite, 64 * 1024, useAsync: true);
                if (totalSize > 0)
                {
                    _fileStream.SetLength(totalSize);
                }
            }

            session.Status = DownloadStatus.Downloading;
            StatusChanged?.Invoke(session);

            // 2. Schedule Chunks across all available adapters for dynamic live switching
            if (supportsRanges && totalSize > 0 && allAdapters.Count > 0)
            {
                await DownloadMultiChannelAsync(session, allAdapters, token);
            }
            else
            {
                LogReceived?.Invoke("Server does not support Range slicing. Downloading via single stream...");
                await DownloadSingleStreamAsync(session, enabledAdapters[0], token);
            }

            if (token.IsCancellationRequested || session.Status == DownloadStatus.Cancelled)
            {
                session.Status = DownloadStatus.Cancelled;
                session.CombinedSpeedBps = 0;
                foreach (var adp in _bondingManager.Adapters)
                {
                    adp.CurrentSpeedBps = 0;
                }
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
        catch (OperationCanceledException)
        {
            session.Status = DownloadStatus.Cancelled;
            StatusChanged?.Invoke(session);
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
        }
    }

    private async Task DownloadMultiChannelAsync(DownloadSession session, List<NetworkAdapterInfo> adapters, CancellationToken token)
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
        var adapterSpeedTrackers = adapters.ToDictionary(a => a.Id, a => a.TotalBytesDownloaded);

        // Background monitor for live speed calculation
        var monitorTask = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested && session.Status != DownloadStatus.Completed && session.Status != DownloadStatus.Failed && session.Status != DownloadStatus.Cancelled)
            {
                await Task.Delay(250, token);
                
                if (_isPaused)
                {
                    session.UpdateSpeed(0);
                    foreach (var adp in adapters)
                    {
                        adp.CurrentSpeedBps = 0;
                        adp.NotifyMetricsChanged();
                    }
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

                    // Update real-time speed for each physical adapter smoothly
                    foreach (var adapter in adapters)
                    {
                        if (adapterSpeedTrackers.TryGetValue(adapter.Id, out long lastCount))
                        {
                            long adapterBytes = adapter.TotalBytesDownloaded;
                            long adapterDelta = Math.Max(0, adapterBytes - lastCount);
                            adapterSpeedTrackers[adapter.Id] = adapterBytes;
                            adapter.CurrentSpeedBps = (adapter.IsEnabled && adapter.IsOnline) ? (adapterDelta / elapsedSeconds) : 0;
                            adapter.NotifyMetricsChanged();
                        }
                    }

                    ProgressUpdated?.Invoke(session);
                }
            }
        }, token);

        // Worker tasks per network adapter (12 workers for Mobile 5G, 8 for Wi-Fi for maximum 300+ Mbps throughput)
        var workerTasks = new List<Task>();
        var trackedAdapterIds = new HashSet<string>(adapters.Select(a => a.Id));
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
                                adp.CurrentSpeedBps = 0;
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
                            try
                            {
                                await DownloadChunkWithClientAsync(client, session.Url, chunk, adp, session, token);
                                success = true;
                            }
                            catch (Exception ex)
                            {
                                if (!token.IsCancellationRequested)
                                {
                                    string msg = $"[{adp.Name}] Notice on chunk {chunk.Index}: {ex.Message}";
                                    LogReceived?.Invoke(msg);
                                    await Task.Delay(500, token);
                                }
                            }

                            // Seamless Handover: If chunk is not completed (e.g. paused, errored, or connection turned OFF mid-download),
                            // return it to pendingQueue immediately so an active connection picks it up right where it left off!
                            if (!chunk.IsCompleted && !token.IsCancellationRequested)
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
        }

        session.UpdateSpeed(0);
        foreach (var adp in adapters)
        {
            adp.CurrentSpeedBps = 0;
            adp.NotifyMetricsChanged();
        }
        ProgressUpdated?.Invoke(session);
    }

    private async Task DownloadChunkWithClientAsync(HttpClient client, string url, DownloadChunk chunk, NetworkAdapterInfo adapter, DownloadSession session, CancellationToken token)
    {
        // Support resuming chunk from exact downloaded offset
        long startFrom = chunk.StartOffset + chunk.BytesDownloaded;
        if (startFrom > chunk.EndOffset)
        {
            chunk.IsCompleted = true;
            return;
        }

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Range = new RangeHeaderValue(startFrom, chunk.EndOffset);

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();

        if (startFrom > 0 && response.StatusCode != System.Net.HttpStatusCode.PartialContent)
        {
            throw new InvalidOperationException($"Server returned status {response.StatusCode} instead of 206 Partial Content for byte-range slice.");
        }

        using var contentStream = await response.Content.ReadAsStreamAsync(token);
        byte[] buffer = new byte[256 * 1024]; // 256 KB high-throughput buffer for 300+ Mbps 5G & gigabit streams
        long writeOffset = startFrom;

        int bytesRead;
        while ((bytesRead = await contentStream.ReadAsync(buffer.AsMemory(0, buffer.Length), token)) > 0)
        {
            lock (_fileLock)
            {
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

            // Apply real-time bandwidth throttling if configured by user
            // Speed limit applies ONLY to Home Wi-Fi / LAN — Mobile 5G runs 100% full unthrottled speed!
            if (!adapter.IsMobilePhone)
            {
                await _throttler.ThrottleAsync(bytesRead, token);
            }

            // If paused mid-read or connection turned OFF mid-download, exit chunk gracefully so it can hand off seamlessly
            if (_isPaused || !adapter.IsEnabled || !adapter.IsOnline)
            {
                return;
            }
        }

        if (chunk.BytesDownloaded >= chunk.TotalBytes)
        {
            chunk.IsCompleted = true;
        }
    }

    private async Task DownloadSingleStreamAsync(DownloadSession session, NetworkAdapterInfo adapter, CancellationToken token)
    {
        var client = _bondingManager.GetOrCreateClient(adapter);

        while (!token.IsCancellationRequested && (session.TotalBytes <= 0 || session.DownloadedBytes < session.TotalBytes))
        {
            // Support pausing and resuming single stream from exact byte position
            while (_isPaused && !token.IsCancellationRequested)
            {
                await Task.Delay(200, token);
            }

            if (token.IsCancellationRequested) break;

            var request = new HttpRequestMessage(HttpMethod.Get, session.Url);
            if (session.DownloadedBytes > 0)
            {
                request.Headers.Range = new RangeHeaderValue(session.DownloadedBytes, null);
            }

            try
            {
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
                response.EnsureSuccessStatusCode();

                using var contentStream = await response.Content.ReadAsStreamAsync(token);
                byte[] buffer = new byte[256 * 1024]; // 256 KB high-throughput buffer for 300+ Mbps 5G & gigabit streams
                int bytesRead;
                var stopwatch = Stopwatch.StartNew();
                long lastBytes = session.DownloadedBytes;

                while ((bytesRead = await contentStream.ReadAsync(buffer.AsMemory(0, buffer.Length), token)) > 0)
                {
                    lock (_fileLock)
                    {
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
                        await _throttler.ThrottleAsync(bytesRead, token);
                    }

                    if (stopwatch.ElapsedMilliseconds >= 250)
                    {
                        double sec = stopwatch.Elapsed.TotalSeconds;
                        double instant = (session.DownloadedBytes - lastBytes) / sec;
                        session.UpdateSpeed(instant);
                        adapter.CurrentSpeedBps = instant;
                        lastBytes = session.DownloadedBytes;
                        stopwatch.Restart();
                        ProgressUpdated?.Invoke(session);
                    }

                    if (_isPaused)
                    {
                        // Cleanly disconnect socket during pause so server doesn't timeout
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                if (!_isPaused && !token.IsCancellationRequested)
                {
                    LogReceived?.Invoke($"Stream notice ({adapter.Name}): {ex.Message}. Retrying...");
                    await Task.Delay(1000, token);
                }
            }
        }
    }

    private async Task<(string fileName, long totalSize, bool supportsRanges)> ProbeUrlAsync(HttpClient client, string url, CancellationToken token)
    {
        string rawFileName = string.Empty;
        try
        {
            string urlToParse = url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                ? url
                : "https://" + url;
            rawFileName = Path.GetFileName(new Uri(urlToParse).LocalPath);
        }
        catch { }

        if (string.IsNullOrWhiteSpace(rawFileName))
        {
            rawFileName = "downloaded_file.bin";
        }
        string fileName = SanitizeFileName(rawFileName);

        long totalSize = -1;
        bool supportsRanges = false;

        // Primary Probe: Test byte-range request directly with a 1MB test slice (Range: bytes=0-1048575)
        // Testing a non-zero range slice guarantees Google Drive, Google UserContent, Hetzner, and CDNs return 206 Partial Content!
        try
        {
            var rangeReq = new HttpRequestMessage(HttpMethod.Get, url);
            rangeReq.Headers.Range = new RangeHeaderValue(0, 1024 * 1024 - 1);
            using var rangeRes = await client.SendAsync(rangeReq, HttpCompletionOption.ResponseHeadersRead, token);

            if (rangeRes.StatusCode == System.Net.HttpStatusCode.PartialContent)
            {
                supportsRanges = true;
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
                if (rangeRes.Headers.AcceptRanges.Contains("bytes") || rangeRes.Content.Headers.ContentRange != null)
                {
                    supportsRanges = true;
                }
                if (rangeRes.Content.Headers.ContentDisposition?.FileName != null)
                {
                    fileName = SanitizeFileName(rangeRes.Content.Headers.ContentDisposition.FileName.Trim('"', '\''));
                }
            }
        }
        catch (Exception ex)
        {
            LogReceived?.Invoke($"[Notice] Range probe check: {ex.Message}");
        }

        // Secondary fallback if size still not known: send HEAD request
        if (totalSize <= 0)
        {
            try
            {
                var headReq = new HttpRequestMessage(HttpMethod.Head, url);
                using var headRes = await client.SendAsync(headReq, token);
                if (headRes.IsSuccessStatusCode)
                {
                    if (headRes.Content.Headers.ContentLength.HasValue)
                    {
                        totalSize = headRes.Content.Headers.ContentLength.Value;
                    }
                    if (headRes.Headers.AcceptRanges.Contains("bytes") || headRes.Content.Headers.ContentRange != null)
                    {
                        supportsRanges = true;
                    }
                    if (headRes.Content.Headers.ContentDisposition?.FileName != null)
                    {
                        fileName = SanitizeFileName(headRes.Content.Headers.ContentDisposition.FileName.Trim('"', '\''));
                    }
                }
            }
            catch { }
        }

        // Auto-enable range slicing for large files (> 5MB)
        // Google Drive, Hdhub4u, CDNs, and media hosts support 206 Partial Content on actual chunk requests!
        if (!supportsRanges && totalSize > 5 * 1024 * 1024)
        {
            supportsRanges = true;
        }

        return (fileName, totalSize, supportsRanges);
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = string.Concat(name.Split(invalid, StringSplitOptions.RemoveEmptyEntries));
        return string.IsNullOrWhiteSpace(clean) ? "downloaded_file.bin" : clean;
    }

    public void Cancel()
    {
        _isPaused = false;
        if (_currentSession != null)
        {
            _currentSession.Status = DownloadStatus.Cancelled;
            _currentSession.CombinedSpeedBps = 0;
            foreach (var adp in _bondingManager.Adapters)
            {
                adp.CurrentSpeedBps = 0;
            }
            StatusChanged?.Invoke(_currentSession);
            ProgressUpdated?.Invoke(_currentSession);
            LogReceived?.Invoke("🛑 Download cancelled by user.");
        }
        _cts?.Cancel();
    }
}
