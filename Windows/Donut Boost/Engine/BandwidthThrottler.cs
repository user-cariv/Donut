using System.Diagnostics;

namespace DonutBoost.Engine;

public class BandwidthThrottler
{
    private long _maxBytesPerSecond = 0; // 0 = unlimited
    private long _bytesConsumedInCurrentWindow = 0;
    private long _windowStartTimeTicks = Stopwatch.GetTimestamp();
    private readonly object _lock = new();

    public double CurrentLimitMbps
    {
        get
        {
            lock (_lock)
            {
                return _maxBytesPerSecond <= 0 ? 0 : (_maxBytesPerSecond * 8.0) / (1024.0 * 1024.0);
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
                _maxBytesPerSecond = (long)((mbps * 1024.0 * 1024.0) / 8.0);
            }
            _windowStartTimeTicks = Stopwatch.GetTimestamp();
            _bytesConsumedInCurrentWindow = 0;
        }
    }

    public async Task ThrottleAsync(int bytesRead, CancellationToken token)
    {
        if (_maxBytesPerSecond <= 0 || bytesRead <= 0) return;

        double delayMs = 0;
        lock (_lock)
        {
            if (_maxBytesPerSecond <= 0) return;

            long now = Stopwatch.GetTimestamp();
            double elapsedSec = (double)(now - _windowStartTimeTicks) / Stopwatch.Frequency;

            if (elapsedSec >= 0.5)
            {
                // Reset sliding window every 500ms for smooth, responsive rate pacing
                _windowStartTimeTicks = now;
                _bytesConsumedInCurrentWindow = bytesRead;
            }
            else
            {
                _bytesConsumedInCurrentWindow += bytesRead;
                double allowedBytes = _maxBytesPerSecond * elapsedSec;
                if (_bytesConsumedInCurrentWindow > allowedBytes)
                {
                    double targetSeconds = (double)_bytesConsumedInCurrentWindow / _maxBytesPerSecond;
                    delayMs = (targetSeconds - elapsedSec) * 1000.0;
                }
            }
        }

        if (delayMs > 2)
        {
            await Task.Delay((int)Math.Min(delayMs, 500), token);
        }
    }
}
