namespace DonutBoost.Engine;

// Called under the download queue lock so a link's workers share one decision.
internal sealed class DownloadParallelism
{
    internal const int MaximumWorkers = 8;
    internal int Workers { get; private set; } = 4;
    private TimeSpan _lastSample;
    private TimeSpan _probeAfter;
    private long _lastBytes;
    private double _baselineRate;
    private bool _probing;
    private bool _hasSample;

    internal void Sample(long bytesReceived, TimeSpan now)
    {
        var elapsed = now - _lastSample;
        if (!_hasSample || elapsed > TimeSpan.FromSeconds(4) || bytesReceived < _lastBytes)
        {
            _hasSample = true;
            if (_probing) Workers -= 2;
            _probing = false;
            _lastSample = now;
            _lastBytes = bytesReceived;
            return;
        }
        if (elapsed < TimeSpan.FromSeconds(2)) return;

        double rate = (bytesReceived - _lastBytes) / elapsed.TotalSeconds;
        _lastSample = now;
        _lastBytes = bytesReceived;
        if (_probing)
        {
            _probing = false;
            if (rate < _baselineRate * 1.10)
            {
                Workers -= 2;
                _probeAfter = now + TimeSpan.FromSeconds(12);
                return;
            }
        }

        if (Workers < MaximumWorkers && rate > 0 && now >= _probeAfter)
        {
            _baselineRate = rate;
            Workers += 2;
            _probing = true;
        }
    }
}
