using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using DonutBoost.Models;

namespace DonutBoost.Engine;

/// <summary>
/// Game Launcher Accelerator & Live Network Traffic Monitor.
/// Auto-detects active game downloads (Steam, Epic Games, EA, Rockstar, Battle.net)
/// and monitors real-time multi-channel bandwidth across Home Wi-Fi and Mobile Data
/// without hijacking the Windows system proxy or breaking browser downloads.
/// </summary>
public class GameBoostProxyServer : IDisposable
{
    private readonly NetworkBondingManager _bondingManager;
    private readonly LicenseManager? _licenseManager;
    private CancellationTokenSource? _cts;
    private volatile bool _isRunning = false;

    private long _totalBytesDownloaded = 0;
    private int _activeConnections = 0;
    private string _detectedLauncher = "Game Launcher";
    private long _detectedGameSizeBytes = 0;
    private string _detectedGameName = "";

    public bool IsRunning => _isRunning;
    public int Port => 9090;
    public long TotalBytesDownloaded => Interlocked.Read(ref _totalBytesDownloaded);
    public int ActiveConnections => _activeConnections;
    public long DetectedGameSizeBytes => _detectedGameSizeBytes;
    public string DetectedGameName => _detectedGameName;

    public event Action<long, double, int, string>? ProxyMetricsUpdated;
    public event Action<string>? LogReceived;

    // Known game launcher process names
    private static readonly string[] GameLauncherProcesses = new[]
    {
        "EpicGamesLauncher",
        "UnrealCEFSubProcess",
        "steam",
        "steamwebhelper",
        "EADesktop",
        "EALauncher",
        "Link2EA",
        "Battle.net",
        "Agent",
        "RockstarService",
        "Launcher",
        "SocialClubHelper",
        "upc",
        "uplay",
        "UbisoftConnect",
        "XboxApp",
        "GamingServices",
        "GOG Galaxy",
        "GalaxyClient",
        "RiotClientServices"
    };

    public GameBoostProxyServer(NetworkBondingManager bondingManager, LicenseManager? licenseManager = null)
    {
        _bondingManager = bondingManager;
        _licenseManager = licenseManager;
    }

    public bool Start(int port = 9090)
    {
        if (_isRunning) return true;

        if (_licenseManager != null && !_licenseManager.ValidateActiveSessionToken())
        {
            LogReceived?.Invoke("🔒 Security: Active valid license required to use Game Launcher Accelerator.");
            return false;
        }

        try
        {
            _isRunning = true;
            _cts = new CancellationTokenSource();
            _ = MonitorGameTrafficLoopAsync(_cts.Token);

            LogReceived?.Invoke("🎮 Game Launcher Accelerator active. Monitoring Steam, Epic Games, EA, Rockstar with Home Wi-Fi & Mobile 5G.");
            return true;
        }
        catch (Exception ex)
        {
            LogReceived?.Invoke($"✕ Failed to start Game Launcher Accelerator: {ex.Message}");
            _isRunning = false;
            return false;
        }
    }

    public void Stop()
    {
        if (!_isRunning) return;

        _isRunning = false;
        try
        {
            _cts?.Cancel();
        }
        catch { }

        LogReceived?.Invoke("🛑 Game Launcher Accelerator stopped.");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetProcessIoCounters(IntPtr hProcess, out IO_COUNTERS counters);

    private static readonly ConcurrentDictionary<int, ulong> _processLastWriteBytes = new();

    private class LauncherCandidate
    {
        public string Name { get; set; } = "";
        public List<Process> Processes { get; set; } = new();
        public double CurrentWriteSpeedBps { get; set; }
    }

    private static (string ActiveLauncher, string SummaryLabel, int ActiveStreams) DetectLauncher(Process[] processes, double elapsedSec)
    {
        var candidates = new List<LauncherCandidate>();

        // 1. Steam
        var steam = processes.Where(p => {
            try { return p.ProcessName.Contains("steam", StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }).ToList();
        if (steam.Count > 0) candidates.Add(new LauncherCandidate { Name = "Steam", Processes = steam });

        // 2. Epic Games
        var epic = processes.Where(p => {
            try { return p.ProcessName.Contains("Epic", StringComparison.OrdinalIgnoreCase) || p.ProcessName.Contains("UnrealCEF", StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }).ToList();
        if (epic.Count > 0) candidates.Add(new LauncherCandidate { Name = "Epic Games", Processes = epic });

        // 3. Rockstar Games
        var rockstar = processes.Where(p => {
            try { return p.ProcessName.Contains("Rockstar", StringComparison.OrdinalIgnoreCase) || p.ProcessName.Contains("SocialClub", StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }).ToList();
        if (rockstar.Count > 0) candidates.Add(new LauncherCandidate { Name = "Rockstar Games", Processes = rockstar });

        // 4. EA App / Origin
        var ea = processes.Where(p => {
            try { return p.ProcessName.StartsWith("EA", StringComparison.OrdinalIgnoreCase) || p.ProcessName.Contains("Link2EA", StringComparison.OrdinalIgnoreCase) || p.ProcessName.Contains("Origin", StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }).ToList();
        if (ea.Count > 0) candidates.Add(new LauncherCandidate { Name = "EA App", Processes = ea });

        // 5. Battle.net
        var bnet = processes.Where(p => {
            try { return p.ProcessName.Contains("Battle.net", StringComparison.OrdinalIgnoreCase) || p.ProcessName.Contains("Agent", StringComparison.OrdinalIgnoreCase) || p.ProcessName.Contains("Blizzard", StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }).ToList();
        if (bnet.Count > 0) candidates.Add(new LauncherCandidate { Name = "Battle.net", Processes = bnet });

        // 6. Ubisoft Connect
        var ubi = processes.Where(p => {
            try { return p.ProcessName.Contains("upc", StringComparison.OrdinalIgnoreCase) || p.ProcessName.Contains("Ubisoft", StringComparison.OrdinalIgnoreCase) || p.ProcessName.Contains("uplay", StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }).ToList();
        if (ubi.Count > 0) candidates.Add(new LauncherCandidate { Name = "Ubisoft Connect", Processes = ubi });

        // 7. Riot Games
        var riot = processes.Where(p => {
            try { return p.ProcessName.Contains("Riot", StringComparison.OrdinalIgnoreCase) || p.ProcessName.Contains("LeagueClient", StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }).ToList();
        if (riot.Count > 0) candidates.Add(new LauncherCandidate { Name = "Riot Games", Processes = riot });

        // 8. Xbox App
        var xbox = processes.Where(p => {
            try { return p.ProcessName.Contains("GamingServices", StringComparison.OrdinalIgnoreCase) || p.ProcessName.Contains("XboxApp", StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }).ToList();
        if (xbox.Count > 0) candidates.Add(new LauncherCandidate { Name = "Xbox App", Processes = xbox });

        // 9. GOG Galaxy
        var gog = processes.Where(p => {
            try { return p.ProcessName.Contains("Galaxy", StringComparison.OrdinalIgnoreCase) || p.ProcessName.Contains("GOG", StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }).ToList();
        if (gog.Count > 0) candidates.Add(new LauncherCandidate { Name = "GOG Galaxy", Processes = gog });

        if (candidates.Count == 0)
        {
            return ("", "Ready", 0);
        }

        // Measure disk write throughput for each running launcher process
        foreach (var c in candidates)
        {
            double totalWriteBps = 0;
            foreach (var p in c.Processes)
            {
                try
                {
                    if (GetProcessIoCounters(p.Handle, out var io))
                    {
                        ulong curr = io.WriteTransferCount;
                        if (_processLastWriteBytes.TryGetValue(p.Id, out ulong prev))
                        {
                            _processLastWriteBytes[p.Id] = curr;
                            if (curr >= prev && elapsedSec > 0)
                            {
                                totalWriteBps += (curr - prev) / elapsedSec;
                            }
                        }
                        else
                        {
                            _processLastWriteBytes[p.Id] = curr;
                        }
                    }
                }
                catch { }
            }
            c.CurrentWriteSpeedBps = totalWriteBps;
        }

        // The active downloading launcher is the one with highest disk write activity (minimum 50 KB/s)
        var activeDownloading = candidates
            .Where(c => c.CurrentWriteSpeedBps >= 50 * 1024)
            .OrderByDescending(c => c.CurrentWriteSpeedBps)
            .FirstOrDefault();

        if (activeDownloading != null)
        {
            return (activeDownloading.Name, activeDownloading.Name, activeDownloading.Processes.Count);
        }

        // If no launcher is writing at > 50 KB/s, check active install files
        if (candidates.Any(c => c.Name == "Epic Games"))
        {
            var (eName, _) = DetectEpicDownloadSize();
            if (!string.IsNullOrEmpty(eName))
            {
                var epicCandidate = candidates.First(c => c.Name == "Epic Games");
                return ("Epic Games", "Epic Games", epicCandidate.Processes.Count);
            }
        }

        // In Standby (none actively downloading), report unified "Game Launcher Standby"
        return ("Game Launcher", "Game Launcher Standby", candidates.Sum(c => c.Processes.Count));
    }

    private string _summaryLauncherLabel = "Ready";

    private async Task MonitorGameTrafficLoopAsync(CancellationToken token)
    {
        var lastInterfaceBytes = new ConcurrentDictionary<string, long>();
        var lastCheckTime = Stopwatch.StartNew();
        var processCheckStopwatch = Stopwatch.StartNew();
        var activeTrafficStopwatch = Stopwatch.StartNew();
        var sizeDetectionStopwatch = Stopwatch.StartNew();
        activeTrafficStopwatch.Restart();
        double smoothedSpeedBps = 0;

        bool isGameActive = false;
        int activeStreamsCount = 0;

        while (!token.IsCancellationRequested && _isRunning)
        {
            await Task.Delay(250, token);

            double loopElapsedSeconds = lastCheckTime.Elapsed.TotalSeconds;

            // 1. Check for active game launcher processes every 1.5 seconds to save CPU
            if (processCheckStopwatch.ElapsedMilliseconds >= 1500)
            {
                processCheckStopwatch.Restart();
                try
                {
                    var runningProcesses = Process.GetProcesses();
                    var (activeLauncher, summaryLabel, count) = DetectLauncher(runningProcesses, Math.Max(0.5, loopElapsedSeconds));

                    if (!string.IsNullOrEmpty(activeLauncher))
                    {
                        isGameActive = true;
                        _detectedLauncher = activeLauncher;
                        _summaryLauncherLabel = summaryLabel;
                        activeStreamsCount = count;
                    }
                    else
                    {
                        isGameActive = false;
                        activeStreamsCount = 0;
                        _activeConnections = 0;
                        _detectedLauncher = "Ready";
                        _summaryLauncherLabel = "Ready";
                    }
                }
                catch { }
            }

            // Periodic License Validation
            if (_licenseManager != null && !_licenseManager.ValidateActiveSessionToken())
            {
                LogReceived?.Invoke("🔒 Security: Active valid license required. Accelerator suspended.");
                Stop();
                break;
            }

            // 2. Measure actual hardware network throughput across physical adapters
            double elapsedSeconds = lastCheckTime.Elapsed.TotalSeconds;
            if (elapsedSeconds < 0.25) continue;
            lastCheckTime.Restart();

            double totalInstantBps = 0;
            var currentNics = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .ToList();

            var adapterSamples = new List<(NetworkAdapterInfo Adapter, long DeltaBytes, double SpeedBps)>();

            foreach (var adapter in _bondingManager.Adapters)
            {
                var nic = currentNics.FirstOrDefault(n => n.Id == adapter.Id || n.Name.Equals(adapter.Name, StringComparison.OrdinalIgnoreCase));
                if (nic == null) continue;

                try
                {
                    var stats = nic.GetIPStatistics();
                    long currentBytes = stats.BytesReceived;

                    if (lastInterfaceBytes.TryGetValue(adapter.Id, out long previousBytes))
                    {
                        long deltaBytes = Math.Max(0, currentBytes - previousBytes);
                        double adapterSpeedBps = deltaBytes / elapsedSeconds;

                        // Only assign speed if adapter is online and enabled
                        if (adapter.IsEnabled && adapter.IsOnline)
                        {
                            adapter.CurrentSpeedBps = (adapter.CurrentSpeedBps <= 0)
                                ? adapterSpeedBps
                                : (adapter.CurrentSpeedBps * 0.70) + (adapterSpeedBps * 0.30);

                            if (adapter.CurrentSpeedBps < 5000) adapter.CurrentSpeedBps = 0;

                            totalInstantBps += adapterSpeedBps;
                            adapterSamples.Add((adapter, deltaBytes, adapterSpeedBps));
                        }
                    }

                    lastInterfaceBytes[adapter.Id] = currentBytes;
                }
                catch { }
            }

            // 3. Genuine Game Download Threshold (100 KB/s = 800 Kbps)
            // Low background telemetry (pings, store browsing, Steam chat < 100 KB/s) is treated as Standby.
            const double MinActiveDownloadBps = 100 * 1024;
            bool isGenuineDownload = isGameActive && totalInstantBps >= MinActiveDownloadBps;

            if (isGenuineDownload)
            {
                activeTrafficStopwatch.Restart();
                // 1-second rolling EMA to match Steam's 1-second counter and avoid erratic >300 spikes
                smoothedSpeedBps = (smoothedSpeedBps <= 0) 
                    ? totalInstantBps 
                    : (smoothedSpeedBps * 0.72) + (totalInstantBps * 0.28);

                // Add to per-adapter downloaded bytes and total downloaded bytes
                foreach (var sample in adapterSamples)
                {
                    if (sample.DeltaBytes > 0)
                    {
                        sample.Adapter.AddDownloadedBytes(sample.DeltaBytes);
                        sample.Adapter.NotifyMetricsChanged();
                    }
                }

                long frameBytes = (long)(totalInstantBps * elapsedSeconds);
                if (frameBytes > 0)
                {
                    Interlocked.Add(ref _totalBytesDownloaded, frameBytes);
                }
            }
            else if (activeTrafficStopwatch.ElapsedMilliseconds < 3500)
            {
                // Smooth decay during momentary disk commit or TCP renegotiation
                smoothedSpeedBps = Math.Max(0, smoothedSpeedBps * 0.82);
            }

            // If active game traffic occurred within the last 3.5 seconds, maintain active acceleration display
            if (isGameActive && activeTrafficStopwatch.ElapsedMilliseconds < 3500 && (totalInstantBps >= MinActiveDownloadBps || smoothedSpeedBps >= MinActiveDownloadBps))
            {
                _activeConnections = Math.Max(4, Math.Min(16, activeStreamsCount * 2));
                // Use smoothedSpeedBps so speed matches Steam's 1-second counter without spiking above 300 on tiny 250ms bursts
                double reportSpeed = smoothedSpeedBps;

                // Throttled detection: Check for auto-detected game download sizes from Steam / Epic only once every 4s to eliminate disk thrashing
                if (_detectedGameSizeBytes <= 0 || sizeDetectionStopwatch.ElapsedMilliseconds >= 4000)
                {
                    sizeDetectionStopwatch.Restart();
                    if (_detectedLauncher == "Steam")
                    {
                        var (sName, sBytes) = DetectSteamDownloadSize();
                        if (sBytes > 0) _detectedGameSizeBytes = sBytes;
                        if (!string.IsNullOrEmpty(sName)) _detectedGameName = sName;
                    }
                    else if (_detectedLauncher == "Epic Games")
                    {
                        var (eName, eBytes) = DetectEpicDownloadSize();
                        if (eBytes > 0) _detectedGameSizeBytes = eBytes;
                        if (!string.IsNullOrEmpty(eName)) _detectedGameName = eName;
                    }
                }

                string displayLabel = !string.IsNullOrEmpty(_detectedGameName) 
                    ? $"{_detectedLauncher} - {_detectedGameName}" 
                    : _detectedLauncher;

                ProxyMetricsUpdated?.Invoke(TotalBytesDownloaded, reportSpeed, _activeConnections, displayLabel);
            }
            else
            {
                // Launcher is open but in idle standby (no active game download taking place)
                smoothedSpeedBps = 0;
                _activeConnections = 0;
                _detectedGameName = "";
                _detectedGameSizeBytes = 0;
                ProxyMetricsUpdated?.Invoke(TotalBytesDownloaded, 0, 0, _summaryLauncherLabel);
            }
        }
    }

    private static (string GameName, long TotalBytes) DetectSteamDownloadSize()
    {
        try
        {
            // 1. Locate Steam installation directory across common locations & registry
            var searchPaths = new List<string>();
            try
            {
                if (Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) is string hklmPath && !string.IsNullOrEmpty(hklmPath))
                    searchPaths.Add(hklmPath);
            }
            catch { }
            try
            {
                if (Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) is string hkcuPath && !string.IsNullOrEmpty(hkcuPath))
                    searchPaths.Add(hkcuPath.Replace('/', '\\'));
            }
            catch { }
            searchPaths.Add(@"C:\Program Files (x86)\Steam");
            searchPaths.Add(@"C:\Program Files\Steam");

            string? steamRoot = searchPaths.FirstOrDefault(Directory.Exists);
            if (string.IsNullOrEmpty(steamRoot)) return ("", 0);

            // 2. Discover all Steam library folders from libraryfolders.vdf
            var libraryFolders = new List<string> { Path.Combine(steamRoot, "steamapps") };
            string vdfPath = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdfPath))
            {
                try
                {
                    string vdfContent = File.ReadAllText(vdfPath);
                    var matches = Regex.Matches(vdfContent, @"""path""\s+""([^""]+)""");
                    foreach (Match m in matches)
                    {
                        string p = m.Groups[1].Value.Replace(@"\\", @"\");
                        string appsPath = Path.Combine(p, "steamapps");
                        if (Directory.Exists(appsPath) && !libraryFolders.Contains(appsPath, StringComparer.OrdinalIgnoreCase))
                        {
                            libraryFolders.Add(appsPath);
                        }
                    }
                }
                catch { }
            }

            // 3. Scan library folders for actively downloading games
            foreach (var lib in libraryFolders)
            {
                string downloadingDir = Path.Combine(lib, "downloading");
                var activeAppIds = new List<string>();

                if (Directory.Exists(downloadingDir))
                {
                    try
                    {
                        foreach (var sub in Directory.GetDirectories(downloadingDir))
                        {
                            string appid = Path.GetFileName(sub);
                            if (long.TryParse(appid, out _)) activeAppIds.Add(appid);
                        }
                    }
                    catch { }
                }

                // Check manifests for active appids first
                foreach (var appid in activeAppIds)
                {
                    string acf = Path.Combine(lib, $"appmanifest_{appid}.acf");
                    if (File.Exists(acf))
                    {
                        var res = ParseSteamManifest(acf);
                        if (res.TotalBytes > 0 || !string.IsNullOrEmpty(res.GameName))
                            return res;
                    }
                }

                // Check all appmanifest_*.acf sorted by most recently modified
                try
                {
                    var acfFiles = new DirectoryInfo(lib).GetFiles("appmanifest_*.acf")
                        .OrderByDescending(f => f.LastWriteTimeUtc)
                        .ToList();

                    foreach (var file in acfFiles)
                    {
                        var res = ParseSteamManifest(file.FullName);
                        if (res.TotalBytes > 0)
                            return res;
                    }
                }
                catch { }
            }

            // Fallback: check content_log.txt for chunk stream
            string logPath = Path.Combine(steamRoot, "logs", "content_log.txt");
            if (File.Exists(logPath))
            {
                using var fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var sr = new StreamReader(fs);
                var lines = new List<string>();
                while (!sr.EndOfStream)
                {
                    string line = sr.ReadLine() ?? "";
                    lines.Add(line);
                    if (lines.Count > 30) lines.RemoveAt(0);
                }

                for (int i = lines.Count - 1; i >= 0; i--)
                {
                    if (lines[i].Contains("Current download rate: 0.000 Mbps", StringComparison.OrdinalIgnoreCase) ||
                        lines[i].Contains("Connection has been idle", StringComparison.OrdinalIgnoreCase))
                    {
                        return ("", 0);
                    }

                    if (lines[i].Contains("Downloading", StringComparison.OrdinalIgnoreCase) &&
                        lines[i].Contains("chunks for depot", StringComparison.OrdinalIgnoreCase))
                    {
                        return ("Game Stream", 0);
                    }
                }
            }
        }
        catch { }
        return ("", 0);
    }

    private static (string GameName, long TotalBytes) ParseSteamManifest(string acfPath)
    {
        try
        {
            using var fs = new FileStream(acfPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var sr = new StreamReader(fs);
            string content = sr.ReadToEnd();

            string name = "";
            var nameMatch = Regex.Match(content, @"""name""\s+""([^""]+)""");
            if (nameMatch.Success) name = nameMatch.Groups[1].Value;

            long bytesStage = 0;
            var stageMatch = Regex.Match(content, @"""BytesToStage""\s+""(\d+)""");
            if (stageMatch.Success && long.TryParse(stageMatch.Groups[1].Value, out long s)) bytesStage = s;

            long bytesDownload = 0;
            var dlMatch = Regex.Match(content, @"""BytesToDownload""\s+""(\d+)""");
            if (dlMatch.Success && long.TryParse(dlMatch.Groups[1].Value, out long d)) bytesDownload = d;

            long sizeOnDisk = 0;
            var sizeMatch = Regex.Match(content, @"""SizeOnDisk""\s+""(\d+)""");
            if (sizeMatch.Success && long.TryParse(sizeMatch.Groups[1].Value, out long sd)) sizeOnDisk = sd;

            int stateFlags = 0;
            var flagMatch = Regex.Match(content, @"""StateFlags""\s+""(\d+)""");
            if (flagMatch.Success && int.TryParse(flagMatch.Groups[1].Value, out int sf)) stateFlags = sf;

            // Prioritize the user-visible full game size (e.g. 70 GB), or the stage / network download size
            long effectiveSize = Math.Max(sizeOnDisk, Math.Max(bytesStage, bytesDownload));

            if (effectiveSize > 0 && ((stateFlags & (4 | 1024)) != 0 || bytesDownload > 0 || bytesStage > 0))
            {
                return (string.IsNullOrEmpty(name) ? "Steam Game" : name, effectiveSize);
            }
        }
        catch { }
        return ("", 0);
    }

    private static (string GameName, long TotalBytes) DetectEpicDownloadSize()
    {
        try
        {
            // 1. Epic Games Launcher active manifests
            string manifestsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Epic", "EpicGamesLauncher", "Data", "Manifests");

            if (Directory.Exists(manifestsPath))
            {
                var files = new DirectoryInfo(manifestsPath).GetFiles("*.item")
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .ToList();

                foreach (var file in files)
                {
                    try
                    {
                        using var fs = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        using var sr = new StreamReader(fs);
                        string content = sr.ReadToEnd();

                        bool isIncomplete = content.Contains("\"bIsIncompleteInstall\": true", StringComparison.OrdinalIgnoreCase) ||
                                            content.Contains("\"bIsIncompleteInstall\":true", StringComparison.OrdinalIgnoreCase);

                        string name = "";
                        var nameMatch = Regex.Match(content, @"""DisplayName""\s*:\s*""([^""]+)""");
                        if (nameMatch.Success) name = nameMatch.Groups[1].Value;

                        long installSize = 0;
                        var instMatch = Regex.Match(content, @"""InstallSize""\s*:\s*(\d+)");
                        if (instMatch.Success && long.TryParse(instMatch.Groups[1].Value, out long isz)) installSize = isz;

                        long downloadSize = 0;
                        var dlMatch = Regex.Match(content, @"""DownloadSize""\s*:\s*(\d+)");
                        if (dlMatch.Success && long.TryParse(dlMatch.Groups[1].Value, out long dsz)) downloadSize = dsz;

                        long targetSize = Math.Max(installSize, downloadSize);

                        if (isIncomplete && (targetSize > 0 || !string.IsNullOrEmpty(name)))
                        {
                            return (string.IsNullOrEmpty(name) ? "Epic Game" : name, targetSize);
                        }
                    }
                    catch { }
                }
            }

            // 2. Secondary check: installations.jsonl
            string jsonlPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "com.epicgames.eglv2", "installations.jsonl");

            if (File.Exists(jsonlPath))
            {
                using var fs = new FileStream(jsonlPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var sr = new StreamReader(fs);
                var activeInstalls = new Dictionary<string, string>();
                while (!sr.EndOfStream)
                {
                    string line = sr.ReadLine() ?? "";
                    var idMatch = Regex.Match(line, @"""installationId""\s*:\s*""([^""]+)""");
                    if (!idMatch.Success) continue;
                    string id = idMatch.Groups[1].Value;

                    if (line.Contains("\"deleted\":true") || line.Contains("\"complete\":true"))
                    {
                        activeInstalls.Remove(id);
                    }
                    else if (line.Contains("\"complete\":false"))
                    {
                        var dirMatch = Regex.Match(line, @"""dir""\s*:\s*""([^""]+)""");
                        if (dirMatch.Success)
                        {
                            string dir = dirMatch.Groups[1].Value.Replace(@"\\", @"\");
                            activeInstalls[id] = Path.GetFileName(dir);
                        }
                    }
                }

                if (activeInstalls.Count > 0)
                {
                    return (activeInstalls.Values.Last(), 0);
                }
            }
        }
        catch { }
        return ("", 0);
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }
}
