using System;
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

    private readonly Dictionary<ProcessSampleKey, ulong> _processLastWriteBytes = new();

    private readonly record struct ProcessSampleKey(int ProcessId, long StartedUtcTicks);

    private class LauncherCandidate
    {
        public string Name { get; set; } = "";
        public List<Process> Processes { get; set; } = new();
        public double CurrentWriteSpeedBps { get; set; }
    }

    private static string? ResolveLauncher(Process process)
    {
        string name;
        try { name = process.ProcessName; }
        catch { return null; }

        // Exact executable names avoid attributing unrelated Chromium helpers,
        // games built with Unity, or every process named "Agent" to a launcher.
        switch (name.ToLowerInvariant())
        {
            case "steam":
            case "steamservice":
            case "steamwebhelper":
                return "Steam";
            case "epicgameslauncher":
            case "epicwebhelper":
                return "Epic Games";
            case "rockstarservice":
            case "socialclubhelper":
            case "launcherpatcher":
                return "Rockstar Games";
            case "launcher":
                return ProcessPathContains(process, "Rockstar Games") ? "Rockstar Games" : null;
            case "eadesktop":
            case "eabackgroundservice":
            case "ealocalhostsvc":
            case "origin":
            case "originwebhelperservice":
                return "EA App";
            case "battle.net":
            case "blizzarderror":
            case "agent":
                return ProcessPathContains(process, "Battle.net") || ProcessPathContains(process, "Battle.net\\Agent")
                    ? "Battle.net" : null;
            case "upc":
            case "ubisoftconnect":
            case "uplaywebcore":
                return "Ubisoft Connect";
            case "riotclientservices":
            case "riotclientux":
            case "riotclientuxrender":
            case "riotclientcrashhandler":
            case "leagueclient":
            case "leagueclientux":
                return "Riot Client";
            case "xboxpcapp":
            case "xboxapp":
            case "gamingservices":
            case "gamingservicesnet":
                return "Xbox App";
            case "galaxyclient":
            case "galaxyclientservice":
                return "GOG Galaxy";
            case "winstore.app":
            case "winstore":
            case "installagent":
            case "appinstaller":
                return "Microsoft Store";
            case "unityhub":
            case "unity hub":
            case "unitypackagemanager":
                return "Unity Hub";
            default:
                return null;
        }
    }

    private static bool ProcessPathContains(Process process, string fragment)
    {
        try
        {
            string? path = process.MainModule?.FileName;
            return !string.IsNullOrWhiteSpace(path) && path.Contains(fragment, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private (string ActiveLauncher, string SummaryLabel) DetectLauncher(Process[] processes, double elapsedSec)
    {
        var candidates = processes
            .Select(process => (Process: process, Launcher: ResolveLauncher(process)))
            .Where(item => item.Launcher != null)
            .GroupBy(item => item.Launcher!, StringComparer.OrdinalIgnoreCase)
            .Select(group => new LauncherCandidate
            {
                Name = group.Key,
                Processes = group.Select(item => item.Process).ToList()
            })
            .ToList();

        if (candidates.Count == 0)
        {
            _processLastWriteBytes.Clear();
            return ("", "Ready");
        }

        var liveSamples = new HashSet<ProcessSampleKey>();

        // Disk writes are only a launcher-activity heuristic. They are never
        // treated as downloaded bytes or used to invent a game percentage.
        foreach (var c in candidates)
        {
            double totalWriteBps = 0;
            foreach (var p in c.Processes)
            {
                try
                {
                    var sampleKey = new ProcessSampleKey(p.Id, p.StartTime.ToUniversalTime().Ticks);
                    liveSamples.Add(sampleKey);
                    if (GetProcessIoCounters(p.Handle, out var io))
                    {
                        ulong curr = io.WriteTransferCount;
                        if (_processLastWriteBytes.TryGetValue(sampleKey, out ulong prev))
                        {
                            _processLastWriteBytes[sampleKey] = curr;
                            if (curr >= prev && elapsedSec > 0)
                            {
                                totalWriteBps += (curr - prev) / elapsedSec;
                            }
                        }
                        else
                        {
                            _processLastWriteBytes[sampleKey] = curr;
                        }
                    }
                }
                catch { }
            }
            c.CurrentWriteSpeedBps = totalWriteBps;
        }

        foreach (var staleSample in _processLastWriteBytes.Keys.Where(key => !liveSamples.Contains(key)).ToArray())
            _processLastWriteBytes.Remove(staleSample);

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
                if (sizeDetectionStopwatch.ElapsedMilliseconds >= 3000)
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

            // StateFlags is a bitmask. Keep only update/download work states;
            // UpdateRequired (2), FullyInstalled (4), uninstall/backup, and app
            // running flags are not evidence of an active game download.
            const int updateRunning = 256;
            const int updatePaused = 512;
            const int updateStarted = 1024;
            const int validating = 131072;
            const int addingFiles = 262144;
            const int preallocating = 524288;
            const int downloading = 1048576;
            const int staging = 2097152;
            const int committing = 4194304;
            const int updateStopping = 8388608;
            const int activeUpdateStates = updateRunning | updatePaused | updateStarted | validating |
                addingFiles | preallocating | downloading | staging | committing | updateStopping;
            bool updateIsActive = (stateFlags & activeUpdateStates) != 0;
            long effectiveSize = bytesDownload;
            long currentProgress = Math.Min(bytesDownloaded, bytesDownload);

            bool isSteamworksPrerequisite = name.Contains("Steamworks Common Redistributables", StringComparison.OrdinalIgnoreCase) ||
                                            name.Contains("Redistributable", StringComparison.OrdinalIgnoreCase);
            bool transferStillPending = bytesDownload > 0 && bytesDownloaded < bytesDownload;
            if (updateIsActive && transferStillPending && !isSteamworksPrerequisite && effectiveSize > 0)
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
