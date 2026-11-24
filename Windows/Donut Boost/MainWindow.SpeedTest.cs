using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Windows;

namespace DonutBoost;

public partial class MainWindow
{
    private bool _systemMergeActive;
    private CancellationTokenSource? _speedTestCancellation;

    private async void CombinedSpeedTest_Click(object sender, RoutedEventArgs e)
    {
        if (_speedTestCancellation is { } running)
        {
            running.Cancel();
            return;
        }
        if (!_systemMergeActive)
        {
            CombinedSpeedTestResult.Text = "Turn on System merge before running the test.";
            return;
        }

        using var cancel = new CancellationTokenSource();
        _speedTestCancellation = cancel;
        CombinedSpeedTestButton.Content = "Cancel speed test";
        CombinedSpeedTestResult.Text = "Measuring download throughput…";
        try
        {
            using var duration = CancellationTokenSource.CreateLinkedTokenSource(cancel.Token);
            duration.CancelAfter(TimeSpan.FromSeconds(10));
            using var client = new HttpClient(new SocketsHttpHandler
            {
                UseProxy = false,
                AllowAutoRedirect = false,
                MaxConnectionsPerServer = 8
            })
            {
                DefaultRequestVersion = HttpVersion.Version11,
                DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact
            };
            // Force separate TCP flows so the system-wide scheduler can spread the test traffic.
            client.DefaultRequestHeaders.ConnectionClose = true;
            var clock = Stopwatch.StartNew();
            long[] bytes = await Task.WhenAll(Enumerable.Range(0, 8)
                .Select(index => ReadSpeedSampleAsync(client, index, duration.Token)));
            cancel.Token.ThrowIfCancellationRequested();
            if (_windowClosed) return;

            long total = bytes.Sum();
            if (bytes.Count(count => count > 0) < 2)
                CombinedSpeedTestResult.Text = "Too few test connections returned data. Try again in a moment.";
            else
            {
                double mbps = total * 8 / clock.Elapsed.TotalSeconds / 1_000_000;
                CombinedSpeedTestResult.Text = $"Test download: {mbps:F1} Mbps · {total / 1_000_000.0:F1} MB received";
            }
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            if (!_windowClosed) CombinedSpeedTestResult.Text = "Speed test cancelled.";
        }
        finally
        {
            _speedTestCancellation = null;
            if (!_windowClosed) CombinedSpeedTestButton.Content = "Combined speed test";
        }
    }

    private static async Task<long> ReadSpeedSampleAsync(HttpClient client, int connection, CancellationToken token)
    {
        const int sampleBytes = 20_000_000;
        long received = 0;
        try
        {
            string url = $"https://speed.cloudflare.com/__down?bytes={sampleBytes}&sample={connection}&run={Guid.NewGuid():N}";
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            var buffer = new byte[64 * 1024];
            while (received < sampleBytes)
            {
                int length = (int)Math.Min(buffer.Length, sampleBytes - received);
                int read = await stream.ReadAsync(buffer.AsMemory(0, length), token).ConfigureAwait(false);
                if (read == 0) break;
                received += read;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (HttpRequestException) { }
        catch (IOException) { }
        return received;
    }
}
