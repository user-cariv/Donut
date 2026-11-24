using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using DonutBoost.Engine;
using DonutBoost.Models;

namespace DonutBoost;

public partial class MainWindow : Window
{
    private readonly NetworkBondingManager _bondingManager;
    private readonly MultiChannelDownloader _downloader;
    private readonly GameBoostProxyServer _gameBoostProxy;
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

    public MainWindow()
    {
        InitializeComponent();

        WindowState = WindowState.Maximized;
        MaximizeIconText.Text = "🗗";
        StateChanged += (_, _) =>
        {
            MaximizeIconText.Text = WindowState == WindowState.Maximized ? "🗗" : "🗖";
        };

        _outputFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "DonutBoost");
        OutputFolderTextBlock.Text = _outputFolder;
        if (WifiRoutePreferenceManager.HasSavedMetrics)
            WifiRoutePreferenceText.Text = "Restore route settings";

        _licenseManager = new LicenseManager();
        _bondingManager = new NetworkBondingManager();
        _downloader = new MultiChannelDownloader(_bondingManager, _licenseManager);
        _gameBoostProxy = new GameBoostProxyServer(_bondingManager, _licenseManager);

        _bondingManager.AdaptersChanged += OnAdaptersChanged;
        _bondingManager.AdapterRatesUpdated += OnAdapterRatesUpdated;
        _downloader.ProgressUpdated += OnProgressUpdated;
        _downloader.StatusChanged += OnStatusChanged;
        _downloader.LogReceived += OnLogReceived;
        DownloadQueueList.ItemsSource = _downloadQueue.Entries;
        try { _downloadQueue.Load(); }
        catch (Exception ex) { OnLogReceived($"Could not load the download queue: {ex.Message}"); }
        _downloadQueueTimer.Tick += DownloadQueueTimer_Tick;

        _gameBoostProxy.LogReceived += OnLogReceived;
        _gameBoostProxy.ProxyMetricsUpdated += OnGameProxyMetricsUpdated;

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
                DisableGameBoost();
                _downloader.Cancel();
                OnLogReceived("⏳ Your 24-hour pass has expired. Please enter a new redeem code to continue boosting.");
            }
            UpdateLicenseUI();
        };
        Loaded += MainWindow_Loaded;

    }

    public void SetIncomingDownloadLink(string? link)
    {
        if (string.IsNullOrWhiteSpace(link)) return;
        UrlTextBox.Text = link;
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        OnLogReceived("Browser link received. Review it, then select BOOST DOWNLOAD to start.");
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= MainWindow_Loaded;
        _checkingStartupLicense = true;
        StartBoostButton.IsEnabled = false;
        RedeemCodeButton.IsEnabled = false;
        ActivateTrialButton.IsEnabled = false;
        GameBoostToggle.IsEnabled = false;
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
            if (_licenseManager.CurrentLicense.IsActive)
                EnableGameBoost();
            else
                GameBoostToggle.IsChecked = false;

            _licenseTimer.Start();
            _downloadQueueTimer.Start();
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
            GameBoostToggle.IsEnabled = true;
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
            TitleBarLicenseBadge.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(24, 42, 28));
            TitleBarLicenseBadge.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(48, 209, 88));
            TitleBarLicenseText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(48, 209, 88));

            FooterLicenseText.Text = $"⚡ License: {_licenseManager.CurrentLicense.TierDisplayName}";
            FooterLicenseText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(48, 209, 88));
        }
        else
        {
            ActivationOverlayGrid.Visibility = Visibility.Visible;
            CloseActivationSheetButton.Visibility = Visibility.Collapsed;

            TitleBarLicenseText.Text = "🔒 UNLICENSED";
            TitleBarLicenseBadge.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(45, 20, 20));
            TitleBarLicenseBadge.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 69, 58));
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
            if (GameBoostToggle.IsChecked == true)
            {
                EnableGameBoost();
            }
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
            if (GameBoostToggle.IsChecked == true)
            {
                EnableGameBoost();
            }
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
        Close();
    }

    private void ModeCombineBoth_Click(object sender, RoutedEventArgs e)
    {
        _bondingManager.SetMode(BondingMode.CombineAll);
        OnLogReceived("Selected all available adapters for Donut's own direct downloads. Windows' default route is unchanged.");
        UpdateAdaptersUI();
    }

    private void ModeWifiOnly_Click(object sender, RoutedEventArgs e)
    {
        _bondingManager.SetMode(BondingMode.WifiOnly);
        OnLogReceived("Selected home adapters for Donut's own direct downloads. Other apps and Windows routing are unchanged.");
        UpdateAdaptersUI();
    }

    private void ModeMobileOnly_Click(object sender, RoutedEventArgs e)
    {
        _bondingManager.SetMode(BondingMode.MobileDataOnly);
        OnLogReceived("Selected mobile adapters for Donut's own direct downloads. Other apps and Windows routing are unchanged.");
        UpdateAdaptersUI();
    }

    private static readonly System.Text.RegularExpressions.Regex _digitsOnly = new(@"^[0-9]+$", System.Text.RegularExpressions.RegexOptions.Compiled);

    private void CustomSpeedLimitTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !_digitsOnly.IsMatch(e.Text);
    }

    private void CustomSpeedLimitTextBox_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        if (e.DataObject.GetDataPresent(DataFormats.Text))
        {
            string text = ((string)e.DataObject.GetData(DataFormats.Text))?.Trim() ?? string.Empty;
            if (!_digitsOnly.IsMatch(text))
            {
                e.CancelCommand();
            }
        }
        else
        {
            e.CancelCommand();
        }
    }

    private void SpeedLimitUnlimited_Click(object sender, RoutedEventArgs e)
    {
        _downloader.Throttler.SetLimitMbps(0);
        CustomSpeedLimitTextBox.Text = string.Empty;
        OnLogReceived("⚡ Donut direct-download cap: Unlimited (Both Home Wi-Fi & Mobile 5G uncapped).");
        UpdateFooterSpeedLimitText();
    }

    private void SpeedLimit50_Click(object sender, RoutedEventArgs e)
    {
        _downloader.Throttler.SetLimitMbps(50);
        CustomSpeedLimitTextBox.Text = string.Empty;
        OnLogReceived("⚡ Donut direct-download cap: Capped at 50 Mbps (mobile adapters are not capped by Donut).");
        UpdateFooterSpeedLimitText();
    }

    private void SpeedLimit100_Click(object sender, RoutedEventArgs e)
    {
        _downloader.Throttler.SetLimitMbps(100);
        CustomSpeedLimitTextBox.Text = string.Empty;
        OnLogReceived("⚡ Donut direct-download cap: Capped at 100 Mbps (mobile adapters are not capped by Donut).");
        UpdateFooterSpeedLimitText();
    }

    private void SpeedLimit200_Click(object sender, RoutedEventArgs e)
    {
        _downloader.Throttler.SetLimitMbps(200);
        CustomSpeedLimitTextBox.Text = string.Empty;
        OnLogReceived("⚡ Donut direct-download cap: Capped at 200 Mbps (mobile adapters are not capped by Donut).");
        UpdateFooterSpeedLimitText();
    }

    private void ApplyCustomSpeedLimit_Click(object sender, RoutedEventArgs e)
    {
        string text = CustomSpeedLimitTextBox.Text.Trim();
        if (double.TryParse(text, out double mbps) && mbps > 0)
        {
            _downloader.Throttler.SetLimitMbps(mbps);
            SpeedLimitUnlimitedRadio.IsChecked = false;
            SpeedLimit50Radio.IsChecked = false;
            SpeedLimit100Radio.IsChecked = false;
            SpeedLimit200Radio.IsChecked = false;
            OnLogReceived($"⚡ Donut direct-download cap: Custom cap set to {mbps:F0} Mbps (mobile adapters are not capped by Donut).");
            UpdateFooterSpeedLimitText();
        }
        else
        {
            MessageBox.Show("Please enter a valid positive number in Mbps (e.g. 75).", "Donut Boost", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void UpdateFooterSpeedLimitText()
    {
        double cap = _downloader.Throttler.CurrentLimitMbps;
        string capText = cap > 0 ? $" • Donut download cap: {cap:F0} Mbps (mobile adapters uncapped)" : "";
        FooterStatusModeText.Text = (_bondingManager.CurrentMode switch
        {
            BondingMode.CombineAll => "⚡ Donut downloads: all selected adapters",
            BondingMode.WifiOnly => "📶 Donut downloads: home adapters",
            BondingMode.MobileDataOnly => "📱 Donut downloads: mobile adapters",
            _ => "⚡ Donut download adapter selection"
        }) + capText;
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
            ModeCombineBothRadio.IsChecked = true;
            _bondingManager.CurrentMode = BondingMode.CombineAll;
        }
        else if (wifiActive && !mobileActive)
        {
            ModeWifiOnlyRadio.IsChecked = true;
            _bondingManager.CurrentMode = BondingMode.WifiOnly;
        }
        else if (!wifiActive && mobileActive)
        {
            ModeMobileOnlyRadio.IsChecked = true;
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
        AdaptersItemsControl.ItemsSource = adapters;

        int activeCount = adapters.Count(a => a.IsEnabled && a.IsOnline);
        AdapterCountTextBlock.Text = $"{activeCount} of {adapters.Count} Active";

        var activeNames = adapters.Where(a => a.IsEnabled && a.IsOnline).Select(a => $"{a.IconGlyph} {a.Name}");
        FooterActiveConnectionsText.Text = $"{activeCount} selected ({string.Join(" + ", activeNames)})";
        UpdateFooterSpeedLimitText();

        // Summary pills in left speedometer card (Green for ON, Gray for OFF)
        ActiveAdaptersSummaryPanel.Children.Clear();
        foreach (var adp in adapters)
        {
            bool isAdpOn = adp.IsEnabled && adp.IsOnline;
            var pill = new Border
            {
                Background = isAdpOn 
                    ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(40, 48, 209, 88))
                    : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(30, 113, 113, 122)),
                BorderBrush = isAdpOn 
                    ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(48, 209, 88))
                    : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(113, 113, 122)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(3, 0, 3, 0)
            };
            var text = new TextBlock
            {
                Text = $"{adp.IconGlyph} {adp.Name}: {(isAdpOn ? "ON" : "OFF")}",
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = isAdpOn 
                    ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(48, 209, 88))
                    : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(161, 161, 170))
            };
            pill.Child = text;
            ActiveAdaptersSummaryPanel.Children.Add(pill);
        }
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
            OpenFolderButton.IsEnabled = session.Status == DownloadStatus.Completed;

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
            LogOutputTextBlock.Text = $"{message}\n{LogOutputTextBlock.Text}";
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

    private async Task RunDownloadAsync(string input, string folder)
    {
        _runningTransfer = true;
        StartBoostButton.IsEnabled = false;
        BondTorrentsCheckBox.IsEnabled = false;
        _downloader.BondTorrentPeers = BondTorrentsCheckBox.IsChecked == true;
        LogOutputTextBlock.Text = "Preparing download...";
        try { await _downloader.StartDownloadAsync(input, folder); }
        finally
        {
            _runningTransfer = false;
            if (!_windowClosed)
            {
                StartBoostButton.IsEnabled = _updateCancellation == null && !_checkingStartupLicense;
                BondTorrentsCheckBox.IsEnabled = true;
            }
        }
    }

    private void QueueDownloadButton_Click(object sender, RoutedEventArgs e)
    {
        string input = UrlTextBox.Text.Trim();
        bool validUrl = Uri.TryCreate(input, UriKind.Absolute, out var uri) &&
            uri.Scheme is "http" or "https" or "magnet";
        bool validFile = File.Exists(input) && Path.GetExtension(input).Equals(".torrent", StringComparison.OrdinalIgnoreCase);
        if (!validUrl && !validFile)
        {
            MessageBox.Show("Enter a direct download link, a magnet link, or choose a .torrent file.", "Download queue");
            return;
        }

        DateTimeOffset? scheduledAt = null;
        string schedule = QueueTimeTextBox.Text.Trim();
        if (schedule.Length > 0)
        {
            if (!TimeOnly.TryParseExact(schedule, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var startTime))
            {
                MessageBox.Show("Use a 24-hour time such as 22:30, or leave it blank to start when the queue is free.", "Download queue");
                return;
            }
            var next = DateTime.Today + startTime.ToTimeSpan();
            if (next <= DateTime.Now) next = next.AddDays(1);
            scheduledAt = new DateTimeOffset(next);
        }

        string name = validFile ? Path.GetFileName(input) : uri!.Scheme == "magnet" ? "Magnet download"
            : Path.GetFileName(uri.LocalPath.TrimEnd('/'));
        if (string.IsNullOrWhiteSpace(name)) name = uri?.Host ?? "Download";
        _downloadQueue.Entries.Add(new(Guid.NewGuid(), input, _outputFolder, name, scheduledAt, QueueEntryStatus.Queued));
        QueueTimeTextBox.Clear();
        SaveDownloadQueue();
    }

    private async void DownloadQueueTimer_Tick(object? sender, EventArgs e)
    {
        if (_windowClosed || _checkingStartupLicense || _updateCancellation != null || _runningTransfer ||
            !_licenseManager.CurrentLicense.IsActive || _bondingManager.GetActiveEnabledAdapters().Count == 0) return;

        var now = DateTimeOffset.Now;
        var entry = _downloadQueue.Entries.FirstOrDefault(item => item.Status == QueueEntryStatus.Queued &&
            (item.ScheduledAt == null || item.ScheduledAt <= now));
        if (entry == null) return;

        _downloadQueue.Replace(entry with { Status = QueueEntryStatus.Downloading });
        SaveDownloadQueue();
        try
        {
            await RunDownloadAsync(entry.Input, entry.Folder);
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

    private void RetryQueuedDownloadButton_Click(object sender, RoutedEventArgs e)
    {
        var entries = DownloadQueueList.SelectedItems.OfType<QueuedDownload>()
            .Where(entry => entry.Status is QueueEntryStatus.Failed or QueueEntryStatus.Cancelled or QueueEntryStatus.Interrupted)
            .ToArray();
        if (entries.Length == 0) return;
        foreach (var entry in entries)
            _downloadQueue.Replace(entry with { Status = QueueEntryStatus.Queued, ScheduledAt = null, Detail = null });
        SaveDownloadQueue();
    }

    private void RemoveQueuedDownloadButton_Click(object sender, RoutedEventArgs e)
    {
        var entries = DownloadQueueList.SelectedItems.OfType<QueuedDownload>()
            .Where(entry => entry.Status != QueueEntryStatus.Downloading)
            .ToArray();
        if (entries.Length == 0) return;
        foreach (var entry in entries)
            _downloadQueue.Entries.Remove(entry);
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

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var sessionPath = _downloader.CurrentSession?.DestinationPath;
        var folderToOpen = !string.IsNullOrWhiteSpace(sessionPath) && Directory.Exists(sessionPath)
            ? sessionPath
            : _outputFolder;
        if (Directory.Exists(folderToOpen))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = folderToOpen,
                UseShellExecute = true
            });
        }
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
        var wifi = _bondingManager.Adapters.FirstOrDefault(a => a.IsOnline && a.IsWifi && a.InterfaceIndex > 0);
        var mobileIndexes = _bondingManager.Adapters.Where(a => a.IsOnline && a.IsMobilePhone && a.InterfaceIndex > 0)
            .Select(a => a.InterfaceIndex).Distinct().ToArray();
        if (wifi == null || mobileIndexes.Length == 0)
        {
            MessageBox.Show("Connect home Wi-Fi and a phone tether first, then rescan connections.", "Prefer Home Wi-Fi", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        bool restore = WifiRoutePreferenceManager.HasSavedMetrics;
        string action = restore ? "restore the Windows network metrics saved before this change" : "make Windows prefer Wi-Fi over phone tethering until you restore the setting or restart Windows";
        if (MessageBox.Show($"Donut will {action}. Windows will ask for administrator approval. Game launchers will continue to use Windows networking. Continue?", "Windows Network Preference", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        WifiRoutePreferenceButton.IsEnabled = false;
        try
        {
            if (!await WifiRoutePreferenceManager.RunAsync(restore, wifi.InterfaceIndex, mobileIndexes))
                throw new InvalidOperationException("Windows did not apply the requested interface metrics.");
            WifiRoutePreferenceText.Text = restore ? "Prefer Wi-Fi" : "Restore route settings";
            OnLogReceived(restore
                ? "Restored the Windows interface metrics saved before the Wi-Fi preference was applied."
                : $"Windows now prefers Wi-Fi (interface {wifi.InterfaceIndex}) over phone tethering for the normal system route. Donut direct downloads can still use selected adapters.");
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

    private void AdapterCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        UpdateAdaptersUI();
    }

    private void GameBoostToggle_Click(object sender, RoutedEventArgs e)
    {
        if (!_licenseManager.CurrentLicense.IsActive)
        {
            GameBoostToggle.IsChecked = false;
            ActivationOverlayGrid.Visibility = Visibility.Visible;
            ActivationStatusTextBlock.Text = "Please enter a valid redeem code to use Game Launcher Monitor.";
            ActivationStatusTextBlock.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 69, 58));
            ActivationStatusTextBlock.Visibility = Visibility.Visible;
            return;
        }

        if (GameBoostToggle.IsChecked == true)
        {
            EnableGameBoost();
        }
        else
        {
            DisableGameBoost();
        }
    }

    private void EnableGameBoost()
    {
        if (_gameBoostProxy.Start(9090))
        {
            GameBoostToggle.IsChecked = true;
            GameBoostBadgeBorder.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(10, 45, 27));
            GameBoostBadgeBorder.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(48, 209, 88));
            GameBoostBadgeText.Text = "🟢 MONITORING";
            GameBoostBadgeText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(48, 209, 88));
            OnLogReceived("Game launcher activity monitor active. Adapter rates are measured; launcher-specific progress is shown only when the launcher exposes it.");
        }
        else
        {
            GameBoostToggle.IsChecked = false;
            OnLogReceived("✕ Failed to start Game Launcher Monitor.");
        }
    }

    private void DisableGameBoost()
    {
        _gameBoostProxy.Stop();
        GameBoostToggle.IsChecked = false;
        GameBoostBadgeBorder.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(35, 35, 45));
        GameBoostBadgeBorder.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(113, 113, 122));
        GameBoostBadgeText.Text = "⚪ OFF (Disabled)";
        GameBoostBadgeText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(161, 161, 170));
        GameBoostStreamsText.Text = "• 0 active game streams";
        OnLogReceived("🛑 Game Launcher Monitor turned OFF.");
    }

    private double _smoothedGameSpeedBps = 0;
    private string _lastGameEstimateKey = "";
    private long _lastGameProgressBytes = -1;
    private DateTime _lastGameProgressAt = DateTime.MinValue;

    private void OnAdapterRatesUpdated(double combinedReceiveBps)
    {
        PostToUi(() =>
        {
            // The gauge always shows physical adapter receive rates, even during file transfers.
            double mbps = combinedReceiveBps * 8 / 1_000_000.0;
            string speedText = mbps >= 1000 ? $"{(mbps / 1000):F2} Gbps" : $"{mbps:F1} Mbps";
            CombinedSpeedTextBlock.Text = speedText;

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

    private void OnGameProxyMetricsUpdated(long gameBytes, long gameSizeBytes, double adapterReceiveBps, bool hasLauncherActivity, bool hasReliableProgress, bool isEstimated, string lastHost)
    {
        PostToUi(() =>
        {
            double mbps = adapterReceiveBps * 8 / 1_000_000.0;
            string speedText = mbps >= 1000 ? $"{(mbps / 1000):F2} Gbps" : $"{mbps:F1} Mbps";

            // Build per-adapter speed breakdown string (📶300.0 + 📱500.0 = 800.0 Mbps)
            var adapters = _bondingManager.Adapters;
            var parts = adapters
                .Where(a => a.IsOnline && a.CurrentSpeedBps > 0)
                .Select(a => $"{a.IconGlyph}{(a.CurrentSpeedBps * 8 / 1_000_000.0):F1}")
                .ToList();
            string breakdownText = parts.Count > 1
                ? $" ({string.Join(" + ", parts)})"
                : "";

            if (hasLauncherActivity && hasReliableProgress)
                GameBoostStreamsText.Text = $"• Verified download: {lastHost}";
            else if (hasLauncherActivity)
                GameBoostStreamsText.Text = $"• Possible {lastHost} activity · download not verified";
            else if (!string.IsNullOrEmpty(lastHost) && !lastHost.Equals("Ready", StringComparison.OrdinalIgnoreCase))
                GameBoostStreamsText.Text = $"• {lastHost} detected · waiting for activity";
            else
                GameBoostStreamsText.Text = "• Waiting for game launcher activity";

            bool isDirectDownloadActive = _downloader.CurrentSession != null &&
                (_downloader.CurrentSession.Status == DownloadStatus.Downloading ||
                 _downloader.CurrentSession.Status == DownloadStatus.Probing ||
                 _downloader.CurrentSession.Status == DownloadStatus.Paused);

            if (isDirectDownloadActive) return;

                if (hasLauncherActivity)
            {
                bool hasSize = gameSizeBytes > 0;
                bool canShowProgress = hasSize && hasReliableProgress;
                string estimateKey = $"{lastHost}|{gameSizeBytes}";
                if (!string.Equals(estimateKey, _lastGameEstimateKey, StringComparison.Ordinal))
                {
                    _lastGameEstimateKey = estimateKey;
                    _smoothedGameSpeedBps = 0;
                    _lastGameProgressBytes = -1;
                    _lastGameProgressAt = DateTime.MinValue;
                }

                if (canShowProgress && gameBytes >= 0 && gameBytes != _lastGameProgressBytes)
                {
                    DateTime now = DateTime.UtcNow;
                    if (_lastGameProgressBytes >= 0 && gameBytes > _lastGameProgressBytes && _lastGameProgressAt != DateTime.MinValue)
                    {
                        double elapsed = (now - _lastGameProgressAt).TotalSeconds;
                        if (elapsed > 0)
                        {
                            double measuredRate = (gameBytes - _lastGameProgressBytes) / elapsed;
                            _smoothedGameSpeedBps = _smoothedGameSpeedBps <= 0
                                ? measuredRate
                                : (_smoothedGameSpeedBps * 0.65) + (measuredRate * 0.35);
                        }
                    }
                    else if (_lastGameProgressBytes >= 0 && gameBytes < _lastGameProgressBytes)
                    {
                        _smoothedGameSpeedBps = 0;
                    }

                    _lastGameProgressBytes = gameBytes;
                    _lastGameProgressAt = now;
                }

                long remaining = canShowProgress ? Math.Max(0, gameSizeBytes - gameBytes) : 0;
                bool hasFreshProgressRate = _lastGameProgressAt != DateTime.MinValue &&
                    (DateTime.UtcNow - _lastGameProgressAt).TotalSeconds <= 5;
                double etaRate = isEstimated ? adapterReceiveBps : hasFreshProgressRate ? _smoothedGameSpeedBps : 0;
                string eta = !canShowProgress
                    ? "Unavailable from this launcher"
                    : remaining == 0
                        ? "Finished"
                        : etaRate > 0
                            ? DownloadSession.FormatETAFromSeconds(remaining / etaRate)
                            : "Waiting for progress";
                double percent = canShowProgress ? Math.Clamp((double)gameBytes / gameSizeBytes * 100.0, 0.0, 100.0) : 0;
                string progressLabel = isEstimated ? $"~{Math.Min(percent, 99.9):F1}%" : $"{percent:F1}%";
                string estimatePrefix = isEstimated ? "~" : "";

                StatusBadgeTextBlock.Text = canShowProgress ? "VERIFIED DOWNLOAD" : "UNVERIFIED ACTIVITY";
                FileNameTextBlock.Text = $"Detected: {lastHost}";
                FileSizeTextBlock.Text = canShowProgress
                    ? $"{estimatePrefix}{DownloadSession.FormatBytes(gameBytes)} / {DownloadSession.FormatBytes(gameSizeBytes)}"
                    : hasSize
                        ? $"Size {DownloadSession.FormatBytes(gameSizeBytes)} · progress unavailable"
                        : "Game size unavailable from this launcher";

                DownloadProgressBar.IsIndeterminate = false;
                DownloadProgressBar.Value = percent;
                ProgressPercentTextBlock.Text = canShowProgress ? progressLabel : "Progress unavailable";
                EtaTextBlock.Text = $"ETA: {estimatePrefix}{eta}";

                TitleBarLiveStatusPanel.Visibility = Visibility.Visible;
                TitleBarSpeedTextBlock.Text = speedText;
                TitleBarStatusBadge.Text = canShowProgress ? "VERIFIED DOWNLOAD" : "UNVERIFIED";
                TitleBarProgressText.Text = canShowProgress
                    ? $"{lastHost} · {progressLabel} · ETA {estimatePrefix}{eta}"
                    : $"{lastHost} · adapter traffic {speedText} · progress unavailable";
                FooterProgressText.Text = canShowProgress
                    ? $"{lastHost} · {estimatePrefix}{DownloadSession.FormatBytes(gameBytes)} / {DownloadSession.FormatBytes(gameSizeBytes)} · ETA {estimatePrefix}{eta}{breakdownText}"
                    : $"{lastHost} · game progress/ETA not exposed; combined adapter receive rate {speedText}{breakdownText}";
            }
            else
            {
                _smoothedGameSpeedBps = 0;
                _lastGameProgressBytes = -1;
                _lastGameProgressAt = DateTime.MinValue;
                TitleBarLiveStatusPanel.Visibility = Visibility.Collapsed;
                DownloadProgressBar.IsIndeterminate = false;
                DownloadProgressBar.Value = 0;
                ProgressPercentTextBlock.Text = "";
                EtaTextBlock.Text = "";
                FileSizeTextBlock.Text = "--";

                if (!string.IsNullOrEmpty(lastHost) && !lastHost.Equals("Ready", StringComparison.OrdinalIgnoreCase))
                {
                    StatusBadgeTextBlock.Text = "READY";
                    FileNameTextBlock.Text = $"Launcher detected: {lastHost}";
                    FooterProgressText.Text = $"{lastHost} is idle · combined receive rate {speedText}{breakdownText}";
                }
                else
                {
                    StatusBadgeTextBlock.Text = "NETWORK MONITORING";
                    FileNameTextBlock.Text = "No active launcher download detected";
                    FooterProgressText.Text = $"Combined receive rate {speedText}{breakdownText}";
                }
            }
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
                    if (newVersion || !silent) AppUpdateBanner.Visibility = Visibility.Visible;
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
                UpdateProgressPercentText.Text = "Update installed. Open Donut Boost from Start if it hasn’t reopened.";
                UpdateNowButtonText.Text = "Installed";
                _availableUpdate = null;
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


    private void DismissUpdateBanner_Click(object sender, RoutedEventArgs e)
    {
        AppUpdateBanner.Visibility = Visibility.Collapsed;
    }

    private void TitleBarUpdateBadge_Click(object sender, RoutedEventArgs e)
    {
        AppUpdateBanner.Visibility = Visibility.Visible;
    }

    private async void CheckForUpdatesButton_Click(object sender, RoutedEventArgs e)
    {
        FooterUpdateCheckText.Text = "Checking...";
        await CheckForAppUpdatesAsync(silent: false);
    }

    protected override void OnClosed(EventArgs e)
    {
        _windowClosed = true;
        _downloadQueueTimer.Stop();
        _updateCancellation?.Cancel();
        _autoUpdateTimer?.Stop();
        _licenseTimer?.Stop();
        _gameBoostProxy.Dispose();
        _downloader.Cancel();
        _bondingManager.Dispose();
        base.OnClosed(e);
    }
}
