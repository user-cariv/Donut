using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace DonutBoost.Engine;

internal static class SteamDownloads
{
    internal sealed record Download(string Name, long BytesDownloaded, long TotalBytes, double? RateMbps);

    private static readonly Regex StateLine = new(@"^\[(?<time>[^\]]+)\] AppID (?<id>[0-9]+) App update changed : (?<state>.*)$");
    private static readonly Regex RateLine = new(@"^\[(?<time>[^\]]+)\] Current download rate: (?<rate>[0-9.]+) Mbps");

    public static Download? ReadActiveDownload()
    {
        try
        {
            var processes = Process.GetProcessesByName("steam");
            DateTime started;
            try
            {
                if (processes.Length == 0) return null;
                started = processes[0].StartTime.AddSeconds(-2);
            }
            finally
            {
                foreach (var process in processes) process.Dispose();
            }

            string? root = new[]
            {
                Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string,
                Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string,
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam")
            }.FirstOrDefault(path => path != null && File.Exists(Path.Combine(path, "logs", "content_log.txt")));
            if (root is null) return null;

            return Read(root, started, DateTime.Now);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    internal static Download? Read(string root, DateTime sessionStart, DateTime now)
    {
        var active = new Dictionary<string, DateTime>();
        (DateTime Time, double Mbps)? rate = null;
        using (var stream = File.Open(Path.Combine(root, "logs", "content_log.txt"), FileMode.Open,
                   FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            stream.Position = Math.Max(0, stream.Length - 256 * 1024);
            using var reader = new StreamReader(stream);
            while (reader.ReadLine() is { } line)
            {
                var state = StateLine.Match(line);
                if (TryReadTime(state, out var stateTime) && stateTime >= sessionStart)
                {
                    string id = state.Groups["id"].Value;
                    string flags = state.Groups["state"].Value;
                    if (flags.Contains("Downloading,", StringComparison.Ordinal) &&
                        !flags.Contains("Stopping,", StringComparison.Ordinal))
                        active.TryAdd(id, stateTime);
                    else
                        active.Remove(id);
                }
                var sample = RateLine.Match(line);
                if (TryReadTime(sample, out var sampleTime) && sampleTime >= sessionStart &&
                    double.TryParse(sample.Groups["rate"].Value, NumberStyles.AllowDecimalPoint,
                        CultureInfo.InvariantCulture, out double mbps) && double.IsFinite(mbps))
                    rate = (sampleTime, mbps);
            }
        }
        if (active.Count == 0) return null;

        string steamapps = Path.Combine(root, "steamapps");
        var libraries = new List<string> { steamapps };
        string libraryList = Path.Combine(steamapps, "libraryfolders.vdf");
        if (File.Exists(libraryList))
        {
            foreach (Match match in Regex.Matches(ReadShared(libraryList), @"""path""\s+""([^""]+)"""))
                libraries.Add(Path.Combine(match.Groups[1].Value.Replace(@"\\", @"\"), "steamapps"));
        }
        foreach (var job in active.OrderBy(pair => pair.Key == "228980").ThenByDescending(pair => pair.Value))
        foreach (string library in libraries.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string path = Path.Combine(library, $"appmanifest_{job.Key}.acf");
            if (!File.Exists(path)) continue;
            string manifest = ReadShared(path);
            string name = Field(manifest, "name");
            if (name.Length == 0 ||
                !long.TryParse(Field(manifest, "BytesToDownload"), out long total) || total <= 0 ||
                !long.TryParse(Field(manifest, "BytesDownloaded"), out long downloaded) || downloaded < 0 || downloaded >= total)
                continue;

            double? measured = active.Count == 1 && rate is { } r && r.Time >= job.Value &&
                now >= r.Time && now - r.Time < TimeSpan.FromSeconds(75) ? r.Mbps : null;
            return new Download(name, downloaded, total, measured);
        }
        return null;
    }

    private static bool TryReadTime(Match match, out DateTime time) =>
        DateTime.TryParseExact(match.Groups["time"].Value, "yyyy-MM-dd HH:mm:ss",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out time);

    private static string Field(string text, string field) =>
        Regex.Match(text, $@"""{field}""\s+""([^""]*)""", RegexOptions.IgnoreCase).Groups[1].Value;

    private static string ReadShared(string path)
    {
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
