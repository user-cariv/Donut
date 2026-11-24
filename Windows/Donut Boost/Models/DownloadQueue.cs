using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DonutBoost.Models;

public enum QueueEntryStatus { Queued, Downloading, Completed, Failed, Cancelled, Interrupted }

public sealed record QueuedDownload(Guid Id, string Input, string Folder, string Name,
    DateTimeOffset? ScheduledAt, QueueEntryStatus Status, string? Detail = null, long? ExpectedBytes = null,
    bool RequiresBrowserSession = false)
{
    [JsonIgnore]
    public string StatusText => Status == QueueEntryStatus.Queued && ScheduledAt is { } when
        ? $"Scheduled for {when.LocalDateTime:g}"
        : Status.ToString();
}

internal sealed class DownloadQueue
{
    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DonutBoost", "download-queue.dat");

    public ObservableCollection<QueuedDownload> Entries { get; } = new();

    public void Load()
    {
        if (!File.Exists(_path)) return;
        byte[] serialized = ProtectedData.Unprotect(File.ReadAllBytes(_path), null, DataProtectionScope.CurrentUser);
        var entries = JsonSerializer.Deserialize<List<QueuedDownload>>(serialized) ?? new();
        foreach (var entry in entries)
        {
            Entries.Add(entry.RequiresBrowserSession && entry.Status is QueueEntryStatus.Queued or QueueEntryStatus.Downloading
                ? entry with { Status = QueueEntryStatus.Interrupted, Detail = "Send this link again from your signed-in browser." }
                : entry.Status == QueueEntryStatus.Downloading
                ? entry with { Status = QueueEntryStatus.Interrupted, Detail = "Select Retry to restart this download." }
                : entry);
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        string temp = _path + ".tmp";
        byte[] serialized = JsonSerializer.SerializeToUtf8Bytes(Entries);
        byte[] encrypted = ProtectedData.Protect(serialized, null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(temp, encrypted);
        File.Move(temp, _path, overwrite: true);
    }

    public void Replace(QueuedDownload entry)
    {
        for (int i = 0; i < Entries.Count; i++)
        {
            if (Entries[i].Id != entry.Id) continue;
            Entries[i] = entry;
            return;
        }
    }
}
