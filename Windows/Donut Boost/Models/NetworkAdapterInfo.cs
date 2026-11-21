using System.ComponentModel;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.CompilerServices;

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
    public int InterfaceIndex { get; set; }
    public IPAddress? LocalIp { get; set; }
    public NetworkInterfaceType InterfaceType { get; set; }
    public long SpeedBitsPerSecond { get; set; }

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
    }

    public void NotifyMetricsChanged()
    {
        OnPropertyChanged(nameof(CurrentSpeedBps));
        OnPropertyChanged(nameof(SpeedMbps));
        OnPropertyChanged(nameof(DisplaySpeed));
        OnPropertyChanged(nameof(TotalBytesDownloaded));
        OnPropertyChanged(nameof(FormattedTotalBytes));
    }

    public double SpeedMbps => (CurrentSpeedBps * 8) / (1024 * 1024);

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

    public bool IsMobilePhone => 
        Description.Contains("Apple", StringComparison.OrdinalIgnoreCase) ||
        Description.Contains("NDIS", StringComparison.OrdinalIgnoreCase) ||
        Description.Contains("RNDIS", StringComparison.OrdinalIgnoreCase) ||
        Description.Contains("USB", StringComparison.OrdinalIgnoreCase) ||
        Description.Contains("Remote", StringComparison.OrdinalIgnoreCase) ||
        Description.Contains("Samsung", StringComparison.OrdinalIgnoreCase) ||
        Description.Contains("Pixel", StringComparison.OrdinalIgnoreCase) ||
        Description.Contains("Android", StringComparison.OrdinalIgnoreCase) ||
        Description.Contains("Tether", StringComparison.OrdinalIgnoreCase) ||
        Description.Contains("Phone", StringComparison.OrdinalIgnoreCase) ||
        Name.Contains("Cellular", StringComparison.OrdinalIgnoreCase) ||
        Name.Contains("Mobile", StringComparison.OrdinalIgnoreCase);

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

    public string RoleBadgeText => IsWifi ? "Default Internet (Wi-Fi)" : (IsMobilePhone ? "Dedicated Speed Pipeline" : "Bonded Network");
    public string RoleBadgeColor => IsWifi ? "#10B981" : (IsMobilePhone ? "#F59E0B" : "#38BDF8");

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
