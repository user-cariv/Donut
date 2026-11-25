using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using DonutBoost.Engine;
using DonutBoost.Models;

namespace DonutBoost;

public partial class MainWindow : Window
{
    private readonly NetworkBondingManager _bondingManager;
    private readonly MultiChannelDownloader _downloader;
    private readonly LicenseManager _licenseManager;
    private readonly System.Windows.Threading.DispatcherTimer _licenseTimer;
    private UpdateManifest? _availableUpdate;
    private System.Windows.Threading.DispatcherTimer? _autoUpdateTimer;
    private string _outputFolder;
    private bool _checkingStartupLicense;
    private bool _windowClosed;
    private CancellationTokenSource? _updateCancellation;
    private readonly DownloadQueue _downloadQueue = new();
    private readonly DispatcherTimer _downloadQueueTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private bool _runningTransfer;
    private readonly Dictionary<Guid, (string CookieHeader, DateTimeOffset Expiry)> _browserSessions = new();
    private readonly Dictionary<string, long> _savedAdapterDataLimits = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _dailyAdapterLimits = new(StringComparer.OrdinalIgnoreCase);
    private static readonly string SpeedLimitsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DonutBoost", "speed-limits.json");
    private sealed record SpeedLimits(Dictionary<string, long>? DataLimits, Dictionary<string, long>? DailyLimits,
        [property: JsonPropertyName("WifiMbps")] double DownloadMbps = 0);

    public MainWindow()
    {
        InitializeComponent();
        InitializePreviewMotion();

        WindowState = WindowState.Maximized;
        MaximizeIconText.Text = "🗗";
        StateChanged += (_, _) =>
        {
            MaximizeIconText.Text = WindowState == WindowState.Maximized ? "🗗" : "🗖";
        };

        Loaded += (_, _) => FitDashboardCardsToWorkArea();
        SizeChanged += (_, _) => FitDashboardCardsToWorkArea();
        LocationChanged += (_, _) => FitDashboardCardsToWorkArea();

        _outputFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "DonutBoost");
        OutputFolderTextBlock.Text = _outputFolder;
        if (WifiRoutePreferenceManager.HasSavedMetrics)
        {
            WifiRoutePreferenceText.Text = "Restore route settings";
            WifiRoutePreferenceButton.Visibility = Visibility.Visible;
        }

        _licenseManager = new LicenseManager();
        _bondingManager = new NetworkBondingManager();
        _downloader = new MultiChannelDownloader(_bondingManager, _licenseManager);
        LoadSpeedLimits();

        _bondingManager.AdaptersChanged += OnAdaptersChanged;
        _bondingManager.AdapterRatesUpdated += OnAdapterRatesUpdated;
        _downloader.ProgressUpdated += OnProgressUpdated;
        _downloader.StatusChanged += OnStatusChanged;
        _downloader.LogReceived += OnLogReceived;
        try { _downloadQueue.Load(); }
        catch (Exception ex) { OnLogReceived($"Could not load the download queue: {ex.Message}"); }
        _downloadQueueTimer.Tick += DownloadQueueTimer_Tick;


        UpdateLicenseUI();
        UpdateAdaptersUI();

        _licenseTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(10)
        };
        _licenseTimer.Tick += (_, _) =>
        {
            bool wasActive = _licenseManager.CurrentLicense.IsActive;
            _licenseManager.LoadLicense();
            bool isNowActive = _licenseManager.CurrentLicense.IsActive;

            if (wasActive && !isNowActive)
            {
                _downloader.Cancel();
                OnLogReceived("⏳ Your 24-hour pass has expired. Please enter a new redeem code to continue boosting.");
            }
            UpdateLicenseUI();
        };
        Loaded += MainWindow_Loaded;
        ContentRendered += StartSystemMergeOnFirstRender;

    }

    private void FitDashboardCardsToWorkArea()
    {
        if (WindowState == WindowState.Minimized || Content is not FrameworkElement layout ||
            PresentationSource.FromVisual(layout) is not HwndSource source)
            return;

        const double bottomGap = 8;
        double overlap = 0;
        if (WindowState == WindowState.Maximized)
        {
            const uint nearestMonitor = 2;
            nint monitor = MonitorFromWindow(source.Handle, nearestMonitor);
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (monitor != 0 && GetMonitorInfo(monitor, ref info))
            {
                double workBottom = layout.PointFromScreen(new Point(info.WorkArea.Left, info.WorkArea.Bottom)).Y;
                overlap = Math.Max(0, layout.ActualHeight - workBottom);
            }
        }

        PreviewFooter.Margin = new Thickness(0, 0, 0, overlap);
        var margin = new Thickness(0, 0, 0, bottomGap);
        if (NetworkAdaptersCard.Margin != margin)
            NetworkAdaptersCard.Margin = margin;
        if (SystemLogCard.Margin != margin)
            SystemLogCard.Margin = margin;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorRect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public MonitorRect MonitorArea, WorkArea;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint window, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

    public void SetIncomingDownloadLink(string? link)
    {
        if (string.IsNullOrWhiteSpace(link)) return;
        DownloadsNavigationButton.IsChecked = true;
        ShowPreviewDownloads_Click(this, new RoutedEventArgs());
        UrlTextBox.Text = link;
        RestoreFromBackground();
        UrlTextBox.Focus();
        OnLogReceived("Browser link received. Review it, then select Download to start.");
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= MainWindow_Loaded;
        _checkingStartupLicense = true;
        StartBoostButton.IsEnabled = false;
        RedeemCodeButton.IsEnabled = false;
        ActivateTrialButton.IsEnabled = false;
        try
        {
            if (_licenseManager.CurrentLicense.IsActive)
            {
                ActivationStatusTextBlock.Text = "Verifying this device activation...";
                var verification = await _licenseManager.VerifyStoredLicenseCloudAsync();
                if (_windowClosed) return;
                if (!verification.Success)
                    OnLogReceived($"[LICENSE] {verification.Message}");
            }

            if (!_licenseManager.CurrentLicense.IsActive)
            {
                var trialResult = await _licenseManager.ActivateTrialAsync();
                if (_windowClosed) return;
                if (trialResult.Success)
                    OnLogReceived("24-hour free pass activated for this device.");
            }

            UpdateLicenseUI();

            _licenseTimer.Start();
            _downloadQueueTimer.Start();
            _steamDownloadTimer.Tick += SteamDownloadTimer_Tick;
            _steamDownloadTimer.Start();
            _ = Task.Run(async () =>
            {
                await Task.Delay(2500);
                await CheckForAppUpdatesAsync(silent: true);
            });

            _autoUpdateTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(30)
            };
            _autoUpdateTimer.Tick += async (_, _) => await CheckForAppUpdatesAsync(silent: true);
            _autoUpdateTimer.Start();
        }
        catch (Exception ex)
        {
            if (_windowClosed) return;
            OnLogReceived($"Startup could not finish license setup: {ex.Message}");
            ActivationStatusTextBlock.Text = "Donut Boost opened, but license setup needs attention. Try activating again.";
            ActivationStatusTextBlock.Visibility = Visibility.Visible;
        }
        finally
        {
            _checkingStartupLicense = false;
            StartBoostButton.IsEnabled = _updateCancellation == null;
            RedeemCodeButton.IsEnabled = true;
            ActivateTrialButton.IsEnabled = true;
        }
    }
    private void UpdateLicenseUI()
    {
        HwidTextBlock.Text = _licenseManager.MachineHwid;
        bool isActive = _licenseManager.CurrentLicense.IsActive;

        if (isActive)
        {
            ActivationOverlayGrid.Visibility = Visibility.Collapsed;
            CloseActivationSheetButton.Visibility = Visibility.Visible;

            TitleBarLicenseText.Text = _licenseManager.CurrentLicense.BadgeText;
            TitleBarLicenseBadge.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(23, 44, 49));
            TitleBarLicenseText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(88, 225, 208));

            FooterLicenseText.Text = $"⚡ License: {_licenseManager.CurrentLicense.TierDisplayName}";
            FooterLicenseText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(48, 209, 88));
        }
        else
        {
            ActivationOverlayGrid.Visibility = Visibility.Visible;
            CloseActivationSheetButton.Visibility = Visibility.Collapsed;

            TitleBarLicenseText.Text = "🔒 UNLICENSED";
            TitleBarLicenseBadge.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(45, 20, 20));
            TitleBarLicenseText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 69, 58));

            FooterLicenseText.Text = "🔒 Unlicensed (Redeem Code Required)";
            FooterLicenseText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 69, 58));

            if (!string.IsNullOrWhiteSpace(_licenseManager.CurrentLicense.StatusMessage))
            {
                ActivationStatusTextBlock.Text = _licenseManager.CurrentLicense.StatusMessage;
                ActivationStatusTextBlock.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 69, 58));
                ActivationStatusTextBlock.Visibility = Visibility.Visible;
            }
        }
    }

    private async void RedeemCodeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_checkingStartupLicense) return;
        string code = RedeemCodeTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(code))
        {
            ActivationStatusTextBlock.Text = "Please enter a valid redeem code.";
            ActivationStatusTextBlock.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 69, 58));
            ActivationStatusTextBlock.Visibility = Visibility.Visible;
            return;
        }

        ActivationStatusTextBlock.Text = "Verifying code with secure cloud...";
        ActivationStatusTextBlock.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(251, 191, 36));
        ActivationStatusTextBlock.Visibility = Visibility.Visible;

        RedeemCodeButton.IsEnabled = false;
        var result = await _licenseManager.RedeemCodeAsync(code, () => !_windowClosed &&
            MessageBox.Show(this, "Restore this device's existing activation with its original expiry?",
                "Restore Donut Boost activation", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes);
        if (_windowClosed) return;
        RedeemCodeButton.IsEnabled = true;
        if (result.Success)
        {
            ActivationStatusTextBlock.Text = result.Message;
            ActivationStatusTextBlock.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(48, 209, 88));
            ActivationStatusTextBlock.Visibility = Visibility.Visible;

            OnLogReceived($"✓ [LICENSE] Successfully redeemed {result.Info.TierDisplayName}!");
            UpdateLicenseUI();
        }
        else
        {
            ActivationStatusTextBlock.Text = result.Message;
            ActivationStatusTextBlock.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 69, 58));
            ActivationStatusTextBlock.Visibility = Visibility.Visible;
        }
    }

    private async void ActivateTrialButton_Click(object sender, RoutedEventArgs e)
    {
        ActivateTrialButton.IsEnabled = false;
        var result = await _licenseManager.ActivateTrialAsync();
        ActivateTrialButton.IsEnabled = true;
        if (result.Success)
        {
            ActivationStatusTextBlock.Text = result.Message;
            ActivationStatusTextBlock.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(48, 209, 88));
            ActivationStatusTextBlock.Visibility = Visibility.Visible;

            OnLogReceived("✓ [LICENSE] 24-Hour Free Pass activated! Enjoy unlimited speed bonding.");
            UpdateLicenseUI();
        }
        else
        {
            ActivationStatusTextBlock.Text = result.Message;
            ActivationStatusTextBlock.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 69, 58));
            ActivationStatusTextBlock.Visibility = Visibility.Visible;
        }
    }

    private void PasteRedeemCode_Click(object sender, RoutedEventArgs e)
    {
        if (Clipboard.ContainsText())
        {
            RedeemCodeTextBox.Text = Clipboard.GetText().Trim().ToUpper();
        }
    }

    private void RedeemCodeTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            RedeemCodeButton_Click(sender, e);
        }
    }

    private void TitleBarLicenseButton_Click(object sender, RoutedEventArgs e)
    {
        if (ActivationOverlayGrid.Visibility == Visibility.Visible && _licenseManager.CurrentLicense.IsActive)
        {
            ActivationOverlayGrid.Visibility = Visibility.Collapsed;
        }
        else
        {
            ActivationOverlayGrid.Visibility = Visibility.Visible;
            CloseActivationSheetButton.Visibility = _licenseManager.CurrentLicense.IsActive ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void CloseActivationSheetButton_Click(object sender, RoutedEventArgs e)
    {
        if (_licenseManager.CurrentLicense.IsActive)
        {
            ActivationOverlayGrid.Visibility = Visibility.Collapsed;
        }
    }

    private void OpenWebsiteButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://user-cariv.github.io/Donut/",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            OnLogReceived($"Could not open website: {ex.Message}");
        }
    }

    private void OpenBrowserExtensionButton_Click(object sender, RoutedEventArgs e)
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "browser-extension");
        var setupFolder = Path.Combine(folder, "Select this folder to unpack in chrome extension");
        if (!File.Exists(Path.Combine(setupFolder, "manifest.json")))
        {
            MessageBox.Show("The bundled extension folder is missing. Select Check for Updates in Donut Boost to check for a newer installer.",
                "Donut Boost browser extension", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
            OnLogReceived("Open the Chrome Extensions shortcut, turn on Developer mode, and click Load unpacked. Select the folder shown in File Explorer.");
        }
        catch (Exception ex)
        {
            OnLogReceived($"Could not open the browser extension folder: {ex.Message}");
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
        }
        else
        {
            DragMove();
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeRestoreButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleMaximize();
    }

    private void ToggleMaximize()
    {
        if (WindowState == WindowState.Maximized)
        {
            WindowState = WindowState.Normal;
            MaximizeIconText.Text = "🗖";
        }
        else
        {
            WindowState = WindowState.Maximized;
            MaximizeIconText.Text = "🗗";
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        HideToNotificationArea();
    }

    private void UpdateFooterSpeedLimitText()
    {
        PreviewBoth.IsChecked = _bondingManager.CurrentMode == BondingMode.CombineAll;
        PreviewWifi.IsChecked = _bondingManager.CurrentMode == BondingMode.WifiOnly;
        PreviewMobile.IsChecked = _bondingManager.CurrentMode == BondingMode.MobileDataOnly;
        MovePreviewModePill();
        double cap = _downloader.Throttler.CurrentLimitMbps;
        string capText = cap > 0 ? $" • Donut Wi-Fi cap: {cap:0.##} Mbps" : "";
        FooterStatusModeText.Text = (_bondingManager.CurrentMode switch
        {
            BondingMode.CombineAll => "⚡ Donut downloads: all selected adapters",
            BondingMode.WifiOnly => "📶 Donut downloads: home adapters",
            BondingMode.MobileDataOnly => "📱 Donut downloads: mobile adapters",
            _ => "⚡ Donut download adapter selection"
        }) + capText;
    }

    private static bool IsValidNetworkSpeedLimit(double mbps) => mbps == 0 || mbps is >= 0.1 and <= 100000;

    private void LoadSpeedLimits()
    {
        try
        {
            if (!File.Exists(SpeedLimitsPath)) return;
            var settings = JsonSerializer.Deserialize<SpeedLimits>(File.ReadAllText(SpeedLimitsPath));
            if (settings is null) return;
            if (IsValidNetworkSpeedLimit(settings.DownloadMbps))
                SetDownloadLimit(settings.DownloadMbps);
            if (settings.DataLimits is not null)
                foreach (var (adapter, limit) in settings.DataLimits.Where(entry => entry.Value is > 0 and <= 10_000_000))
                    _savedAdapterDataLimits[adapter] = limit;
            if (settings.DailyLimits is not null)
                foreach (var (adapter, limit) in settings.DailyLimits.Where(entry => entry.Value is > 0 and <= 10_000_000))
                    _dailyAdapterLimits[adapter] = limit;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            OnLogReceived($"Could not load download speed limits: {ex.Message}");
        }
    }

    private void SaveSpeedLimits()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SpeedLimitsPath)!);
            string pending = SpeedLimitsPath + ".tmp";
            File.WriteAllText(pending, JsonSerializer.Serialize(new SpeedLimits(
                _savedAdapterDataLimits, _dailyAdapterLimits, _downloader.Throttler.CurrentLimitMbps)));
            File.Move(pending, SpeedLimitsPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            OnLogReceived($"Could not save download speed limits: {ex.Message}");
        }
    }

    private void AdapterToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is NetworkAdapterInfo adapter)
        {
            string state = adapter.IsEnabled ? "ON (Active)" : "OFF (Paused)";
            OnLogReceived($"[{adapter.IconGlyph} {adapter.Name} ({adapter.TypeLabel})] {state} for Donut's direct downloads. System routing was not changed.");

            SyncDownloadModeRadios();
            UpdateAdaptersUI();
        }
    }

    private void SyncDownloadModeRadios()
    {
        var adapters = _bondingManager.Adapters;
        bool wifiActive = adapters.Any(a => !a.IsMobilePhone && a.IsEnabled);
        bool mobileActive = adapters.Any(a => a.IsMobilePhone && a.IsEnabled);

        if (wifiActive && mobileActive)
        {
            _bondingManager.CurrentMode = BondingMode.CombineAll;
        }
        else if (wifiActive && !mobileActive)
        {
            _bondingManager.CurrentMode = BondingMode.WifiOnly;
        }
        else if (!wifiActive && mobileActive)
        {
            _bondingManager.CurrentMode = BondingMode.MobileDataOnly;
        }

        // Adapter selection is local to Donut's direct downloader. Never rewrite
        // system routes or interface metrics; Windows keeps the user's default Wi-Fi.
    }

    private void PostToUi(Action action)
    {
        if (_windowClosed) return;
        try
        {
            if (Dispatcher.CheckAccess()) action();
            else if (!Dispatcher.HasShutdownStarted && !Dispatcher.HasShutdownFinished)
                Dispatcher.BeginInvoke(action);
        }
        catch (InvalidOperationException) { /* Ignore callbacks arriving during window shutdown. */ }
    }
    private void OnAdaptersChanged()
    {
        PostToUi(UpdateAdaptersUI);
    }

    private void UpdateAdaptersUI()
    {
        var adapters = _bondingManager.Adapters;
        foreach (var adapter in adapters)
        {
            bool supportsDataBudgets = !adapter.IsWifi && !adapter.IsMobilePhone;
            long transferLimit = supportsDataBudgets ? _savedAdapterDataLimits.GetValueOrDefault(adapter.Id) : 0;
            long dailyLimit = supportsDataBudgets ? _dailyAdapterLimits.GetValueOrDefault(adapter.Id) : 0;
            if (adapter.DownloadDataLimitMb != transferLimit)
                adapter.SetDownloadDataLimitMb(transferLimit);
            if (adapter.DailyDownloadLimitMb != dailyLimit)
                adapter.SetDailyDownloadLimitMb(dailyLimit);
        }
        SyncPreviewAdapters(adapters.OrderBy(adapter => adapter.IsWifi ? 0 : adapter.IsMobilePhone ? 1 : 2).ToArray());

        int activeCount = adapters.Count(a => a.IsEnabled && a.IsOnline);
        AdapterCountTextBlock.Text = $"{activeCount} of {adapters.Count} Active";

        var activeNames = adapters.Where(a => a.IsEnabled && a.IsOnline).Select(a => $"{a.IconGlyph} {a.Name}");
        FooterActiveConnectionsText.Text = $"{activeCount} active connections";
        UpdateFooterSpeedLimitText();

        bool wifiOn = adapters.Any(adapter => adapter.IsWifi && adapter.IsEnabled && adapter.IsOnline);
        bool tetherOn = adapters.Any(adapter => adapter.IsEnabled && adapter.IsOnline && adapter.IsMobilePhone);
        bool ethernetOn = adapters.Any(adapter => adapter.IsEnabled && adapter.IsOnline && !adapter.IsMobilePhone &&
            (adapter.InterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Ethernet3Megabit
                or NetworkInterfaceType.FastEthernetT or NetworkInterfaceType.FastEthernetFx or NetworkInterfaceType.GigabitEthernet));
        WifiSummaryText.Text = $"Wi-Fi: {(wifiOn ? "On" : "Off")}";
        TetherSummaryText.Text = tetherOn ? "Mobile data: On" : $"Ethernet: {(ethernetOn ? "On" : "Off")}";
        TetherSummaryText.SetResourceReference(TextBlock.ForegroundProperty,
            tetherOn || ethernetOn ? "EthernetOnlineBrush" : "TextSecondaryBrush");
    }

    private void OnProgressUpdated(DownloadSession session)
    {
        if (session.Status == DownloadStatus.Cancelled) return;

        PostToUi(() =>
        {
            if (_downloader.CurrentSession?.Status == DownloadStatus.Cancelled) return;

            DownloadProgressBar.Value = session.ProgressPercentage;
            ProgressPercentTextBlock.Text = $"{session.ProgressPercentage:F1}%";
            FileSizeTextBlock.Text = $"{session.FormattedDownloadedSize} / {session.FormattedTotalSize}";
            EtaTextBlock.Text = $"ETA: {session.EstimatedTimeRemaining}";

            // Live Sticky Title Bar Status (Always in view when scrolling down)
            TitleBarLiveStatusPanel.Visibility = (session.Status == DownloadStatus.Downloading || session.Status == DownloadStatus.Paused) 
                ? Visibility.Visible : Visibility.Collapsed;
            TitleBarSpeedTextBlock.Text = $"File speed {session.FormattedSpeed}";
            TitleBarStatusBadge.Text = session.Status == DownloadStatus.Paused ? "⏸ PAUSED" : "⚡ BOOSTING SPEED";
            TitleBarProgressText.Text = $"{session.ProgressPercentage:F1}% (ETA: {session.EstimatedTimeRemaining})";

            // Live Sticky Footer Status Strip (Always in view when scrolling down)
            FooterLiveSpeedText.Text = $"File speed {session.FormattedSpeed}";
            FooterProgressText.Text = $"{session.ProgressPercentage:F1}% • {session.FormattedDownloadedSize} / {session.FormattedTotalSize} (ETA: {session.EstimatedTimeRemaining})";
        });
    }

    private void OnStatusChanged(DownloadSession session)
    {
        PostToUi(() =>
        {
            StatusBadgeTextBlock.Text = session.Status switch
            {
                DownloadStatus.Probing => "Probing Server...",
                DownloadStatus.Downloading => "⚡ BOOSTING SPEED",
                DownloadStatus.Paused => "⏸ PAUSED",
                DownloadStatus.Completed => "✓ Completed",
                DownloadStatus.Failed => "✕ Failed",
                DownloadStatus.Cancelled => "Ready to Boost",
                _ => "Ready"
            };

            if (session.Status == DownloadStatus.Cancelled)
            {
                // Cleanly remove movie name and reset task card to idle
                FileNameTextBlock.Text = "No active download";
                FileSizeTextBlock.Text = "-- / --";
                DownloadProgressBar.Value = 0;
                ProgressPercentTextBlock.Text = "0%";
                EtaTextBlock.Text = "Ready to Boost";
                StatusBadgeTextBlock.Text = "Ready to Boost";
                TitleBarLiveStatusPanel.Visibility = Visibility.Collapsed;
                FooterLiveSpeedText.Text = "0.0 Mbps";
                FooterProgressText.Text = "Ready to Boost";
            }
            else
            {
                FileNameTextBlock.Text = string.IsNullOrWhiteSpace(session.FileName) ? "Starting..." : session.FileName;
                FileSizeTextBlock.Text = session.TotalBytes > 0 ? $"{session.FormattedDownloadedSize} / {session.FormattedTotalSize}" : "--";
            }

            // Sticky Status update
            TitleBarLiveStatusPanel.Visibility = (session.Status == DownloadStatus.Downloading || session.Status == DownloadStatus.Paused) 
                ? Visibility.Visible : Visibility.Collapsed;
            TitleBarSpeedTextBlock.Text = $"File speed {session.FormattedSpeed}";
            TitleBarStatusBadge.Text = StatusBadgeTextBlock.Text;
            TitleBarProgressText.Text = $"{session.ProgressPercentage:F1}% (ETA: {session.EstimatedTimeRemaining})";

            FooterLiveSpeedText.Text = $"File speed {session.FormattedSpeed}";
            FooterProgressText.Text = $"{StatusBadgeTextBlock.Text} • {session.ProgressPercentage:F1}% (ETA: {session.EstimatedTimeRemaining})";

            bool isRunning = session.Status == DownloadStatus.Downloading;
            bool isPaused = session.Status == DownloadStatus.Paused;
            bool isProbing = session.Status == DownloadStatus.Probing;

            StartBoostButton.IsEnabled = !_runningTransfer && _updateCancellation == null && !_checkingStartupLicense
                && !isRunning && !isPaused && !isProbing;
            PauseResumeButton.IsEnabled = isRunning || isPaused;
            PauseResumeButtonText.Text = isPaused ? "▶ Resume" : "⏸ Pause";

            CancelDownloadButton.IsEnabled = isRunning || isPaused || isProbing;

            if (session.Status == DownloadStatus.Completed)
            {
                EtaTextBlock.Text = "Finished";
                FooterProgressText.Text = "✓ Completed Successfully!";
            }
            else if (session.Status == DownloadStatus.Cancelled)
            {
                FileNameTextBlock.Text = "No active download";
                FileSizeTextBlock.Text = "-- / --";
                DownloadProgressBar.Value = 0;
                ProgressPercentTextBlock.Text = "0%";
                EtaTextBlock.Text = "Ready to Boost";
                StatusBadgeTextBlock.Text = "Ready to Boost";
                TitleBarLiveStatusPanel.Visibility = Visibility.Collapsed;
                FooterLiveSpeedText.Text = "0.0 Mbps";
                FooterProgressText.Text = "Ready to Boost";
            }

            try
            {
                AdaptersItemsControl.Items.Refresh();
            }
            catch { }
        });
    }

    private void OnLogReceived(string message)
    {
        PostToUi(() =>
        {
            string history = LogOutputTextBlock.Text;
            LogOutputTextBlock.Text = $"{DateTime.Now:HH:mm:ss}  {message}\n{history[..Math.Min(history.Length, 12000)]}";
        });
    }

    private async void StartBoostButton_Click(object sender, RoutedEventArgs e)
    {
        if (_checkingStartupLicense || _updateCancellation != null || _runningTransfer) return;
        if (!_licenseManager.CurrentLicense.IsActive)
        {
            ActivationOverlayGrid.Visibility = Visibility.Visible;
            ActivationStatusTextBlock.Text = "Please enter a valid redeem code to start speed bonding.";
            ActivationStatusTextBlock.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 69, 58));
            ActivationStatusTextBlock.Visibility = Visibility.Visible;
            return;
        }

        string url = UrlTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(url))
        {
            MessageBox.Show("Enter a direct HTTP(S) download URL, a magnet link, or choose a .torrent file.", "Donut Boost", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var activeAdapters = _bondingManager.GetActiveEnabledAdapters();
        if (activeAdapters.Count == 0)
        {
            MessageBox.Show("No active network connections enabled for this mode! Please check your Wi-Fi, tether your mobile phone via USB, or switch to Combine Both.", 
                "Donut Boost", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try { await RunDownloadAsync(url, _outputFolder); }
        catch (Exception ex) { OnLogReceived($"Download error: {ex.Message}"); }
    }

    private async Task RunDownloadAsync(string input, string folder, long? expectedBytes = null, string? browserCookieHeader = null)
    {
        _runningTransfer = true;
        RestoreLiveSpeedGauge();
        UpdateSteamTask(null);
        StartBoostButton.IsEnabled = false;
        OnLogReceived("Preparing download?");
        try { await _downloader.StartDownloadAsync(input, folder, expectedBytes: expectedBytes, browserCookieHeader: browserCookieHeader); }
        finally
        {
            _runningTransfer = false;
            if (!_windowClosed)
            {
                StartBoostButton.IsEnabled = _updateCancellation == null && !_checkingStartupLicense;
            }
        }
    }

    internal BrowserCaptureReply HandleBrowserCapture(BrowserCaptureRequest request)
    {
        if (_windowClosed || request.Id == Guid.Empty) return new("unavailable");
        var existing = _downloadQueue.Entries.FirstOrDefault(entry => entry.Id == request.Id);
        if (existing is not null)
            return new(existing.Status switch
            {
                QueueEntryStatus.Queued or QueueEntryStatus.Downloading => "accepted",
                QueueEntryStatus.Completed => "completed",
                _ => "failed"
            });
        bool withBrowserSession = request.Action == "authenticated";
        if (request.Action != "queue" && !withBrowserSession) return new("missing");
        if (_checkingStartupLicense || _updateCancellation != null || !_licenseManager.CurrentLicense.IsActive ||
            _bondingManager.GetActiveEnabledAdapters().Count == 0 || _downloadQueue.Entries.Count > 500 ||
            (!withBrowserSession && request.ExpectedBytes is not > 0) ||
            request.Url is null || request.Url.Length > 8192 || request.Url.Any(char.IsControl) ||
            !Uri.TryCreate(request.Url, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo) ||
            uri.AbsolutePath.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase))
            return new("unavailable");
        if (withBrowserSession && (uri.Scheme != "https" ||
            request.CookieHeader is not { Length: > 0 and <= 4096 } header ||
            header.Any(character => character is < ' ' or > '~')))
            return new("unavailable");

        var name = Path.GetFileName(uri.LocalPath.TrimEnd('/'));
        var entry = new QueuedDownload(request.Id, uri.AbsoluteUri, _outputFolder,
            string.IsNullOrWhiteSpace(name) ? uri.Host : name, null, QueueEntryStatus.Queued,
            ExpectedBytes: request.ExpectedBytes, RequiresBrowserSession: withBrowserSession);
        _downloadQueue.Entries.Add(entry);
        try { _downloadQueue.Save(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        {
            _downloadQueue.Entries.Remove(entry);
            OnLogReceived("Could not save the browser download to the queue. Chrome or Edge will keep the download.");
            return new("unavailable");
        }
        if (withBrowserSession)
            _browserSessions[request.Id] = (request.CookieHeader!, DateTimeOffset.UtcNow.AddMinutes(5));
        return new("accepted");
    }

    private async void DownloadQueueTimer_Tick(object? sender, EventArgs e)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var id in _browserSessions.Where(session => session.Value.Expiry <= now).Select(session => session.Key).ToArray())
            _browserSessions.Remove(id);
        if (_windowClosed || _checkingStartupLicense || _updateCancellation != null || _runningTransfer ||
            !_licenseManager.CurrentLicense.IsActive || _bondingManager.GetActiveEnabledAdapters().Count == 0) return;

        var entry = _downloadQueue.Entries.FirstOrDefault(item => item.Status == QueueEntryStatus.Queued &&
            (item.ScheduledAt == null || item.ScheduledAt <= now));
        if (entry == null) return;

        string? browserCookieHeader = null;
        if (entry.RequiresBrowserSession)
        {
            if (!_browserSessions.Remove(entry.Id, out var browserSession) || browserSession.Expiry <= DateTimeOffset.UtcNow)
            {
                _downloadQueue.Replace(entry with { Status = QueueEntryStatus.Interrupted, Detail = "Send this link again from your signed-in browser." });
                SaveDownloadQueue();
                return;
            }
            browserCookieHeader = browserSession.CookieHeader;
        }

        _downloadQueue.Replace(entry with { Status = QueueEntryStatus.Downloading });
        SaveDownloadQueue();
        try
        {
            await RunDownloadAsync(entry.Input, entry.Folder, entry.ExpectedBytes, browserCookieHeader);
            if (_windowClosed) return;
            var session = _downloader.CurrentSession;
            var status = session?.Status switch
            {
                DownloadStatus.Completed => QueueEntryStatus.Completed,
                DownloadStatus.Cancelled => QueueEntryStatus.Cancelled,
                _ => QueueEntryStatus.Failed
            };
            _downloadQueue.Replace(entry with
            {
                Name = string.IsNullOrEmpty(session?.FileName) ? entry.Name : session.FileName,
                Status = status,
                Detail = session?.ErrorMessage
            });
        }
        catch (Exception ex)
        {
            if (_windowClosed) return;
            _downloadQueue.Replace(entry with { Status = QueueEntryStatus.Failed, Detail = ex.Message });
            OnLogReceived($"Queued download error: {ex.Message}");
        }
        SaveDownloadQueue();
    }

    private void SaveDownloadQueue()
    {
        try { _downloadQueue.Save(); }
        catch (Exception ex) { OnLogReceived($"Could not save the download queue: {ex.Message}"); }
    }

    private void PauseResumeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_downloader.IsPaused)
        {
            _downloader.Resume();
        }
        else
        {
            _downloader.Pause();
        }
    }

    private void CancelDownloadButton_Click(object sender, RoutedEventArgs e)
    {
        _downloader.Cancel();
        FileNameTextBlock.Text = "No active download";
        FileSizeTextBlock.Text = "-- / --";
        DownloadProgressBar.Value = 0;
        ProgressPercentTextBlock.Text = "0%";
        EtaTextBlock.Text = "Ready to Boost";
        StatusBadgeTextBlock.Text = "Ready to Boost";
        TitleBarLiveStatusPanel.Visibility = Visibility.Collapsed;
        FooterLiveSpeedText.Text = "0.0 Mbps";
        FooterProgressText.Text = "Ready to Boost";
    }

    private void BrowseFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select destination folder for boosted downloads",
            InitialDirectory = _outputFolder
        };
        if (dialog.ShowDialog() == true)
        {
            _outputFolder = dialog.FolderName;
            OutputFolderTextBlock.Text = _outputFolder;
        }
    }

    private void PasteUrlButton_Click(object sender, RoutedEventArgs e)
    {
        if (Clipboard.ContainsText())
        {
            UrlTextBox.Text = Clipboard.GetText().Trim();
        }
    }

    private void SelectTorrentButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose a BitTorrent metadata file",
            Filter = "Torrent files (*.torrent)|*.torrent",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog() == true)
            UrlTextBox.Text = dialog.FileName;
    }

    private void RefreshAdaptersButton_Click(object sender, RoutedEventArgs e)
    {
        _bondingManager.RefreshAdapters();
    }

    private async void PreferWifiRouteButton_Click(object sender, RoutedEventArgs e)
    {
        if (!WifiRoutePreferenceManager.HasSavedMetrics) return;
        if (MessageBox.Show(this, "Restore the Windows network settings Donut saved? Windows will ask for administrator approval.",
                "Restore network settings", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        WifiRoutePreferenceButton.IsEnabled = false;
        try
        {
            if (!await WifiRoutePreferenceManager.RunAsync(restore: true, wifiIndex: 0, mobileIndexes: Array.Empty<int>()))
                throw new InvalidOperationException("Windows did not apply the requested interface metrics.");
            WifiRoutePreferenceButton.Visibility = Visibility.Collapsed;
            OnLogReceived("Restored the saved Windows network settings.");
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            OnLogReceived("Windows network preference was not changed because administrator approval was cancelled.");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not change Windows network preference: {ex.Message}", "Donut Boost", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { WifiRoutePreferenceButton.IsEnabled = true; }
    }

    private async void PreferPreviewWifi_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.MenuItem command) return;
        var adapters = _bondingManager.Adapters;
        var wifi = adapters.FirstOrDefault(adapter => adapter.IsWifi && adapter.IsOnline && adapter.GatewayIp is not null);
        var phones = adapters.Where(adapter => adapter.IsMobilePhone && adapter.IsOnline && adapter.InterfaceIndex > 0)
            .Select(adapter => adapter.InterfaceIndex).ToArray();
        if (wifi is null || phones.Length == 0)
        {
            MessageBox.Show("Connect Wi-Fi and USB tethering first.", "Donut Boost", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (WifiRoutePreferenceManager.HasSavedMetrics)
        {
            MessageBox.Show("Restore the saved route settings before applying a new Wi-Fi preference.", "Donut Boost", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        command.IsEnabled = false;
        try
        {
            if (!await WifiRoutePreferenceManager.RunAsync(false, wifi.InterfaceIndex, phones))
                throw new InvalidOperationException("Windows did not apply the interface metrics.");
            OnLogReceived("Windows now prefers Wi-Fi over USB tethering for the normal default route.");
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            OnLogReceived("Administrator approval for the Wi-Fi preference was declined.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Donut Boost", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            if (WifiRoutePreferenceManager.HasSavedMetrics)
            {
                WifiRoutePreferenceText.Text = "Restore route settings";
                WifiRoutePreferenceButton.Visibility = Visibility.Visible;
            }
            command.IsEnabled = true;
        }
    }

    private void AdapterCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        UpdateAdaptersUI();
    }

    private void OnAdapterRatesUpdated(IReadOnlyList<(NetworkAdapterInfo Adapter, double BytesPerSecond)> sample)
    {
        PostToUi(() =>
        {
            foreach (var (adapter, bytesPerSecond) in sample)
                adapter.CurrentSpeedBps = bytesPerSecond;
            UpdatePreviewGauge(sample);
            double combinedReceiveBps = sample.Sum(item => item.BytesPerSecond);
            double mbps = combinedReceiveBps * 8 / 1_000_000.0;
            string speedText = mbps >= 1000 ? $"{(mbps / 1000):F2} Gbps" : $"{mbps:F1} Mbps";
            _liveGaugeMbps = mbps;
            if (!_showingSpeedTest) SetSpeedGaugeValue(mbps);

            bool isDirectDownloadActive = _downloader.CurrentSession != null &&
                (_downloader.CurrentSession.Status == DownloadStatus.Downloading ||
                 _downloader.CurrentSession.Status == DownloadStatus.Probing ||
                 _downloader.CurrentSession.Status == DownloadStatus.Paused);
            if (isDirectDownloadActive) return;

            FooterLiveSpeedText.Text = speedText;

            // Show per-adapter speed breakdown in footer
            var adapters = _bondingManager.Adapters;
            var parts = adapters
                .Where(a => a.IsOnline && a.CurrentSpeedBps > 0)
                .Select(a => $"{a.IconGlyph}{(a.CurrentSpeedBps * 8 / 1_000_000.0):F1}")
                .ToList();
            if (parts.Count > 1)
                FooterProgressText.Text = $"{string.Join(" + ", parts)} = {speedText}";
        });
    }

    private async Task CheckForAppUpdatesAsync(bool silent)
    {
        try
        {
            var (hasUpdate, manifest) = await UpdateManager.CheckForUpdatesAsync();
            PostToUi(() =>
            {
                if (_windowClosed || _updateCancellation != null) return;
                if (manifest == null)
                {
                    if (!silent) FooterUpdateCheckText.Text = "Update check unavailable. Retry";
                    return;
                }

                if (hasUpdate)
                {
                    bool newVersion = _availableUpdate?.LatestVersion != manifest.LatestVersion;
                    _availableUpdate = manifest;
                    if (newVersion || !silent) ShowRealUpdateBanner();
                    TitleBarUpdateBadge.Visibility = Visibility.Visible;
                    UpdateVersionTag.Text = $"v{manifest.LatestVersion}";
                    UpdateReleaseNotesText.Text = string.IsNullOrWhiteSpace(manifest.ReleaseNotes)
                        ? "A new version of Donut Boost is available."
                        : manifest.ReleaseNotes;
                    FooterUpdateCheckText.Text = $"Update available · v{manifest.LatestVersion}";
                    FooterUpdateCheckText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(168, 85, 247));
                    if (newVersion) OnLogReceived($"Donut Boost v{manifest.LatestVersion} is available. Select Update Now to install it.");
                }
                else
                {
                    _availableUpdate = null;
                    AppUpdateBanner.Visibility = Visibility.Collapsed;
                    TitleBarUpdateBadge.Visibility = Visibility.Collapsed;
                    if (!silent)
                    {
                        FooterUpdateCheckText.Text = $"Latest · v{UpdateManager.CurrentVersion}";
                        FooterUpdateCheckText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(48, 209, 88));
                    }
                }
            });
        }
        catch (Exception ex)
        {
            if (!silent)
            {
                PostToUi(() =>
                {
                    FooterUpdateCheckText.Text = "Update check unavailable. Retry";
                    OnLogReceived($"Could not check for updates: {ex.Message}");
                });
            }
        }
    }

    private async void UpdateNowButton_Click(object sender, RoutedEventArgs e)
    {
        if (_availableUpdate == null || _updateCancellation != null) return;

        UpdateProgressPanel.Visibility = Visibility.Visible;
        if (_runningTransfer)
        {
            UpdateProgressPercentText.Text = "Finish or cancel the active download before updating.";
            return;
        }

        RestoreLiveSpeedGauge();
        using var cancellation = new CancellationTokenSource();
        _updateCancellation = cancellation;
        StartBoostButton.IsEnabled = false;
        UpdateNowButton.IsEnabled = false;
        UpdateNowButtonText.Text = "Updating...";
        UpdateProgressBar.Value = 0;
        UpdateProgressPercentText.Text = "Checking for the latest installer...";

        try
        {
            var (hasUpdate, manifest) = await UpdateManager.CheckForUpdatesAsync(cancellation.Token);
            if (!hasUpdate || manifest == null)
                throw new InvalidOperationException("No newer installer could be verified. Select Try Again in a moment.");

            _availableUpdate = manifest;
            UpdateVersionTag.Text = $"v{manifest.LatestVersion}";
            UpdateProgressPercentText.Text = "Downloading update...";
            OnLogReceived($"Downloading Donut Boost v{manifest.LatestVersion}...");
            bool success = await UpdateManager.DownloadAndInstallUpdateAsync(
                manifest,
                (percent, readBytes, totalBytes) => PostToUi(() =>
                {
                    UpdateProgressBar.Value = percent;
                    UpdateProgressPercentText.Text = totalBytes > 0
                        ? $"Downloading update... {percent}% ({DownloadSession.FormatBytes(readBytes)} / {DownloadSession.FormatBytes(totalBytes)})"
                        : $"Downloading update... {DownloadSession.FormatBytes(readBytes)}";
                }),
                status => PostToUi(() =>
                {
                    UpdateProgressBar.Value = 100;
                    UpdateProgressPercentText.Text = status;
                }),
                cancellation.Token);

            if (!success)
                throw new InvalidOperationException("The update did not finish. Select Try Again.");

            if (!_windowClosed)
            {
                UpdateProgressPercentText.Text = "Update installed. Restarting Donut Boost…";
                Close();
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex)
        {
            PostToUi(() =>
            {
                UpdateNowButtonText.Text = "Try Again";
                UpdateProgressPercentText.Text = ex.Message;
                OnLogReceived($"Update error: {ex.Message}");
            });
        }
        finally
        {
            _updateCancellation = null;
            if (!_windowClosed)
            {
                UpdateNowButton.IsEnabled = _availableUpdate != null;
                StartBoostButton.IsEnabled = !_checkingStartupLicense;
            }
        }
    }


    private void TitleBarUpdateBadge_Click(object sender, RoutedEventArgs e)
    {
        ShowRealUpdateBanner();
    }

    private async void CheckForUpdatesButton_Click(object sender, RoutedEventArgs e)
    {
        FooterUpdateCheckText.Text = "Checking...";
        await CheckForAppUpdatesAsync(silent: false);
    }

    protected override void OnClosed(EventArgs e)
    {
        _windowClosed = true;
        _speedTestCancellation?.Cancel();
        DisposeNotificationArea();
        _stopSystemMerge?.Set();
        _downloadQueueTimer.Stop();
        _steamDownloadTimer.Stop();
        _updateCancellation?.Cancel();
        _autoUpdateTimer?.Stop();
        _licenseTimer?.Stop();
        _downloader.Cancel();
        _bondingManager.FlushDailyUsage();
        _bondingManager.Dispose();
        base.OnClosed(e);
    }
}
