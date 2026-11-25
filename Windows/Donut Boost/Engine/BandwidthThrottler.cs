using System.Diagnostics;

namespace DonutBoost.Engine;

public class BandwidthThrottler
{
    private long _maxBytesPerSecond = 0; // 0 = unlimited
    private double _nextAvailableTick;
    private readonly object _lock = new();

    public double CurrentLimitMbps
    {
        get
        {
            lock (_lock)
            {
                return _maxBytesPerSecond * 8.0 / 1_000_000.0;
            }
        }
    }

    public void SetLimitMbps(double mbps)
    {
        lock (_lock)
        {
            if (mbps <= 0)
            {
                _maxBytesPerSecond = 0;
            }
            else
            {
                _maxBytesPerSecond = (long)(mbps * 1_000_000.0 / 8.0);
            }
            _nextAvailableTick = 0;
        }
    }

    internal void ResetSchedule()
    {
        lock (_lock) _nextAvailableTick = 0;
    }

    // Each worker reserves time on the same clock, so parallel requests share the limit.
    public double ReserveDelayMilliseconds(int bytesRead)
    {
        lock (_lock)
        {
            if (_maxBytesPerSecond <= 0 || bytesRead <= 0) return 0;
            long now = Stopwatch.GetTimestamp();
            _nextAvailableTick = Math.Max(now, _nextAvailableTick) +
                bytesRead * (double)Stopwatch.Frequency / _maxBytesPerSecond;
            return (_nextAvailableTick - now) * 1000.0 / Stopwatch.Frequency;
        }
    }

    internal Task PaceWifiAsync(Models.NetworkAdapterInfo adapter, int bytesRead, CancellationToken token)
    {
        if (!adapter.IsWifi) return Task.CompletedTask;
        double delay = ReserveDelayMilliseconds(bytesRead);
        return delay >= 1 ? Task.Delay(TimeSpan.FromMilliseconds(delay), token) : Task.CompletedTask;
    }
}
