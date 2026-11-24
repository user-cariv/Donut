using System.IO;
using System.Text.Json;

namespace DonutBoost.Engine;

internal sealed class AdapterDataLimit
{
    private readonly object _lock = new();
    private long _maximumBytes;
    private long _receivedBytes;
    private long _reservedBytes;
    private long _dailyMaximumBytes;
    private long _dailyReceivedBytes;
    private long _lastSavedBytes;
    private string? _usagePath;
    private DateOnly _day = DateOnly.FromDateTime(DateTime.Today);
    private sealed record DailyUsage(DateOnly Day, long Bytes);

    public long DailyMaximumBytes { get { lock (_lock) return _dailyMaximumBytes; } }

    public void SetDailyLimit(long maximumBytes, string usagePath)
    {
        lock (_lock)
        {
            RollDay();
            if (_usagePath != usagePath)
            {
                _usagePath = usagePath;
                try
                {
                    if (File.Exists(usagePath) &&
                        JsonSerializer.Deserialize<DailyUsage>(File.ReadAllText(usagePath)) is { } saved && saved.Day == _day)
                        _dailyReceivedBytes = _lastSavedBytes = Math.Max(0, saved.Bytes);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
            }
            _dailyMaximumBytes = maximumBytes;
        }
    }

    public void FlushDailyUsage()
    {
        lock (_lock)
        {
            RollDay();
            SaveDailyUsage();
        }
    }

    private void RollDay()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        if (_day == today) return;
        _day = today;
        _dailyReceivedBytes = _lastSavedBytes = 0;
    }

    private void SaveDailyUsage()
    {
        if (_usagePath is null || _dailyReceivedBytes == _lastSavedBytes) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_usagePath)!);
            string temporary = _usagePath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(new DailyUsage(_day, _dailyReceivedBytes)));
            File.Move(temporary, _usagePath, overwrite: true);
            _lastSavedBytes = _dailyReceivedBytes;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private long RemainingBytes => Math.Min(
        _maximumBytes > 0 ? _maximumBytes - _receivedBytes : long.MaxValue,
        _dailyMaximumBytes > 0 ? _dailyMaximumBytes - _dailyReceivedBytes : long.MaxValue);

    public long MaximumBytes
    {
        get { lock (_lock) return _maximumBytes; }
        set { lock (_lock) _maximumBytes = value; }
    }

    public bool IsReached
    {
        get { lock (_lock) { RollDay(); return RemainingBytes <= 0; } }
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
                RollDay();
                long remaining = RemainingBytes;
                if (remaining <= 0) return -1;
                reserved = (int)Math.Clamp(remaining - _reservedBytes, 0, buffer.Length);
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
                RollDay();
                _dailyReceivedBytes += readBytes;
                _reservedBytes -= reserved;
                if (_dailyMaximumBytes > 0 &&
                    (_dailyReceivedBytes - _lastSavedBytes >= 4_000_000 || _dailyReceivedBytes >= _dailyMaximumBytes))
                    SaveDailyUsage();
            }
        }
    }
}
