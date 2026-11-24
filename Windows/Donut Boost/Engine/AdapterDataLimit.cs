namespace DonutBoost.Engine;

internal sealed class AdapterDataLimit
{
    private readonly object _lock = new();
    private long _maximumBytes;
    private long _receivedBytes;
    private long _reservedBytes;

    public long MaximumBytes
    {
        get { lock (_lock) return _maximumBytes; }
        set { lock (_lock) _maximumBytes = value; }
    }

    public bool IsReached
    {
        get { lock (_lock) return _maximumBytes > 0 && _receivedBytes >= _maximumBytes; }
    }

    public void Reset()
    {
        lock (_lock)
        {
            _receivedBytes = 0;
            _reservedBytes = 0;
        }
    }

    public async Task<int> ReadAsync(Memory<byte> buffer,
        Func<Memory<byte>, CancellationToken, ValueTask<int>> read, CancellationToken token)
    {
        if (buffer.IsEmpty) return 0;
        int reserved;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            lock (_lock)
            {
                if (_maximumBytes > 0 && _receivedBytes >= _maximumBytes) return -1;
                reserved = _maximumBytes == 0
                    ? buffer.Length
                    : (int)Math.Clamp(_maximumBytes - _receivedBytes - _reservedBytes, 0, buffer.Length);
                if (reserved > 0)
                {
                    _reservedBytes += reserved;
                    break;
                }
            }
            await Task.Delay(35, token);
        }

        int readBytes = 0;
        try
        {
            readBytes = await read(buffer[..reserved], token);
            return readBytes;
        }
        finally
        {
            lock (_lock)
            {
                _receivedBytes += readBytes;
                _reservedBytes -= reserved;
            }
        }
    }
}
