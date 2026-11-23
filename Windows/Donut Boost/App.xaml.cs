using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace DonutBoost;

public partial class App : Application
{
    private static Mutex? _singleInstanceMutex;
    private const string DownloadPipeName = "DonutBoost.DownloadLink.v1";
    private CancellationTokenSource? _downloadPipeCancellation;

    protected override async void OnStartup(StartupEventArgs e)
    {
        _singleInstanceMutex = new Mutex(true, "DonutBoost_SingleInstance_AppMutex", out bool isNewInstance);
        if (!isNewInstance)
        {
            string? link = FindDownloadLink(e.Args);
            bool forwarded = string.IsNullOrWhiteSpace(link) || await TryForwardDownloadLinkAsync(link);

            // Focus an already visible instance. A windowless process left
            // behind by a previous crash must not block a fresh launch.
            if (ActivateVisibleInstance())
            {
                if (!forwarded)
                    MessageBox.Show("Donut Boost is already open but could not receive this link. Update the app, then try again.", "Donut Boost", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }
        }

        base.OnStartup(e);

        try
        {
            var mainWindow = new MainWindow();
            MainWindow = mainWindow;
            mainWindow.Show();
            mainWindow.SetIncomingDownloadLink(FindDownloadLink(e.Args));
            _downloadPipeCancellation = new CancellationTokenSource();
            _ = ListenForDownloadLinksAsync(_downloadPipeCancellation.Token);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Donut Boost could not finish opening. Please install the latest version from the Donut website.\n\nDetails: {ex.Message}",
                "Donut Boost startup error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _downloadPipeCancellation?.Cancel();
        _downloadPipeCancellation?.Dispose();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    private async Task ListenForDownloadLinksAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(
                    DownloadPipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(token);
                using var reader = new StreamReader(pipe, Encoding.UTF8, false, 1024, leaveOpen: true);
                string? message = await reader.ReadLineAsync(token);
                if (TryGetDownloadLink(message, out string? link) && MainWindow is MainWindow window)
                    _ = window.Dispatcher.BeginInvoke(() => window.SetIncomingDownloadLink(link));
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (IOException) { }
            catch (ObjectDisposedException) when (token.IsCancellationRequested) { break; }
        }
    }

    private static async Task<bool> TryForwardDownloadLinkAsync(string link)
    {
        for (int attempt = 0; attempt < 15; attempt++)
        {
            try
            {
                await using var pipe = new NamedPipeClientStream(
                    ".", DownloadPipeName, PipeDirection.Out,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.ConnectAsync(150);
                await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 1024, leaveOpen: true) { AutoFlush = true };
                await writer.WriteLineAsync(link);
                return true;
            }
            catch (TimeoutException) { }
            catch (IOException) { }
            await Task.Delay(100);
        }
        return false;
    }

    private static bool TryGetDownloadLink(string? value, out string? link)
    {
        link = null;
        if (string.IsNullOrWhiteSpace(value) || value.Length > 8192) return false;
        string candidate = value.Trim();
        if (Uri.TryCreate(candidate, UriKind.Absolute, out var protocolUri)
            && protocolUri.Scheme.Equals("donutboost", StringComparison.OrdinalIgnoreCase))
        {
            if (!protocolUri.Host.Equals("download", StringComparison.OrdinalIgnoreCase)) return false;
            string? encoded = System.Web.HttpUtility.ParseQueryString(protocolUri.Query).Get("url");
            if (string.IsNullOrWhiteSpace(encoded)) return false;
            candidate = encoded.Trim();
        }

        if (candidate.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
        {
            if (candidate.Any(char.IsControl)) return false;
            link = candidate;
            return true;
        }
        if (Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            if (candidate.Any(char.IsControl)) return false;
            link = uri.AbsoluteUri;
            return true;
        }
        return false;
    }

    private static string? FindDownloadLink(IEnumerable<string> values)
    {
        foreach (string value in values)
            if (TryGetDownloadLink(value, out string? link)) return link;
        return null;
    }

    private static bool ActivateVisibleInstance()
    {
        foreach (var process in Process.GetProcessesByName("DonutBoost"))
        {
            if (process.Id == Environment.ProcessId) continue;
            try
            {
                IntPtr window = process.MainWindowHandle;
                if (window == IntPtr.Zero) continue;
                if (IsIconic(window)) ShowWindow(window, 9);
                SetForegroundWindow(window);
                return true;
            }
            catch { }
        }
        return false;
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr window);
}
