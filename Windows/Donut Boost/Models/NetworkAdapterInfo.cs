using System.ComponentModel;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;

namespace DonutBoost.Models;

public enum BondingMode
{
    CombineAll,
    WifiOnly,
    MobileDataOnly
}

public class NetworkAdapterInfo : INotifyPropertyChanged
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    internal string? PhoneDeviceName { get; set; }
    public string PreviewDisplayName => PhoneDeviceName ?? (IsMobilePhone ? "USB tethering" : Name);
    public int InterfaceIndex { get; set; }
    public IPAddress? LocalIp { get; set; }
    public int IPv6InterfaceIndex { get; set; }
    public IPAddress? LocalIPv6 { get; set; }
    public bool HasDownloadAddress => LocalIp != null || LocalIPv6 != null;
    public IPAddress? GatewayIp { get; set; }
    public NetworkInterfaceType InterfaceType { get; set; }
    public long SpeedBitsPerSecond { get; set; }
    internal Engine.BandwidthThrottler DownloadThrottler { get; } = new();
    internal Engine.AdapterDataLimit TransferDataLimit { get; } = new();
    public double DownloadLimitMbps => DownloadThrottler.CurrentLimitMbps;
    public long DownloadDataLimitMb => TransferDataLimit.MaximumBytes / 1_000_000;
    public long DailyDownloadLimitMb => TransferDataLimit.DailyMaximumBytes / 1_000_000;

    internal void SetDailyDownloadLimitMb(long megabytes)
    {
        string key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Id.ToUpperInvariant())));
        string path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DonutBoost", "adapter-usage", key + ".json");
        TransferDataLimit.SetDailyLimit(megabytes * 1_000_000, path);
        OnPropertyChanged(nameof(DailyDownloadLimitMb));
    }

    internal void SetDownloadLimitMbps(double mbps)
    {
        DownloadThrottler.SetLimitMbps(mbps);
        OnPropertyChanged(nameof(DownloadLimitMbps));
    }

    internal void SetDownloadDataLimitMb(long megabytes)
    {
        TransferDataLimit.MaximumBytes = megabytes * 1_000_000;
        OnPropertyChanged(nameof(DownloadDataLimitMb));
    }

    private bool _isEnabled = true;
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled != value)
            {
                _isEnabled = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StatusPillText));
                OnPropertyChanged(nameof(StatusPillColor));
                OnPropertyChanged(nameof(ToggleTooltip));
            }
        }
    }

    public string StatusPillText => IsEnabled ? "ON" : "OFF";
    public string StatusPillColor => IsEnabled ? "#10B981" : "#71717A";
    public string ToggleTooltip => IsEnabled ? "Connection ACTIVE. Click to Turn OFF mid-download" : "Connection PAUSED. Click to Turn ON mid-download";

    private bool _isOnline = true;
    public bool IsOnline
    {
        get => _isOnline;
        set
        {
            if (_isOnline != value)
            {
                _isOnline = value;
                OnPropertyChanged();
            }
        }
    }

    private const int PreviewSampleCount = 20;
    private const double PreviewPointSpacing = 65.0 / (PreviewSampleCount - 1);
    private static readonly PointCollection EmptyPreviewSparkline = CreateEmptyPreviewSparkline();
    private readonly Queue<double> _previewHistory = new();
    private readonly double[] _previewStart = new double[PreviewSampleCount];
    private readonly double[] _previewTarget = new double[PreviewSampleCount];
    private long _previewSampleTime;
    public PointCollection PreviewSparkline { get; private set; } = EmptyPreviewSparkline;
    public string PreviewSparklineBrush => IsWifi ? "#FFAB30" : "#A593FF";

    private static PointCollection CreateEmptyPreviewSparkline()
    {
        var points = new PointCollection();
        points.Freeze();
        return points;
    }

    internal void AddPreviewSample(double bytesPerSecond)
    {
        RenderPreviewSample(immediate: true);
        if (_previewHistory.Count == PreviewSampleCount) _previewHistory.Dequeue();
        _previewHistory.Enqueue(Math.Max(0, bytesPerSecond));
        double scale = Math.Max(125_000, _previewHistory.Max());

        if (PreviewSparkline.Count == 0)
        {
            var baseline = new PointCollection(PreviewSampleCount);
            for (int index = 0; index < PreviewSampleCount; index++)
                baseline.Add(new Point(index * PreviewPointSpacing, 15));
            PublishPreviewSparkline(baseline);
        }

        for (int index = 0; index < PreviewSampleCount; index++)
        {
            _previewStart[index] = PreviewSparkline[index].Y;
            _previewTarget[index] = 15;
        }

        int sampleIndex = PreviewSampleCount - _previewHistory.Count;
        foreach (double sample in _previewHistory)
            _previewTarget[sampleIndex++] = 15 - 13 * sample / scale;
        _previewSampleTime = System.Diagnostics.Stopwatch.GetTimestamp();

        if (!System.Windows.SystemParameters.ClientAreaAnimation)
            RenderPreviewSample(immediate: true);
    }

    internal void RenderPreviewSample(bool immediate = false)
    {
        if (_previewSampleTime == 0) return;
        double progress = immediate ? 1 : Math.Min(1,
            System.Diagnostics.Stopwatch.GetElapsedTime(_previewSampleTime).TotalMilliseconds / 950);
        var points = new PointCollection(PreviewSampleCount + 1);
        double firstVisibleY = Interpolate(_previewStart[1], _previewTarget[0], progress);
        points.Add(new Point(0, Interpolate(_previewStart[0], firstVisibleY, progress)));
        for (int index = 1; index < PreviewSampleCount; index++)
        {
            if (index == 1 && progress >= 1) continue; // The left edge already contains this point.
            points.Add(new Point((index - progress) * PreviewPointSpacing,
                Interpolate(_previewStart[index], _previewTarget[index - 1], progress)));
        }
        points.Add(new Point((PreviewSampleCount - 1) * PreviewPointSpacing,
            Interpolate(_previewStart[^1], _previewTarget[^1], progress)));
        PublishPreviewSparkline(points);
        if (progress >= 1) _previewSampleTime = 0;
    }

    private static double Interpolate(double start, double end, double progress) =>
        start + (end - start) * progress;

    private void PublishPreviewSparkline(PointCollection points)
    {
        // Adapter discovery can create models off the WPF dispatcher.
        points.Freeze();
        PreviewSparkline = points;
        OnPropertyChanged(nameof(PreviewSparkline));
    }
    private double _currentSpeedBps;
    public double CurrentSpeedBps
    {
        get => _currentSpeedBps;
        set
        {
            _currentSpeedBps = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplaySpeed));
            OnPropertyChanged(nameof(SpeedMbps));
        }
    }

    private long _totalBytesDownloaded;
    public long TotalBytesDownloaded
    {
        get => Interlocked.Read(ref _totalBytesDownloaded);
        set
        {
            Interlocked.Exchange(ref _totalBytesDownloaded, value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(FormattedTotalBytes));
        }
    }

    public void AddDownloadedBytes(long bytes)
    {
        Interlocked.Add(ref _totalBytesDownloaded, bytes);
        OnPropertyChanged(nameof(TotalBytesDownloaded));
        OnPropertyChanged(nameof(FormattedTotalBytes));
    }

    public void NotifyMetricsChanged()
    {
        OnPropertyChanged(nameof(CurrentSpeedBps));
        OnPropertyChanged(nameof(SpeedMbps));
        OnPropertyChanged(nameof(DisplaySpeed));
        OnPropertyChanged(nameof(TotalBytesDownloaded));
        OnPropertyChanged(nameof(FormattedTotalBytes));
    }

    public double SpeedMbps => (CurrentSpeedBps * 8) / 1_000_000.0;

    public string DisplaySpeed
    {
        get
        {
            double mbps = SpeedMbps;
            if (mbps >= 1000)
            {
                return $"{(mbps / 1000):F2} Gbps";
            }
            return $"{mbps:F1} Mbps";
        }
    }

    public string FormattedTotalBytes => DownloadSession.FormatBytes(TotalBytesDownloaded);

    public bool IsMobilePhone => LooksLikeMobileAdapter(Name, Description);

    public static bool LooksLikeMobileAdapter(string? name, string? description)
    {
        string value = $"{name} {description}";
        return value.Contains("Apple Mobile", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("iPhone", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("RNDIS", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("Remote NDIS", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("Android", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("Samsung", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("Pixel", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("Tether", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("Cellular", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("Mobile", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("Phone", StringComparison.OrdinalIgnoreCase);
    }

    public bool IsWifi =>
        InterfaceType == NetworkInterfaceType.Wireless80211 ||
        HasWifiHint(Name) ||
        HasWifiHint(Description);

    private static bool HasWifiHint(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        (value.Contains("Wi-Fi", StringComparison.OrdinalIgnoreCase) ||
         value.Contains("WiFi", StringComparison.OrdinalIgnoreCase) ||
         value.Contains("Wireless", StringComparison.OrdinalIgnoreCase) ||
         value.Contains("WLAN", StringComparison.OrdinalIgnoreCase) ||
         value.Contains("802.11", StringComparison.OrdinalIgnoreCase) ||
         value.Contains("802_11", StringComparison.OrdinalIgnoreCase));

    public void NotifyAdapterMetadataChanged()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(InterfaceType));
        OnPropertyChanged(nameof(LocalIp));
        OnPropertyChanged(nameof(SpeedBitsPerSecond));
        OnPropertyChanged(nameof(IsMobilePhone));
        OnPropertyChanged(nameof(IsWifi));
        OnPropertyChanged(nameof(TypeLabel));
        OnPropertyChanged(nameof(PreviewDisplayName));
        OnPropertyChanged(nameof(IconGlyph));
        OnPropertyChanged(nameof(AccentColor));
        OnPropertyChanged(nameof(RoleBadgeText));
        OnPropertyChanged(nameof(RoleBadgeColor));
    }

    public string TypeLabel
    {
        get
        {
            if (IsMobilePhone) return "Phone 5G/4G (USB)";
            if (IsWifi) return "Home Wi-Fi";
            if (InterfaceType == NetworkInterfaceType.Ethernet) return "Ethernet / LAN";
            return InterfaceType.ToString();
        }
    }

    public string IconGlyph
    {
        get
        {
            if (IsMobilePhone) return "📱";
            if (IsWifi) return "📶";
            return "🌐";
        }
    }

    public string AccentColor
    {
        get
        {
            if (IsMobilePhone) return "#F59E0B"; // Amber / Gold for 5G Phone
            if (IsWifi) return "#38BDF8"; // Sky blue for Wi-Fi
            return "#10B981"; // Emerald green for Ethernet
        }
    }

    public string RoleBadgeText => IsWifi ? "Wi-Fi adapter" : (IsMobilePhone ? "Mobile data adapter" : "Network adapter");
    public string RoleBadgeColor => IsWifi ? "#10B981" : (IsMobilePhone ? "#F59E0B" : "#38BDF8");

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        var handler = PropertyChanged;
        if (handler != null)
        {
            var app = System.Windows.Application.Current;
            if (app != null && app.Dispatcher != null && !app.Dispatcher.CheckAccess())
            {
                app.Dispatcher.BeginInvoke(new Action(() =>
                {
                    handler(this, new PropertyChangedEventArgs(propertyName));
                }));
            }
            else
            {
                handler(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }
}
