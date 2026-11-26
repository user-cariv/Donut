using System.Globalization;
using System.IO;
using System.Windows;
using DonutBoost.Engine;
using DonutBoost.Models;

namespace DonutBoost;

public partial class MainWindow
{
    private void AddManualDownloadToQueue_Click(object sender, RoutedEventArgs e)
    {
        string input = UrlTextBox.Text.Trim();
        if (_downloadQueue.Entries.Count >= 500)
        {
            QueueFeedback.Text = "Remove older entries before adding to the queue.";
            return;
        }

        string? name;
        if (Path.IsPathFullyQualified(input) && File.Exists(input) &&
            (Path.GetExtension(input).Equals(".torrent", StringComparison.OrdinalIgnoreCase) || UsenetDownload.IsInput(input)))
        {
            if (UsenetDownload.IsInput(input) && _downloader.UsenetSettings is null && !ConfigureUsenetServer())
                return;
            name = Path.GetFileName(input);
        }
        else if (Uri.TryCreate(input, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" or "magnet")
        {
            input = uri.AbsoluteUri;
            name = uri.Scheme == "magnet" ? "Magnet download" : Path.GetFileName(uri.LocalPath);
            if (string.IsNullOrWhiteSpace(name)) name = uri.Host;
        }
        else
        {
            QueueFeedback.Text = "Enter a direct HTTP(S) URL or magnet link, or choose a torrent or NZB file.";
            return;
        }

        DateTimeOffset? scheduledAt = null;
        string timeText = QueueScheduleText.Text.Trim();
        if (timeText.Length > 0)
        {
            if (!TimeOnly.TryParseExact(timeText, "HH:mm", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out TimeOnly clockTime))
            {
                QueueFeedback.Text = "Enter a local start time in HH:mm or leave it blank.";
                return;
            }
            DateTime now = DateTime.Now;
            DateTime start = now.Date + clockTime.ToTimeSpan();
            if (start <= now) start = start.AddDays(1);
            if (TimeZoneInfo.Local.IsInvalidTime(start))
            {
                QueueFeedback.Text = "That local time is skipped by a clock change. Choose another time.";
                return;
            }
            scheduledAt = new DateTimeOffset(start);
        }

        if (_downloadQueue.Entries.Any(entry => entry.Input == input && entry.Folder == _outputFolder &&
                entry.Status is QueueEntryStatus.Queued or QueueEntryStatus.Downloading))
        {
            QueueFeedback.Text = "That download is already in the queue.";
            return;
        }

        var item = new QueuedDownload(Guid.NewGuid(), input, _outputFolder, name, scheduledAt, QueueEntryStatus.Queued);
        _downloadQueue.Entries.Add(item);
        if (!TrySaveQueueChange())
        {
            _downloadQueue.Entries.Remove(item);
            return;
        }
        QueueFeedback.Text = scheduledAt is null ? "Added to the queue." : item.StatusText;
        QueueScheduleText.Clear();
        DownloadQueueList.SelectedItem = item;
    }

    private void RetryQueuedDownload_Click(object sender, RoutedEventArgs e)
    {
        if (DownloadQueueList.SelectedItem is not QueuedDownload item || item.Status == QueueEntryStatus.Downloading)
            return;
        if (item.RequiresBrowserSession)
        {
            QueueFeedback.Text = "Send this link again from your signed-in browser.";
            return;
        }
        if (UsenetDownload.IsInput(item.Input) && _downloader.UsenetSettings is null && !ConfigureUsenetServer())
            return;

        var pending = item with { ScheduledAt = null, Status = QueueEntryStatus.Queued, Detail = null };
        _downloadQueue.Replace(pending);
        if (!TrySaveQueueChange())
            _downloadQueue.Replace(item);
        else
        {
            DownloadQueueList.SelectedItem = pending;
            QueueFeedback.Text = "Queued to start as soon as the current download finishes.";
        }
    }

    private void RemoveQueuedDownload_Click(object sender, RoutedEventArgs e)
    {
        if (DownloadQueueList.SelectedItem is not QueuedDownload item || item.Status == QueueEntryStatus.Downloading)
            return;
        int index = _downloadQueue.Entries.IndexOf(item);
        _downloadQueue.Entries.RemoveAt(index);
        if (!TrySaveQueueChange())
            _downloadQueue.Entries.Insert(index, item);
        else
        {
            _browserSessions.Remove(item.Id);
            QueueFeedback.Text = "Removed from the queue.";
        }
    }

    private bool TrySaveQueueChange()
    {
        try
        {
            _downloadQueue.Save();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        {
            QueueFeedback.Text = $"Could not save the queue. {ex.Message}";
            return false;
        }
    }
}
