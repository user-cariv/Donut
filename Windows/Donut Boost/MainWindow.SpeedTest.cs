using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;

namespace DonutBoost;

public partial class MainWindow
{
    private CancellationTokenSource? _speedTestCancellation;
    private bool _showingSpeedTest;
    private double _liveGaugeMbps;

    private async void SpeedTestButton_Click(object sender, RoutedEventArgs e)
    {
        if (_showingSpeedTest)
        {
            RestoreLiveSpeedGauge();
            return;
        }
        if (_runningTransfer || _updateCancellation != null)
        {
            MessageBox.Show(this, "Finish the current Donut transfer before running a speed test.", "Speed test");
            return;
        }

        using var cancellation = new CancellationTokenSource();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(25));
        ShowPreviewDownloads_Click(sender, e);
        _speedTestCancellation = cancellation;
        _showingSpeedTest = true;
        SpeedTestButton.Content = "Cancel test";
        SpeedGaugeHeading.Text = "D O W N L O A D  T E S T";
        LiveSpeedGaugeDetail.Visibility = Visibility.Collapsed;
        SpeedTestGaugeDetail.Visibility = Visibility.Visible;
        SpeedTestGaugeDetail.Text = "Measuring to Cloudflare…";
        WifiRateArc.Visibility = MobileRateArc.Visibility = Visibility.Hidden;
        CombinedSpeedTextBlock.Text = "—";
        CombinedSpeedUnitTextBlock.Text = "MBPS";

        try
        {
            using var client = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false });
            double mbps = await MeasureDownloadMbpsAsync(client, deadline.Token);
            if (cancellation.IsCancellationRequested) return;
            SetSpeedGaugeValue(mbps);
            SpeedTestGaugeDetail.Text = "Cloudflare · 96 MiB";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException)
        {
            if (cancellation.IsCancellationRequested) return;
            SpeedTestGaugeDetail.Text = "Could not finish test";
            OnLogReceived($"Speed test failed: {ex.Message}");
        }
        finally
        {
            if (_speedTestCancellation == cancellation)
            {
                _speedTestCancellation = null;
                if (!_windowClosed && _showingSpeedTest) SpeedTestButton.Content = "Live traffic";
            }
        }
    }

    private void RestoreLiveSpeedGauge()
    {
        if (!_showingSpeedTest) return;
        _speedTestCancellation?.Cancel();
        _speedTestCancellation = null;
        _showingSpeedTest = false;
        SpeedTestButton.Content = "Speed test";
        SpeedGaugeHeading.Text = "C O M B I N E D";
        SpeedTestGaugeDetail.Visibility = Visibility.Collapsed;
        LiveSpeedGaugeDetail.Visibility = Visibility.Visible;
        WifiRateArc.Visibility = MobileRateArc.Visibility = Visibility.Visible;
        SetSpeedGaugeValue(_liveGaugeMbps);
    }

    private void SetSpeedGaugeValue(double mbps)
    {
        CombinedSpeedTextBlock.Text = mbps >= 1000 ? $"{mbps / 1000:F2}" : $"{mbps:F1}";
        CombinedSpeedUnitTextBlock.Text = mbps >= 1000 ? "GBPS" : "MBPS";
    }

    internal static async Task<double> MeasureDownloadMbpsAsync(HttpClient client, CancellationToken token)
    {
        const int streamCount = 12;
        const int bytesPerStream = 8 * 1024 * 1024;
        string run = Guid.NewGuid().ToString("N");
        var clock = Stopwatch.StartNew();
        var counts = await Task.WhenAll(Enumerable.Range(0, streamCount).Select(DownloadAsync));
        return counts.Sum() * 8.0 / clock.Elapsed.TotalSeconds / 1_000_000;

        async Task<int> DownloadAsync(int index)
        {
            using var response = await client.GetAsync(
                $"https://speed.cloudflare.com/__down?bytes={bytesPerStream}&run={run}&stream={index}",
                HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            var buffer = new byte[64 * 1024];
            int received = 0;
            while (received < bytesPerStream)
            {
                int read = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, bytesPerStream - received)), token)
                    .ConfigureAwait(false);
                if (read == 0) throw new EndOfStreamException("The test server returned less data than requested.");
                received += read;
            }
            return received;
        }
    }
}
