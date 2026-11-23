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
    private bool _ownsInstanceMutex;
    private const string DownloadPipeName = "DonutBoost.DownloadLink.v1";
    private const int MaxHandoffLength = 8192;
    private CancellationTokenSource? _downloadPipeCancellation;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            _singleInstanceMutex = new Mutex(false, "DonutBoost_SingleInstance_AppMutex");
            string? link = FindDownloadLink(e.Args);

            if (!TryAcquireInstance())
            {
                if (link != null && await TryForwardDownloadLinkAsync(link))
                {
                    ActivateVisibleInstance();
                    Shutdown();
                    return;
                }

                // The original instance may have just exited. Only start a new
                // window if this process can now take ownership of the mutex.
                if (!TryAcquireInstance())
                {
                    ActivateVisibleInstance();
                    if (link != null)
                        MessageBox.Show("Donut Boost could not receive the link. Try again when the app is ready, or paste the link into its download box.",
                            "Donut Boost", MessageBoxButton.OK, MessageBoxImage.Information);
                    Shutdown();
                    return;
                }
            }

            var mainWindow = new MainWindow();
            MainWindow = mainWindow;
            _downloadPipeCancellation = new CancellationTokenSource();
            var token = _downloadPipeCancellation.Token;
            _ = Task.Run(() => ListenForDownloadLinksAsync(mainWindow, token), token);
            mainWindow.Show();
            mainWindow.SetIncomingDownloadLink(link);
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

    private bool TryAcquireInstance()
    {
        try { _ownsInstanceMutex = _singleInstanceMutex!.WaitOne(0); }
        catch (AbandonedMutexException) { _ownsInstanceMutex = true; }
        return _ownsInstanceMutex;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _downloadPipeCancellation?.Cancel();
        _downloadPipeCancellation?.Dispose();
        if (_ownsInstanceMutex)
            _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    private static async Task ListenForDownloadLinksAsync(MainWindow window, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(
                    DownloadPipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(token);
                using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                readTimeout.CancelAfter(TimeSpan.FromSeconds(3));
                using var reader = new StreamReader(pipe, Encoding.UTF8, false, 1024, leaveOpen: true);
                string? message = await ReadHandoffAsync(reader, readTimeout.Token);
                if (TryGetDownloadLink(message, out string? link) && !window.Dispatcher.HasShutdownStarted)
                    _ = window.Dispatcher.BeginInvoke(() => window.SetIncomingDownloadLink(link));
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) when (token.IsCancellationRequested) { break; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A busy pipe must not spin, particularly while Windows closes
                // the previous app instance during an update.
                try { await Task.Delay(200, token); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    private static async Task<string?> ReadHandoffAsync(StreamReader reader, CancellationToken token)
    {
        char[] buffer = new char[1024];
        var message = new StringBuilder();
        while (message.Length <= MaxHandoffLength)
        {
            int count = await reader.ReadAsync(buffer.AsMemory(), token);
            if (count == 0) return null;
            int newline = Array.IndexOf(buffer, '\n', 0, count);
            int length = newline >= 0 ? newline : count;
            if (message.Length + length > MaxHandoffLength) return null;
            message.Append(buffer, 0, length);
            if (newline >= 0) return message.ToString().TrimEnd('\r');
        }
        return null;
    }

    private static async Task<bool> TryForwardDownloadLinkAsync(string link)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        byte[] payload = Encoding.UTF8.GetBytes(link + "\n");
        for (int attempt = 0; attempt < 15; attempt++)
        {
            try
            {
                await using var pipe = new NamedPipeClientStream(
                    ".", DownloadPipeName, PipeDirection.Out,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.ConnectAsync(150, deadline.Token);
                await pipe.WriteAsync(payload.AsMemory(), deadline.Token);
                return true;
            }
            catch (TimeoutException) { }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { return false; }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested) { return false; }
            await Task.Delay(100);
        }
        return false;
    }

    private static bool TryGetDownloadLink(string? value, out string? link)
    {
        link = null;
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxHandoffLength) return false;
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
            using var candidate = process;
            if (candidate.Id == Environment.ProcessId) continue;
            try
            {
                IntPtr window = candidate.MainWindowHandle;
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
