namespace DonutBoost.Engine;

// Use under the queue lock. After repeated stalls, allow one recovery request at a time.
internal sealed class DownloadLinkRecovery
{
    private int _failures;
    private TimeSpan _retryAfter;
    private bool _probeInFlight;

    internal bool TryStart(TimeSpan now, out bool recoveryProbe)
    {
        recoveryProbe = _failures >= 2;
        if (!recoveryProbe) return true;
        if (_probeInFlight || now < _retryAfter) return false;

        _probeInFlight = true;
        return true;
    }

    internal void Finish(TimeSpan now, bool recoveryProbe, bool madeProgress, bool stalled)
    {
        if (recoveryProbe) _probeInFlight = false;
        else if (_failures >= 2) return;

        if (madeProgress)
        {
            _failures = 0;
            return;
        }
        if (!stalled) return;

        _failures = Math.Min(_failures + 1, 5);
        if (_failures >= 2)
            _retryAfter = now + TimeSpan.FromSeconds(1 << (_failures - 1));
    }
}
