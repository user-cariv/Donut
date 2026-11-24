using System.Buffers.Binary;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;

namespace DonutBoost;

internal sealed record BrowserCaptureRequest(string Action, Guid Id, string? Url, long? ExpectedBytes, string? CookieHeader);
internal sealed record BrowserCaptureReply(string Status);

internal static class BrowserCapture
{
    private const string PipeName = "DonutBoost.BrowserCapture.v1";
    private const int MaxMessageBytes = 16 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<int> RunHostAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var token = timeout.Token;
        try
        {
            using var input = Console.OpenStandardInput();
            using var output = Console.OpenStandardOutput();
            var request = await ReadAsync<BrowserCaptureRequest>(input, token);
            BrowserCaptureReply reply = new("unavailable");
            if (request is not null)
            {
                try
                {
                    await using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut,
                        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await pipe.ConnectAsync(750, token);
                    await WriteAsync(pipe, request, token);
                    reply = await ReadAsync<BrowserCaptureReply>(pipe, token) ?? reply;
                }
                catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException) { }
            }
            await WriteAsync(output, reply, token);
            return 0;
        }
        catch (Exception ex) when (ex is IOException or JsonException or OperationCanceledException or InvalidOperationException)
        {
            return 1;
        }
    }

    public static async Task ListenAsync(MainWindow window, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(token);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromSeconds(3));
                var request = await ReadAsync<BrowserCaptureRequest>(pipe, timeout.Token);
                if (request is null) continue;
                var response = await window.Dispatcher.InvokeAsync(() => window.HandleBrowserCapture(request),
                    System.Windows.Threading.DispatcherPriority.Normal, timeout.Token);
                await WriteAsync(pipe, response, timeout.Token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (Exception ex) when (ex is IOException or JsonException or OperationCanceledException or UnauthorizedAccessException)
            {
                if (!token.IsCancellationRequested) await Task.Delay(50);
            }
        }
    }

    private static async Task<T?> ReadAsync<T>(Stream stream, CancellationToken token)
    {
        byte[] header = new byte[4];
        await stream.ReadExactlyAsync(header, token);
        int length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length is <= 0 or > MaxMessageBytes) return default;
        byte[] body = new byte[length];
        await stream.ReadExactlyAsync(body, token);
        return JsonSerializer.Deserialize<T>(body, JsonOptions);
    }

    private static async Task WriteAsync<T>(Stream stream, T value, CancellationToken token)
    {
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        byte[] header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, body.Length);
        await stream.WriteAsync(header, token);
        await stream.WriteAsync(body, token);
        await stream.FlushAsync(token);
    }
}
