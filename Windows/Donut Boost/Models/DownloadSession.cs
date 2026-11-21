namespace DonutBoost.Models;

public class DownloadChunk
{
    public int Index { get; set; }
    public long StartOffset { get; set; }
    public long EndOffset { get; set; }
    public long BytesDownloaded { get; set; }
    public bool IsCompleted { get; set; }
    public string? AssignedAdapterId { get; set; }

    public long TotalBytes => EndOffset - StartOffset + 1;
}

public enum DownloadStatus
{
    Idle,
    Probing,
    Downloading,
    Paused,
    Completed,
    Failed,
    Cancelled
}

public class DownloadSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Url { get; set; } = string.Empty;
    public string DestinationPath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public long TotalBytes { get; set; }
    
    private long _downloadedBytes;
    public long DownloadedBytes 
    { 
        get => Interlocked.Read(ref _downloadedBytes); 
        set => Interlocked.Exchange(ref _downloadedBytes, value); 
    }

    public void AddDownloadedBytes(long bytes)
    {
        Interlocked.Add(ref _downloadedBytes, bytes);
    }

    public bool SupportsRangeRequests { get; set; }
    public DownloadStatus Status { get; set; } = DownloadStatus.Idle;
    public string? ErrorMessage { get; set; }

    public double CombinedSpeedBps { get; set; } // Instant bytes/sec
    private double _smoothedSpeedBps; // Smoothed for accurate ETA
    private double _displaySpeedBps; // Smoothed real-time display speed

    public void UpdateSpeed(double instantSpeedBps)
    {
        if (instantSpeedBps <= 0)
        {
            CombinedSpeedBps = 0;
            _displaySpeedBps = 0;
            return;
        }

        // Fast-response Exponential Moving Average for real-time speed display (no jitter or lag)
        _displaySpeedBps = (_displaySpeedBps <= 0) 
            ? instantSpeedBps 
            : (_displaySpeedBps * 0.65) + (instantSpeedBps * 0.35);

        CombinedSpeedBps = _displaySpeedBps;

        if (_smoothedSpeedBps <= 0)
        {
            _smoothedSpeedBps = instantSpeedBps;
        }
        else
        {
            // Exponential Moving Average to prevent wildly jumping ETA
            _smoothedSpeedBps = (_smoothedSpeedBps * 0.85) + (instantSpeedBps * 0.15);
        }
    }

    public List<DownloadChunk> Chunks { get; set; } = new();

    public double ProgressPercentage => TotalBytes > 0 
        ? Math.Clamp((double)DownloadedBytes / TotalBytes * 100.0, 0.0, 100.0) 
        : 0.0;

    public string FormattedTotalSize => FormatBytes(TotalBytes);
    public string FormattedDownloadedSize => FormatBytes(DownloadedBytes);

    public string FormattedSpeed
    {
        get
        {
            if (Status == DownloadStatus.Paused) return "0.0 Mbps";
            double mbps = (CombinedSpeedBps * 8) / 1_000_000.0;
            if (mbps >= 1000)
            {
                return $"{(mbps / 1000):F2} Gbps";
            }
            return $"{mbps:F1} Mbps";
        }
    }

    public string EstimatedTimeRemaining
    {
        get
        {
            if (Status == DownloadStatus.Paused) return "Paused";
            if (_smoothedSpeedBps <= 1024 || TotalBytes <= 0 || DownloadedBytes >= TotalBytes)
                return "--:--";

            long remainingBytes = TotalBytes - DownloadedBytes;
            double seconds = remainingBytes / _smoothedSpeedBps;
            return FormatETAFromSeconds(seconds);
        }
    }

    public static string FormatETAFromSeconds(double seconds)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds <= 0)
            return "--:--";
        var ts = TimeSpan.FromSeconds(seconds);
        if (ts.TotalHours >= 1)
            return $"{(int)ts.TotalHours}h {ts.Minutes:D2}m {ts.Seconds:D2}s";
        return $"{ts.Minutes:D2}m {ts.Seconds:D2}s";
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "0 B";
        string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
        int i = 0;
        double d = bytes;
        while (d >= 1024 && i < suffixes.Length - 1)
        {
            d /= 1024;
            i++;
        }
        return $"{d:F1} {suffixes[i]}";
    }
}
