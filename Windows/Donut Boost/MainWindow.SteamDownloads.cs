using System.Windows;
using System.Windows.Threading;
using DonutBoost.Engine;
using DonutBoost.Models;

namespace DonutBoost;

public partial class MainWindow
{
    private readonly DispatcherTimer _steamDownloadTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private bool _readingSteam;

    private async void SteamDownloadTimer_Tick(object? sender, EventArgs e)
    {
        if (_readingSteam || _windowClosed || _runningTransfer) return;
        _readingSteam = true;
        try
        {
            var download = await Task.Run(SteamDownloads.ReadActiveDownload);
            if (!_windowClosed && !_runningTransfer) UpdateSteamTask(download);
        }
        finally
        {
            _readingSteam = false;
        }
    }

    private void UpdateSteamTask(SteamDownloads.Download? download)
    {
        SteamTaskPanel.Visibility = download is null ? Visibility.Collapsed : Visibility.Visible;
        DonutTaskPanel.Visibility = download is null ? Visibility.Visible : Visibility.Collapsed;
        if (download is null) return;

        SteamGameName.Text = download.Name;
        SteamGameSize.Text = $"{DownloadSession.FormatBytes(download.BytesDownloaded)} / {DownloadSession.FormatBytes(download.TotalBytes)}";
        SteamGameProgress.Value = 100.0 * download.BytesDownloaded / download.TotalBytes;
        SteamGameRate.Text = download.RateMbps is { } rate ? $"Steam reported {rate:F1} Mbps" : "Waiting for a Steam speed sample";
        SteamGameEta.Text = download.RateMbps is > 0
            ? $"Est. download ETA {DownloadSession.FormatETAFromSeconds((download.TotalBytes - download.BytesDownloaded) * 8.0 / (download.RateMbps.Value * 1_000_000))}"
            : "Download ETA unavailable";
    }
}
