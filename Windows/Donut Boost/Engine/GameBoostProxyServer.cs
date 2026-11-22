using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using DonutBoost.Models;

namespace DonutBoost.Engine;

/// <summary>
/// Monitors game-launcher process activity and actual receive rates reported by
/// Windows network-interface counters. It does not intercept launcher traffic.
/// </summary>
public class GameBoostProxyServer : IDisposable
{
    private readonly NetworkBondingManager _bondingManager;
    private readonly LicenseManager? _licenseManager;
    private CancellationTokenSource? _cts;
    private Task? _monitorTask;
    private volatile bool _isRunning = false;

    private string _detectedLauncher = "Game Launcher";
    private long _detectedGameSizeBytes = 0;
    private long _detectedGameProgressBytes = 0;
    private bool _hasReliableGameProgress;
    private string _detectedGameName = "";

    public bool IsRunning => _isRunning;
    public int Port => 9090;
    public long DetectedGameSizeBytes => _detectedGameSizeBytes;
    public long DetectedGameProgressBytes => _detectedGameProgressBytes;
    public bool HasReliableGameProgress => _hasReliableGameProgress;
    public string DetectedGameName => _detectedGameName;

    public event Action<long, long, double, bool, bool, string>? ProxyMetricsUpdated;
    public event Action<string>? LogReceived;

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
            LogReceived?.Invoke("🔒 Security: Active valid license required to use Game Launcher Monitor.");
            return false;
        }

        try
        {
            _isRunning = true;
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _monitorTask = Task.Run(async () =>
            {
                try { await MonitorGameTrafficLoopAsync(token); }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    _isRunning = false;
                    LogReceived?.Invoke($"Game launcher monitor stopped after an error: {ex.Message}");
                }
            });

            LogReceived?.Invoke("Game launcher activity monitor started. Progress is available only when a launcher exposes reliable download counters.");
            return true;
        }
        catch (Exception ex)
        {
            LogReceived?.Invoke($"✕ Failed to start Game Launcher Monitor: {ex.Message}");
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

        LogReceived?.Invoke("🛑 Game Launcher Monitor stopped.");
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

    private static (string ActiveLauncher, string SummaryLabel) DetectLauncher(Process[] processes, double elapsedSec)
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

        // 7. Riot Client
        var riot = processes.Where(p => {
            try { return p.ProcessName.Contains("Riot", StringComparison.OrdinalIgnoreCase) || 
                         p.ProcessName.Contains("LeagueClient", StringComparison.OrdinalIgnoreCase) ||
                         p.ProcessName.Contains("VALORANT", StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }).ToList();
        if (riot.Count > 0) candidates.Add(new LauncherCandidate { Name = "Riot Client", Processes = riot });

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

        // 10. Microsoft Store
        var msStore = processes.Where(p => {
            try { return p.ProcessName.Contains("WinStore", StringComparison.OrdinalIgnoreCase) || 
                         p.ProcessName.Contains("DeliveryOptimization", StringComparison.OrdinalIgnoreCase) ||
                         p.ProcessName.Contains("InstallAgent", StringComparison.OrdinalIgnoreCase) ||
                         p.ProcessName.Contains("wsappx", StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }).ToList();
        if (msStore.Count > 0) candidates.Add(new LauncherCandidate { Name = "Microsoft Store", Processes = msStore });

        // 11. Unity / Unity Hub
        var unity = processes.Where(p => {
            try { return p.ProcessName.Contains("UnityHub", StringComparison.OrdinalIgnoreCase) || 
                         p.ProcessName.Equals("Unity", StringComparison.OrdinalIgnoreCase) ||
                         p.ProcessName.Contains("UnityPackage", StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }).ToList();
        if (unity.Count > 0) candidates.Add(new LauncherCandidate { Name = "Unity", Processes = unity });

        if (candidates.Count == 0)
        {
            return ("", "Ready");
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
            return (activeDownloading.Name, activeDownloading.Name);
        }

        // In Standby (none actively downloading), report unified "Game Launcher Standby"
        return ("Game Launcher", "Game Launcher Standby");
    }

    private string _summaryLauncherLabel = "Ready";

    private async Task MonitorGameTrafficLoopAsync(CancellationToken token)
    {
        var processCheckStopwatch = Stopwatch.StartNew();
        var sizeDetectionStopwatch = Stopwatch.StartNew();

        bool isGameActive = false;
        while (!token.IsCancellationRequested && _isRunning)
        {
            await Task.Delay(250, token);
            if (token.IsCancellationRequested || !_isRunning) break;

            // 1. Check for active game launcher processes every 1.5 seconds to save CPU
            if (processCheckStopwatch.ElapsedMilliseconds >= 1500)
            {
                double processSampleSeconds = Math.Max(0.1, processCheckStopwatch.Elapsed.TotalSeconds);
                processCheckStopwatch.Restart();
                try
                {
                    var runningProcesses = Process.GetProcesses();
                    (string activeLauncher, string summaryLabel) = ("", "Ready");
                    try
                    {
                        (activeLauncher, summaryLabel) = DetectLauncher(runningProcesses, processSampleSeconds);
                    }
                    finally
                    {
                        foreach (var process in runningProcesses) process.Dispose();
                    }

                    if (!string.IsNullOrEmpty(activeLauncher))
                    {
                        if (!string.Equals(_detectedLauncher, activeLauncher, StringComparison.OrdinalIgnoreCase))
                        {
                            _detectedGameSizeBytes = 0;
                            _detectedGameName = "";
                            _detectedGameProgressBytes = 0;
                            _hasReliableGameProgress = false;
                        }
                        isGameActive = !activeLauncher.Equals("Game Launcher", StringComparison.OrdinalIgnoreCase);
                        _detectedLauncher = activeLauncher;
                        _summaryLauncherLabel = summaryLabel;
                    }
                    else
                    {
                        _detectedGameSizeBytes = 0;
                        _detectedGameName = "";
                        _detectedGameProgressBytes = 0;
                        _hasReliableGameProgress = false;
                        isGameActive = false;
                        _detectedLauncher = "Ready";
                        _summaryLauncherLabel = "Ready";
                    }
                }
                catch { }
            }

            // Periodic License Validation
            if (_licenseManager != null && !_licenseManager.ValidateActiveSessionToken())
            {
                LogReceived?.Invoke("🔒 Security: Active valid license required. Launcher monitor stopped.");
                Stop();
                break;
            }

            // Read the independent physical-interface counters maintained by
            // NetworkBondingManager. They include all apps' RX traffic, not
            // only the launcher; never use this total as game-file progress.
            double totalInstantBps = _bondingManager.Adapters
                .Where(adapter => adapter.IsOnline)
                .Sum(adapter => Math.Max(0, adapter.CurrentSpeedBps));

            // NIC counters show real adapter traffic, but cannot prove which
            // process owns it. Use them for truthful network rates only.
            const double MinLauncherTrafficBps = 8 * 1024;
            bool isLauncherTraffic = isGameActive && totalInstantBps >= MinLauncherTrafficBps;

            // A launcher can be identified while idle. Only label it active
            // when its process is writing and adapter traffic is present.
            if (isGameActive && isLauncherTraffic)
            {
                const bool launcherActivityDetected = true;
                double reportSpeed = totalInstantBps;

                // Refresh Steam's verified transfer counters every three seconds.
                if (_detectedGameSizeBytes <= 0 || sizeDetectionStopwatch.ElapsedMilliseconds >= 3000)
                {
                    sizeDetectionStopwatch.Restart();
                    if (_detectedLauncher == "Steam")
                    {
                        var (steamName, steamBytes, steamProgress) = DetectSteamDownloadSize();
                        _detectedGameSizeBytes = steamBytes;
                        _detectedGameName = steamName;
                        _detectedGameProgressBytes = steamProgress;
                        _hasReliableGameProgress = steamBytes > 0 && !string.IsNullOrWhiteSpace(steamName);
                    }
                    else
                    {
                        // Other launcher processes do not expose a verified
                        // active-game size/progress source to this monitor.
                        _detectedGameSizeBytes = 0;
                        _detectedGameProgressBytes = 0;
                        _detectedGameName = "";
                        _hasReliableGameProgress = false;
                    }
                }

                string displayLabel = !string.IsNullOrEmpty(_detectedGameName) 
                    ? $"{_detectedLauncher} - {_detectedGameName}" 
                    : _detectedLauncher;

                long displayedBytes = _hasReliableGameProgress ? _detectedGameProgressBytes : 0;
                ProxyMetricsUpdated?.Invoke(displayedBytes, _detectedGameSizeBytes, reportSpeed, launcherActivityDetected, _hasReliableGameProgress, displayLabel);
            }
            else
            {
                // Launcher is open but in idle standby (no active game download taking place)
                _detectedGameName = "";
                _detectedGameSizeBytes = 0;
                _detectedGameProgressBytes = 0;
                _hasReliableGameProgress = false;
                foreach (var adapter in _bondingManager.Adapters)
                {
                    adapter.NotifyMetricsChanged();
                }
                ProxyMetricsUpdated?.Invoke(0, 0, totalInstantBps, false, false, _summaryLauncherLabel);
            }
        }
    }

    private static (string GameName, long TotalBytes, long CurrentDownloaded) DetectSteamDownloadSize()
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
            if (string.IsNullOrEmpty(steamRoot)) return ("", 0, 0);

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

            // 3. Scan active download folders first. Steamworks Common
            // Redistributables (AppID 228980) are a small prerequisite and must
            // not be reported as the size of the game the user is downloading.
            foreach (var lib in libraryFolders)
            {
                string downloadingDir = Path.Combine(lib, "downloading");
                var activeAppIds = new List<string>();

                if (Directory.Exists(downloadingDir))
                {
                    try
                    {
                        foreach (var sub in Directory.GetDirectories(downloadingDir)
                                     .OrderByDescending(path => Directory.GetLastWriteTimeUtc(path)))
                        {
                            string appid = Path.GetFileName(sub);
                            if (long.TryParse(appid, out _)) activeAppIds.Add(appid);
                        }
                    }
                    catch { }
                }

                // Prefer actual games over Steamworks Common Redistributables.
                foreach (var appid in activeAppIds
                             .OrderBy(id => id == "228980" ? 1 : 0))
                {
                    if (appid == "228980") continue;
                    string acf = Path.Combine(lib, $"appmanifest_{appid}.acf");
                    if (File.Exists(acf))
                    {
                        var res = ParseSteamManifest(acf);
                        if (res.TotalBytes > 0)
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
                        string appId = Regex.Match(file.Name, @"appmanifest_(\d+)\.acf", RegexOptions.IgnoreCase).Groups[1].Value;
                        if (appId == "228980" || !Directory.Exists(Path.Combine(downloadingDir, appId))) continue;
                        var res = ParseSteamManifest(file.FullName);
                        if (res.TotalBytes > 0)
                            return res;
                    }
                }
                catch { }
            }

            // Steam content log entries do not expose authoritative current progress.
        }
        catch { }
        return ("", 0, 0);
    }

    private static (string GameName, long TotalBytes, long CurrentDownloaded) ParseSteamManifest(string acfPath)
    {
        try
        {
            using var fs = new FileStream(acfPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var sr = new StreamReader(fs);
            string content = sr.ReadToEnd();

            string name = "";
            var nameMatch = Regex.Match(content, @"""name""\s+""([^""]+)""");
            if (nameMatch.Success) name = nameMatch.Groups[1].Value;

            long bytesDownload = 0;
            var dlMatch = Regex.Match(content, @"""BytesToDownload""\s+""(\d+)""");
            if (dlMatch.Success && long.TryParse(dlMatch.Groups[1].Value, out long d)) bytesDownload = d;

            long bytesDownloaded = 0;
            var curDlMatch = Regex.Match(content, @"""BytesDownloaded""\s+""(\d+)""");
            if (curDlMatch.Success && long.TryParse(curDlMatch.Groups[1].Value, out long cdl)) bytesDownloaded = cdl;

            int stateFlags = 0;
            var flagMatch = Regex.Match(content, @"""StateFlags""\s+""(\d+)""");
            if (flagMatch.Success && int.TryParse(flagMatch.Groups[1].Value, out int sf)) stateFlags = sf;

            // StateFlags=4 means FullyInstalled. The old check treated that bit
            // as active and returned stale, completed manifests. Require an
            // actual pending download/staging state and use transfer counters,
            // never installed size/depot size, for download ETA.
            const int activeUpdateStates = 2 | 1024 | 2048 | 4096 | 8192 | 16384 | 32768 | 65536;
            bool updateIsActive = (stateFlags & activeUpdateStates) != 0;
            long effectiveSize = bytesDownload;
            long currentProgress = Math.Min(bytesDownloaded, bytesDownload);

            bool isSteamworksPrerequisite = name.Contains("Steamworks Common Redistributables", StringComparison.OrdinalIgnoreCase) ||
                                            name.Contains("Redistributable", StringComparison.OrdinalIgnoreCase);
            if (updateIsActive && !isSteamworksPrerequisite && effectiveSize > 0)
            {
                return (string.IsNullOrEmpty(name) ? "Steam Game" : name, effectiveSize, currentProgress);
            }
        }
        catch { }
        return ("", 0, 0);
    }

    public void Dispose()
    {
        Stop();
        try { _monitorTask?.Wait(TimeSpan.FromSeconds(2)); }
        catch (AggregateException) { }
        _cts?.Dispose();
        _cts = null;
    }
}
