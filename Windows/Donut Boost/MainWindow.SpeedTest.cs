using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Threading;

namespace DonutBoost;

public partial class MainWindow
{
    private CancellationTokenSource? _speedTestCancellation;
    private bool _showingSpeedTest;
    private double _liveGaugeMbps;
    private double _shownTestMbps;
    private double _targetTestMbps;
    private bool _hasTestReading;

    private async void SpeedTestButton_Click(object sender, RoutedEventArgs e)
    {
        if (_speedTestCancellation is not null)
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
        deadline.CancelAfter(TimeSpan.FromSeconds(50));
        ShowPreviewDownloads_Click(sender, e);
        _speedTestCancellation = cancellation;
        _showingSpeedTest = true;
        SpeedTestButton.Content = "Cancel test";
        SpeedGaugeHeading.Text = "D O W N L O A D  T E S T";
        LiveSpeedGaugeDetail.Visibility = Visibility.Collapsed;
        SpeedTestGaugeDetail.Visibility = Visibility.Visible;
        SpeedTestGaugeDetail.Text = "Connecting to Cloudflare…";
        LiveTrafficButton.Visibility = Visibility.Collapsed;
        _targetWifi = _targetMobile = 0;
        _shownTestMbps = _targetTestMbps = 0;
        _hasTestReading = false;
        CombinedSpeedTextBlock.Text = "—";
        CombinedSpeedUnitTextBlock.Text = "MBPS";

        long received = 0;
        long lastBytes = 0;
        var sampleClock = Stopwatch.StartNew();
        var progressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        progressTimer.Tick += (_, _) =>
        {
            if (_speedTestCancellation != cancellation) return;
            double seconds = sampleClock.Elapsed.TotalSeconds;
            sampleClock.Restart();
            long bytes = Interlocked.Read(ref received);
            if (bytes == 0) return;
            SpeedTestGaugeDetail.Text = "Measuring to Cloudflare…";
            UpdateSpeedTestTarget((bytes - lastBytes) * 8.0 / seconds / 1_000_000);
            lastBytes = bytes;
        };
        progressTimer.Start();

        try
        {
            using var client = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false });
            double mbps = await MeasureDownloadMbpsAsync(client, deadline.Token, bytes => Interlocked.Add(ref received, bytes));
            if (cancellation.IsCancellationRequested) return;
            UpdateSpeedTestTarget(mbps);
            SpeedTestGaugeDetail.Text = "Cloudflare · 192 MiB measured";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException)
        {
            if (cancellation.IsCancellationRequested) return;
            _hasTestReading = false;
            _targetWifi = _targetMobile = 0;
            if (!_previewRendering) RenderPreviewGauge(0, 0);
            CombinedSpeedTextBlock.Text = "—";
            CombinedSpeedUnitTextBlock.Text = "MBPS";
            SpeedTestGaugeDetail.Text = "Could not finish test";
            OnLogReceived($"Speed test failed: {ex.Message}");
        }
        finally
        {
            progressTimer.Stop();
            if (_speedTestCancellation == cancellation)
            {
                _speedTestCancellation = null;
                if (!_windowClosed && _showingSpeedTest)
                {
                    SpeedTestButton.Content = "Test again";
                    LiveTrafficButton.Visibility = Visibility.Visible;
                }
            }
        }
    }

    private void LiveTrafficButton_Click(object sender, RoutedEventArgs e) => RestoreLiveSpeedGauge();

    private void RestoreLiveSpeedGauge()
    {
        if (!_showingSpeedTest) return;
        _speedTestCancellation?.Cancel();
        _speedTestCancellation = null;
        _showingSpeedTest = false;
        SpeedTestButton.Content = "Speed test";
        LiveTrafficButton.Visibility = Visibility.Collapsed;
        SpeedGaugeHeading.Text = "C O M B I N E D";
        SpeedTestGaugeDetail.Visibility = Visibility.Collapsed;
        LiveSpeedGaugeDetail.Visibility = Visibility.Visible;
        SetSpeedGaugeValue(_liveGaugeMbps);
    }

    private void UpdateSpeedTestTarget(double mbps)
    {
        _targetTestMbps = mbps;
        _hasTestReading = true;
        if (!_previewRendering)
        {
            _shownTestMbps = mbps;
            SetSpeedGaugeValue(mbps);
        }
    }

    private void SetSpeedGaugeValue(double mbps)
    {
        string value = mbps >= 1000 ? $"{mbps / 1000:F2}" : $"{mbps:F1}";
        string unit = mbps >= 1000 ? "GBPS" : "MBPS";
        if (CombinedSpeedTextBlock.Text != value) CombinedSpeedTextBlock.Text = value;
        if (CombinedSpeedUnitTextBlock.Text != unit) CombinedSpeedUnitTextBlock.Text = unit;
    }

    internal static async Task<double> MeasureDownloadMbpsAsync(HttpClient client, CancellationToken token,
        Action<int>? onBytesReceived = null)
    {
        const int streamCount = 12;
        const int warmupBytes = 512 * 1024;
        const int bytesPerRequest = 8 * 1024 * 1024;
        string run = Guid.NewGuid().ToString("N");
        await Task.WhenAll(Enumerable.Range(0, streamCount).Select(index => DownloadAsync(index, warmupBytes, null)))
            .ConfigureAwait(false);
        var clock = Stopwatch.StartNew();
        var counts = await Task.WhenAll(Enumerable.Range(0, streamCount).Select(MeasureStreamAsync)).ConfigureAwait(false);
        return counts.Sum() * 8.0 / clock.Elapsed.TotalSeconds / 1_000_000;

        async Task<int> MeasureStreamAsync(int index)
        {
            int first = await DownloadAsync(index, bytesPerRequest, onBytesReceived).ConfigureAwait(false);
            int second = await DownloadAsync(index + streamCount, bytesPerRequest, onBytesReceived).ConfigureAwait(false);
            return first + second;
        }

        async Task<int> DownloadAsync(int index, int requestedBytes, Action<int>? onRead)
        {
            using var response = await client.GetAsync(
                $"https://speed.cloudflare.com/__down?bytes={requestedBytes}&run={run}&stream={index}",
                HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            var buffer = new byte[64 * 1024];
            int received = 0;
            while (received < requestedBytes)
            {
                int read = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, requestedBytes - received)), token)
                    .ConfigureAwait(false);
                if (read == 0) throw new EndOfStreamException("The test server returned less data than requested.");
                received += read;
                onRead?.Invoke(read);
            }
            return received;
        }
    }
}
