using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using DonutBoost.Models;

namespace DonutBoost.Engine;

internal sealed record UsenetServer(string Host, int Port, string User, string Password, int Connections);

internal static class UsenetDownload
{
    private const int MaxArticle = 8 * 1024 * 1024;
    private static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(value =>
    {
        uint entry = (uint)value;
        for (int bit = 0; bit < 8; bit++)
            entry = (entry >> 1) ^ ((entry & 1) != 0 ? 0xEDB88320u : 0);
        return entry;
    }).ToArray();

    internal sealed record Article(string Name, long Length, long Offset, byte[] Data);

    internal static bool IsInput(string input) => Path.IsPathFullyQualified(input) &&
        !input.StartsWith(@"\\", StringComparison.Ordinal) &&
        string.Equals(Path.GetExtension(input), ".nzb", StringComparison.OrdinalIgnoreCase);

    internal static async Task<string> RunAsync(string input, string folder, UsenetServer server,
        Func<List<NetworkAdapterInfo>> getAdapters, BandwidthThrottler globalLimit, Func<bool> paused,
        Action<int, NetworkAdapterInfo> received, CancellationToken token)
    {
        using var xml = XmlReader.Create(input, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Ignore,
            XmlResolver = null,
            MaxCharactersInDocument = 8 * 1024 * 1024
        });
        var root = XDocument.Load(xml).Root ?? throw new InvalidDataException("The NZB file is empty.");
        var ns = root.Name.Namespace;
        var files = root.Elements(ns + "file").Select(file =>
            file.Element(ns + "segments")?.Elements(ns + "segment")
                .OrderBy(segment => (int?)segment.Attribute("number"))
                .Select(segment => ValidateId(segment.Value.Trim())).ToArray() ?? []).ToArray();
        if (root.Name.LocalName != "nzb" || files.Length == 0 ||
            files.Any(parts => parts.Length == 0 || parts.Distinct(StringComparer.Ordinal).Count() != parts.Length))
            throw new InvalidDataException("The NZB contains missing or duplicated articles.");

        Directory.CreateDirectory(folder);
        string staging = Path.Combine(folder, ".donut-nzb-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        var connections = new ConcurrentDictionary<string, ConcurrentBag<NntpConnection>>();
        int requested = 0;
        try
        {
            for (int fileIndex = 0; fileIndex < files.Length; fileIndex++)
            {
                string? name = null;
                long length = 0;
                long possibleLength = (long)files[fileIndex].Length * MaxArticle;
                var ranges = new List<(long Start, long End)>();
                using var file = File.OpenHandle(Path.Combine(staging, $"part-{fileIndex}.tmp"),
                    FileMode.CreateNew, FileAccess.Write, FileShare.None, FileOptions.Asynchronous);
                await Parallel.ForEachAsync(files[fileIndex], new ParallelOptions
                {
                    CancellationToken = token,
                    MaxDegreeOfParallelism = server.Connections
                }, async (id, cancellation) =>
                {
                    while (paused())
                        await Task.Delay(200, cancellation);

                    var adapters = getAdapters().Where(adapter => !adapter.TransferDataLimit.IsReached).ToArray();
                    if (adapters.Length == 0)
                        throw new IOException("There is no active adapter with remaining download data.");
                    int start = (int)((uint)Interlocked.Increment(ref requested) % (uint)adapters.Length);
                    Exception? lastError = null;
                    for (int attempt = 0; attempt < adapters.Length + 1; attempt++)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        var adapter = adapters[(start + attempt) % adapters.Length];
                        var pool = connections.GetOrAdd(adapter.Id, _ => new ConcurrentBag<NntpConnection>());
                        NntpConnection? connection = null;
                        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
                        deadline.CancelAfter(TimeSpan.FromSeconds(90));
                        try
                        {
                            if (!pool.TryTake(out connection))
                            {
                                foreach (var idlePool in connections.Values)
                                {
                                    if (!idlePool.TryTake(out var idle)) continue;
                                    idle.Dispose();
                                    break;
                                }
                                connection = await NntpConnection.ConnectAsync(server, adapter, globalLimit, deadline.Token);
                            }
                            var article = await connection.ReadArticleAsync(id, deadline.Token);
                            if (article.Length > possibleLength)
                                throw new InvalidDataException("The file size cannot be covered by the articles in the NZB.");
                            lock (ranges)
                            {
                                name ??= article.Name;
                                if (length == 0)
                                    length = article.Length;
                                if (name != article.Name || length != article.Length)
                                    throw new InvalidDataException("The NZB articles disagree on the output file.");
                                ranges.Add((article.Offset, article.Offset + article.Data.Length));
                            }
                            await RandomAccess.WriteAsync(file, article.Data, article.Offset, cancellation);
                            received(article.Data.Length, adapter);
                            pool.Add(connection);
                            connection = null;
                            return;
                        }
                        catch (Exception ex) when (!cancellation.IsCancellationRequested &&
                            ex is (IOException or SocketException or OperationCanceledException))
                        {
                            lastError = ex;
                        }
                        finally { connection?.Dispose(); }
                    }
                    throw new IOException($"Could not retrieve a required Usenet article. {lastError?.Message}", lastError);
                });

                long end = 0;
                foreach (var range in ranges.OrderBy(range => range.Start))
                {
                    if (range.Start != end)
                        throw new InvalidDataException("The downloaded articles overlap or leave gaps.");
                    end = range.End;
                }
                if (end != length || name is null)
                    throw new InvalidDataException("The articles did not fill the advertised file.");
                RandomAccess.FlushToDisk(file);
                file.Dispose();
                File.Move(Path.Combine(staging, $"part-{fileIndex}.tmp"), Path.Combine(staging, $"{fileIndex + 1:D3}-{name}"));
            }
            token.ThrowIfCancellationRequested();
            string title = Path.GetFileNameWithoutExtension(input);
            string destination = Path.Combine(folder, $"Usenet-{title[..Math.Min(title.Length, 64)]}-{Guid.NewGuid():N}");
            Directory.Move(staging, destination);
            return destination;
        }
        finally
        {
            foreach (var pool in connections.Values)
                while (pool.TryTake(out var connection))
                    connection.Dispose();
            if (Directory.Exists(staging))
            {
                // Only the flat, app-created staging directory may be cleaned up here.
                foreach (string file in Directory.EnumerateFiles(staging))
                    File.Delete(file);
                Directory.Delete(staging);
            }
        }
    }

    private static string ValidateId(string id)
    {
        id = id.Trim('<', '>');
        if (id.Length is 0 or > 480 || id.Any(c => c <= 32 || c >= 127 || c is '<' or '>'))
            throw new InvalidDataException("The NZB contains an invalid message identifier.");
        return id;
    }

    internal static async Task<Article> DecodeAsync(Func<CancellationToken, ValueTask<string>> readLine, CancellationToken token)
    {
        string header;
        int preamble = 0;
        do
        {
            header = await readLine(token);
            if (header == "." || ++preamble > 100)
                throw new InvalidDataException("The article does not contain a yEnc attachment.");
        } while (!header.StartsWith("=ybegin ", StringComparison.Ordinal));

        int nameStart = header.IndexOf(" name=", StringComparison.Ordinal);
        if (nameStart < 0)
            throw new InvalidDataException("The yEnc attachment has no filename.");
        string name = header[(nameStart + 6)..].Trim().TrimEnd('.');
        if (name.Length is 0 or > 180 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new InvalidDataException("The yEnc attachment contains an invalid filename.");
        long size = LongField(header[..nameStart], "size");
        string line = await readLine(token);
        bool multipart = line.StartsWith("=ypart ", StringComparison.Ordinal);
        long begin = multipart ? LongField(line, "begin") - 1 : 0;
        long end = multipart ? LongField(line, "end") : size;
        if (begin < 0 || end <= begin || end > size || end - begin > MaxArticle)
            throw new InvalidDataException("The yEnc article range is invalid or too large.");
        if (multipart)
            line = await readLine(token);

        byte[] data = new byte[(int)(end - begin)];
        int count = 0;
        uint crc = uint.MaxValue;
        while (!line.StartsWith("=yend ", StringComparison.Ordinal))
        {
            if (line == ".")
                throw new InvalidDataException("The yEnc article ended early.");
            for (int i = line.StartsWith("..", StringComparison.Ordinal) ? 1 : 0; i < line.Length; i++)
            {
                int value = line[i];
                if (value == '=')
                {
                    if (++i == line.Length)
                        throw new InvalidDataException("The yEnc attachment ends in a broken escape.");
                    value = line[i] - 64;
                }
                if (count >= data.Length)
                    throw new InvalidDataException("The yEnc article exceeds its declared range.");
                byte decoded = unchecked((byte)(value - 42));
                data[count++] = decoded;
                crc = CrcTable[(byte)(crc ^ decoded)] ^ (crc >> 8);
            }
            line = await readLine(token);
        }
        if (count != data.Length || LongField(line, "size") != count ||
            !uint.TryParse(Field(line, multipart ? "pcrc32" : "crc32"), NumberStyles.HexNumber,
                CultureInfo.InvariantCulture, out uint expected) || expected != ~crc)
            throw new InvalidDataException("The yEnc article failed its size or CRC check.");
        for (int trailing = 0; await readLine(token) != "."; trailing++)
            if (trailing >= 100)
                throw new InvalidDataException("The yEnc attachment has an oversized postamble.");
        return new Article(name, size, begin, data);
    }

    private static string? Field(string line, string key) =>
        line.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(token => token.StartsWith(key + "=", StringComparison.Ordinal))?[(key.Length + 1)..];

    private static long LongField(string line, string key) =>
        long.TryParse(Field(line, key), NumberStyles.None, CultureInfo.InvariantCulture, out long value)
            ? value : throw new InvalidDataException("The yEnc article contains invalid size information.");

    private sealed class NntpConnection(SslStream stream, NetworkAdapterInfo adapter, BandwidthThrottler globalLimit) : IDisposable
    {
        private readonly byte[] _buffer = new byte[16 * 1024];
        private int _position;
        private int _length;

        public static async Task<NntpConnection> ConnectAsync(UsenetServer server, NetworkAdapterInfo adapter,
            BandwidthThrottler globalLimit, CancellationToken token)
        {
            foreach (var endpoint in await Dns.GetHostAddressesAsync(server.Host, token))
            {
                bool v6 = endpoint.AddressFamily == AddressFamily.InterNetworkV6;
                var address = v6 ? adapter.LocalIPv6 : adapter.LocalIp;
                if (address is null)
                    continue;
                var socket = NetworkBondingManager.CreateBoundSocket(address, v6 ? adapter.IPv6InterfaceIndex : adapter.InterfaceIndex);
                SslStream? stream = null;
                bool connected = false;
                try
                {
                    using var dialTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                    dialTimeout.CancelAfter(TimeSpan.FromSeconds(5));
                    await socket.ConnectAsync(endpoint, server.Port, dialTimeout.Token);
                    stream = new SslStream(new NetworkStream(socket, ownsSocket: true));
                    await stream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = server.Host }, token);
                    var connection = new NntpConnection(stream, adapter, globalLimit);
                    connection.Expect(await connection.ReadLineAsync(token), "200", "201");
                    if (server.User.Length != 0)
                    {
                        string response = await connection.CommandAsync("AUTHINFO USER " + server.User, token);
                        if (response.StartsWith("381 ", StringComparison.Ordinal))
                            response = await connection.CommandAsync("AUTHINFO PASS " + server.Password, token);
                        connection.Expect(response, "281");
                    }
                    connected = true;
                    return connection;
                }
                catch (SocketException) when (!token.IsCancellationRequested) { }
                catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
                finally
                {
                    if (!connected)
                    {
                        if (stream is null) socket.Dispose();
                        else stream.Dispose();
                    }
                }
            }
            throw new IOException("The selected adapter could not connect to the Usenet server.");
        }

        public async Task<Article> ReadArticleAsync(string id, CancellationToken token)
        {
            Expect(await CommandAsync("BODY <" + id + ">", token), "222");
            return await DecodeAsync(ReadLineAsync, token);
        }

        private void Expect(string response, params string[] accepted)
        {
            string code = response.Length >= 3 ? response[..3] : "unknown";
            if (!accepted.Contains(code))
                throw new IOException($"The Usenet server rejected a request with status {code}.");
        }

        private async ValueTask<string> CommandAsync(string command, CancellationToken token)
        {
            if (command.Contains('\r') || command.Contains('\n'))
                throw new InvalidDataException("The Usenet server settings contain an invalid character.");
            await stream.WriteAsync(Encoding.UTF8.GetBytes(command + "\r\n"), token);
            return await ReadLineAsync(token);
        }

        private async ValueTask<string> ReadLineAsync(CancellationToken token)
        {
            StringBuilder? partial = null;
            while (true)
            {
                if (_position == _length)
                {
                    if (!adapter.IsEnabled || !adapter.IsOnline)
                        throw new IOException("The selected Usenet adapter disconnected.");
                    _length = await adapter.TransferDataLimit.ReadAsync(_buffer, stream.ReadAsync, token);
                    _position = 0;
                    if (_length < 0)
                        throw new IOException("The selected Usenet adapter reached its data limit.");
                    if (_length == 0)
                        throw new IOException("The Usenet connection ended early.");
                    await globalLimit.PaceWifiAsync(adapter, _length, token);
                }
                int newline = Array.IndexOf(_buffer, (byte)'\n', _position, _length - _position);
                int end = newline >= 0 ? newline : _length;
                if ((partial?.Length ?? 0) + end - _position > 8192)
                    throw new InvalidDataException("The Usenet server sent an oversized line.");
                string text = Encoding.Latin1.GetString(_buffer, _position, end - _position);
                _position = newline >= 0 ? end + 1 : end;
                if (newline >= 0)
                    return (partial is null ? text : partial.Append(text).ToString()).TrimEnd('\r');
                (partial ??= new StringBuilder()).Append(text);
            }
        }

        public void Dispose() => stream.Dispose();
    }
}
