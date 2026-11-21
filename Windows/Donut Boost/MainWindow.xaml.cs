using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
    private string _outputFolder;

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

        _licenseManager = new LicenseManager();
        _bondingManager = new NetworkBondingManager();
        _downloader = new MultiChannelDownloader(_bondingManager, _licenseManager);
        _gameBoostProxy = new GameBoostProxyServer(_bondingManager, _licenseManager);

        _bondingManager.AdaptersChanged += OnAdaptersChanged;
        _downloader.ProgressUpdated += OnProgressUpdated;
        _downloader.StatusChanged += OnStatusChanged;
        _downloader.LogReceived += OnLogReceived;

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
        _licenseTimer.Start();

        // Game Launcher Acceleration starts AUTO-ON by default as requested
        if (_licenseManager.CurrentLicense.IsActive)
        {
            EnableGameBoost();
        }
        else
        {
            GameBoostToggle.IsChecked = true;
            GameBoostBadgeBorder.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(10, 45, 27));
            GameBoostBadgeBorder.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(48, 209, 88));
            GameBoostBadgeText.Text = "🟢 AUTO-ON (Active)";
            GameBoostBadgeText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(48, 209, 88));
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

        var result = await _licenseManager.RedeemCodeAsync(code);
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

    private void ActivateTrialButton_Click(object sender, RoutedEventArgs e)
    {
        var result = _licenseManager.ActivateTrial();
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
        OnLogReceived("⚡ Switched to: Combine Both (Max Speed). Both Home Wi-Fi & Mobile Data are turned ON.");
        UpdateAdaptersUI();
    }

    private void ModeWifiOnly_Click(object sender, RoutedEventArgs e)
    {
        _bondingManager.SetMode(BondingMode.WifiOnly);
        OnLogReceived("📶 Switched to: Home Wi-Fi Only. Mobile Data is turned OFF mid-download. Traffic routed 100% to Wi-Fi.");
        UpdateAdaptersUI();
    }

    private void ModeMobileOnly_Click(object sender, RoutedEventArgs e)
    {
        _bondingManager.SetMode(BondingMode.MobileDataOnly);
        OnLogReceived("📱 Switched to: Mobile Data Only. Home Wi-Fi is turned OFF mid-download. Traffic routed 100% to Phone 5G/4G.");
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
        OnLogReceived("⚡ Wi-Fi Speed Limit: Unlimited (Both Home Wi-Fi & Mobile 5G uncapped).");
        UpdateFooterSpeedLimitText();
    }

    private void SpeedLimit50_Click(object sender, RoutedEventArgs e)
    {
        _downloader.Throttler.SetLimitMbps(50);
        CustomSpeedLimitTextBox.Text = string.Empty;
        OnLogReceived("⚡ Wi-Fi Speed Limit: Capped at 50 Mbps (Mobile 5G runs 100% full speed).");
        UpdateFooterSpeedLimitText();
    }

    private void SpeedLimit100_Click(object sender, RoutedEventArgs e)
    {
        _downloader.Throttler.SetLimitMbps(100);
        CustomSpeedLimitTextBox.Text = string.Empty;
        OnLogReceived("⚡ Wi-Fi Speed Limit: Capped at 100 Mbps (Mobile 5G runs 100% full speed).");
        UpdateFooterSpeedLimitText();
    }

    private void SpeedLimit200_Click(object sender, RoutedEventArgs e)
    {
        _downloader.Throttler.SetLimitMbps(200);
        CustomSpeedLimitTextBox.Text = string.Empty;
        OnLogReceived("⚡ Wi-Fi Speed Limit: Capped at 200 Mbps (Mobile 5G runs 100% full speed).");
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
            OnLogReceived($"⚡ Wi-Fi Speed Limit: Custom cap set to {mbps:F0} Mbps (Mobile 5G runs 100% uncapped).");
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
        string capText = cap > 0 ? $" • Wi-Fi Cap: {cap:F0} Mbps (5G Uncapped)" : "";
        FooterStatusModeText.Text = (_bondingManager.CurrentMode switch
        {
            BondingMode.CombineAll => "⚡ Mode: Combine Both (Max Speed)",
            BondingMode.WifiOnly => "📶 Mode: Home Wi-Fi Only",
            BondingMode.MobileDataOnly => "📱 Mode: Mobile Data Only",
            _ => "⚡ Mode: Custom Bonding"
        }) + capText;
    }

    private void AdapterToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is NetworkAdapterInfo adapter)
        {
            string state = adapter.IsEnabled ? "ON (Active)" : "OFF (Paused)";
            OnLogReceived($"⚡ [{adapter.IconGlyph} {adapter.Name} ({adapter.TypeLabel})] Switched to {state} in between downloading.");

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
    }

    private void OnAdaptersChanged()
    {
        Dispatcher.Invoke(UpdateAdaptersUI);
    }

    private void UpdateAdaptersUI()
    {
        var adapters = _bondingManager.Adapters;
        AdaptersItemsControl.ItemsSource = adapters;

        int activeCount = adapters.Count(a => a.IsEnabled && a.IsOnline);
        AdapterCountTextBlock.Text = $"{activeCount} of {adapters.Count} Active";

        var activeNames = adapters.Where(a => a.IsEnabled && a.IsOnline).Select(a => $"{a.IconGlyph} {a.Name}");
        FooterActiveConnectionsText.Text = $"{activeCount} Active ({string.Join(" + ", activeNames)})";
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

        Dispatcher.BeginInvoke(() =>
        {
            if (_downloader.CurrentSession?.Status == DownloadStatus.Cancelled) return;

            CombinedSpeedTextBlock.Text = session.FormattedSpeed;
            DownloadProgressBar.Value = session.ProgressPercentage;
            ProgressPercentTextBlock.Text = $"{session.ProgressPercentage:F1}%";
            FileSizeTextBlock.Text = $"{session.FormattedDownloadedSize} / {session.FormattedTotalSize}";
            EtaTextBlock.Text = $"ETA: {session.EstimatedTimeRemaining}";

            // Live Sticky Title Bar Status (Always in view when scrolling down)
            TitleBarLiveStatusPanel.Visibility = (session.Status == DownloadStatus.Downloading || session.Status == DownloadStatus.Paused) 
                ? Visibility.Visible : Visibility.Collapsed;
            TitleBarSpeedTextBlock.Text = session.FormattedSpeed;
            TitleBarStatusBadge.Text = session.Status == DownloadStatus.Paused ? "⏸ PAUSED" : "⚡ BOOSTING SPEED";
            TitleBarProgressText.Text = $"{session.ProgressPercentage:F1}% (ETA: {session.EstimatedTimeRemaining})";

            // Live Sticky Footer Status Strip (Always in view when scrolling down)
            FooterLiveSpeedText.Text = session.FormattedSpeed;
            FooterProgressText.Text = $"{session.ProgressPercentage:F1}% • {session.FormattedDownloadedSize} / {session.FormattedTotalSize} (ETA: {session.EstimatedTimeRemaining})";
        });
    }

    private void OnStatusChanged(DownloadSession session)
    {
        Dispatcher.Invoke(() =>
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
                CombinedSpeedTextBlock.Text = "0.0 Mbps";
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
            TitleBarSpeedTextBlock.Text = session.FormattedSpeed;
            TitleBarStatusBadge.Text = StatusBadgeTextBlock.Text;
            TitleBarProgressText.Text = $"{session.ProgressPercentage:F1}% (ETA: {session.EstimatedTimeRemaining})";

            FooterLiveSpeedText.Text = session.FormattedSpeed;
            FooterProgressText.Text = $"{StatusBadgeTextBlock.Text} • {session.ProgressPercentage:F1}% (ETA: {session.EstimatedTimeRemaining})";

            bool isRunning = session.Status == DownloadStatus.Downloading;
            bool isPaused = session.Status == DownloadStatus.Paused;
            bool isProbing = session.Status == DownloadStatus.Probing;

            StartBoostButton.IsEnabled = !isRunning && !isPaused && !isProbing;
            PauseResumeButton.IsEnabled = isRunning || isPaused;
            PauseResumeButtonText.Text = isPaused ? "▶ Resume" : "⏸ Pause";

            CancelDownloadButton.IsEnabled = isRunning || isPaused || isProbing;
            OpenFolderButton.IsEnabled = session.Status == DownloadStatus.Completed;

            if (session.Status == DownloadStatus.Completed)
            {
                CombinedSpeedTextBlock.Text = "0.0 Mbps";
                EtaTextBlock.Text = "Finished";
                FooterProgressText.Text = "✓ Completed Successfully!";
            }
            else if (session.Status == DownloadStatus.Cancelled)
            {
                FileNameTextBlock.Text = "No active download";
                FileSizeTextBlock.Text = "-- / --";
                DownloadProgressBar.Value = 0;
                ProgressPercentTextBlock.Text = "0%";
                CombinedSpeedTextBlock.Text = "0.0 Mbps";
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
        Dispatcher.Invoke(() =>
        {
            LogOutputTextBlock.Text = $"{message}\n{LogOutputTextBlock.Text}";
        });
    }

    private async void StartBoostButton_Click(object sender, RoutedEventArgs e)
    {
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
            MessageBox.Show("Please enter a valid download URL.", "Donut Boost", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var activeAdapters = _bondingManager.GetActiveEnabledAdapters();
        if (activeAdapters.Count == 0)
        {
            MessageBox.Show("No active network connections enabled for this mode! Please check your Wi-Fi, tether your mobile phone via USB, or switch to Combine Both.", 
                "Donut Boost", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        LogOutputTextBlock.Text = "Initializing multi-channel parallel engine...";
        await _downloader.StartDownloadAsync(url, _outputFolder);
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
        CombinedSpeedTextBlock.Text = "0.0 Mbps";
        EtaTextBlock.Text = "Ready to Boost";
        StatusBadgeTextBlock.Text = "Ready to Boost";
        TitleBarLiveStatusPanel.Visibility = Visibility.Collapsed;
        FooterLiveSpeedText.Text = "0.0 Mbps";
        FooterProgressText.Text = "Ready to Boost";
    }

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (Directory.Exists(_outputFolder))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _outputFolder,
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

    private void RefreshAdaptersButton_Click(object sender, RoutedEventArgs e)
    {
        _bondingManager.RefreshAdapters();
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
            ActivationStatusTextBlock.Text = "Please enter a valid redeem code to use Game Launcher Accelerator.";
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
        WindowsProxyManager.DisableProxy(); // Always ensure Windows system proxy is clean and untouched
        if (_gameBoostProxy.Start(9090))
        {
            GameBoostToggle.IsChecked = true;
            GameBoostBadgeBorder.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(10, 45, 27));
            GameBoostBadgeBorder.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(48, 209, 88));
            GameBoostBadgeText.Text = "🟢 AUTO-ON (Active)";
            GameBoostBadgeText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(48, 209, 88));
            OnLogReceived("🎮 Game Launcher Accelerator active. Monitoring Steam, Epic Games, EA, Rockstar.");
        }
        else
        {
            GameBoostToggle.IsChecked = false;
            OnLogReceived("✕ Failed to start Game Launcher Accelerator.");
        }
    }

    private void DisableGameBoost()
    {
        WindowsProxyManager.DisableProxy();
        _gameBoostProxy.Stop();
        GameBoostToggle.IsChecked = false;
        GameBoostBadgeBorder.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(35, 35, 45));
        GameBoostBadgeBorder.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(113, 113, 122));
        GameBoostBadgeText.Text = "⚪ OFF (Disabled)";
        GameBoostBadgeText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(161, 161, 170));
        GameBoostStreamsText.Text = "• 0 active game streams";
        OnLogReceived("🛑 Game Launcher Accelerator turned OFF.");
    }

    private long _selectedGameSizeTarget = 0; // 0 = Auto-detect

    private void GameSizeButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && long.TryParse(btn.Tag?.ToString(), out long gb))
        {
            _selectedGameSizeTarget = gb > 0 ? (gb * 1024L * 1024L * 1024L) : 0;
            UpdateGameSizeButtonsUI();

            if (_gameBoostProxy.ActiveConnections > 0)
            {
                OnGameProxyMetricsUpdated(_gameBoostProxy.TotalBytesDownloaded, 
                    _bondingManager.Adapters.Sum(a => a.CurrentSpeedBps), 
                    _gameBoostProxy.ActiveConnections, 
                    "Active Game");
            }
        }
    }

    private void CustomGameSizeButton_Click(object sender, RoutedEventArgs e)
    {
        if (!long.TryParse(CustomGameSizeTextBox.Text, out long gb) || gb < 1 || gb > 999)
        {
            MessageBox.Show("Enter a game target between 1 and 999 GB.", "Custom game size", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _selectedGameSizeTarget = gb * 1024L * 1024L * 1024L;
        UpdateGameSizeButtonsUI();
        if (_gameBoostProxy.ActiveConnections > 0)
        {
            OnGameProxyMetricsUpdated(_gameBoostProxy.TotalBytesDownloaded,
                _bondingManager.Adapters.Sum(a => a.CurrentSpeedBps),
                _gameBoostProxy.ActiveConnections,
                "Active Game");
        }
    }

    private void UpdateGameSizeButtonsUI()
    {
        var accentBrush = (System.Windows.Media.SolidColorBrush)FindResource("AccentBrush");
        var subtleBg = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(28, 28, 34));
        var activeBg = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(45, 30, 20));
        var borderBrush = (System.Windows.Media.SolidColorBrush)FindResource("CardBorderBrush");

        SizeAutoBtn.Background = _selectedGameSizeTarget == 0 ? activeBg : subtleBg;
        SizeAutoBtn.BorderBrush = _selectedGameSizeTarget == 0 ? accentBrush : borderBrush;

        Size10GBBtn.Background = _selectedGameSizeTarget == (10L * 1024 * 1024 * 1024) ? activeBg : subtleBg;
        Size10GBBtn.BorderBrush = _selectedGameSizeTarget == (10L * 1024 * 1024 * 1024) ? accentBrush : borderBrush;

        Size25GBBtn.Background = _selectedGameSizeTarget == (25L * 1024 * 1024 * 1024) ? activeBg : subtleBg;
        Size25GBBtn.BorderBrush = _selectedGameSizeTarget == (25L * 1024 * 1024 * 1024) ? accentBrush : borderBrush;

        Size50GBBtn.Background = _selectedGameSizeTarget == (50L * 1024 * 1024 * 1024) ? activeBg : subtleBg;
        Size50GBBtn.BorderBrush = _selectedGameSizeTarget == (50L * 1024 * 1024 * 1024) ? accentBrush : borderBrush;

        Size90GBBtn.Background = _selectedGameSizeTarget == (90L * 1024 * 1024 * 1024) ? activeBg : subtleBg;
        Size90GBBtn.BorderBrush = _selectedGameSizeTarget == (90L * 1024 * 1024 * 1024) ? accentBrush : borderBrush;

        Size100GBBtn.Background = _selectedGameSizeTarget == (100L * 1024 * 1024 * 1024) ? activeBg : subtleBg;
        Size100GBBtn.BorderBrush = _selectedGameSizeTarget == (100L * 1024 * 1024 * 1024) ? accentBrush : borderBrush;
    }

    private void OnGameProxyMetricsUpdated(long totalBytes, double instantSpeedBps, int activeConnections, string lastHost)
    {
        Dispatcher.BeginInvoke(() =>
        {
            double mbps = (instantSpeedBps * 8) / (1024 * 1024);
            string speedText = mbps >= 1000 ? $"{(mbps / 1000):F2} Gbps" : $"{mbps:F1} Mbps";

            // Update Game Launcher Accelerator Card Subtitle
            if (activeConnections > 0)
            {
                GameBoostStreamsText.Text = $"• ⚡ Boosting {lastHost} ({speedText})";
            }
            else if (!string.IsNullOrEmpty(lastHost) && !lastHost.Equals("Ready", StringComparison.OrdinalIgnoreCase))
            {
                GameBoostStreamsText.Text = "• 🟢 Game Launcher Standby (Ready to Boost)";
            }
            else
            {
                GameBoostStreamsText.Text = "• 0 active game streams";
            }

            // Direct URL download takes 100% full priority over background game monitoring!
            bool isDirectDownloadActive = _downloader.CurrentSession != null &&
                                          (_downloader.CurrentSession.Status == DownloadStatus.Downloading ||
                                           _downloader.CurrentSession.Status == DownloadStatus.Probing ||
                                           _downloader.CurrentSession.Status == DownloadStatus.Paused);

            if (!isDirectDownloadActive)
            {
                if (activeConnections > 0)
                {
                    GameSizeSelectorPanel.Visibility = Visibility.Visible;

                    // Calculate target size (Auto-detected or User-selected)
                    long detectedSize = _gameBoostProxy.DetectedGameSizeBytes;
                    long targetBytes = _selectedGameSizeTarget > 0 ? _selectedGameSizeTarget : detectedSize;
                    bool hasTargetSize = targetBytes > 0;

                    long remainingBytes = hasTargetSize ? Math.Max(0, targetBytes - totalBytes) : 0;
                    double etaSeconds = instantSpeedBps > 1024 ? ((double)remainingBytes / instantSpeedBps) : 0;
                    
                    // Without a trustworthy launcher size, do not show a fabricated progress percentage or ETA.
                    string etaText = !hasTargetSize ? "Set target size" : (etaSeconds > 86400 * 2
                        ? "Calculating..."
                        : (etaSeconds > 0 ? DownloadSession.FormatETAFromSeconds(etaSeconds) : "--:--"));

                    double percent = hasTargetSize ? Math.Clamp((double)totalBytes / targetBytes * 100.0, 0.0, 100.0) : 0;

                    CombinedSpeedTextBlock.Text = speedText;
                    StatusBadgeTextBlock.Text = "🎮 BOOSTING GAMES";
                    FileNameTextBlock.Text = $"Accelerating {lastHost}";
                    FileSizeTextBlock.Text = $"{DownloadSession.FormatBytes(totalBytes)} / {(hasTargetSize ? DownloadSession.FormatBytes(targetBytes) : "Size unknown")}";

                    // Real running progress bar + live ETA
                    DownloadProgressBar.IsIndeterminate = !hasTargetSize;
                    DownloadProgressBar.Value = percent;
                    ProgressPercentTextBlock.Text = hasTargetSize ? $"{percent:F1}%" : "";
                    EtaTextBlock.Text = $"ETA: {etaText}";

                    TitleBarLiveStatusPanel.Visibility = Visibility.Visible;
                    TitleBarSpeedTextBlock.Text = speedText;
                    TitleBarStatusBadge.Text = "🎮 BOOSTING GAMES";
                    TitleBarProgressText.Text = hasTargetSize ? $"{lastHost} ({percent:F1}%) • ETA: {etaText}" : $"{lastHost} • Size unknown";

                    FooterLiveSpeedText.Text = speedText;
                    FooterProgressText.Text = hasTargetSize
                        ? $"🎮 {lastHost} • {DownloadSession.FormatBytes(totalBytes)} / {DownloadSession.FormatBytes(targetBytes)} (ETA: {etaText})"
                        : $"🎮 {lastHost} • {DownloadSession.FormatBytes(totalBytes)} transferred • Set a target for progress and ETA";
                }
                else
                {
                    // Standby mode: No active download occurring -> HIDE ETA AND PROGRESS BAR
                    GameSizeSelectorPanel.Visibility = Visibility.Collapsed;
                    TitleBarLiveStatusPanel.Visibility = Visibility.Collapsed;

                    CombinedSpeedTextBlock.Text = "0.0 Mbps";
                    DownloadProgressBar.IsIndeterminate = false;
                    DownloadProgressBar.Value = 0;
                    ProgressPercentTextBlock.Text = "";
                    EtaTextBlock.Text = ""; // Completely hide ETA when not downloading!
                    FileSizeTextBlock.Text = "-- / --";

                    if (!string.IsNullOrEmpty(lastHost) && !lastHost.Equals("Ready", StringComparison.OrdinalIgnoreCase))
                    {
                        StatusBadgeTextBlock.Text = "Standby (Ready to Boost)";
                        FileNameTextBlock.Text = "Game Launcher detected (Standby)";
                        FooterLiveSpeedText.Text = "0.0 Mbps";
                        FooterProgressText.Text = "🎮 Game Launcher Standby • Ready to boost downloads on Steam, Epic, Rockstar, EA, Ubisoft, Battle.net, Riot, Xbox...";
                    }
                    else
                    {
                        StatusBadgeTextBlock.Text = "Ready to Boost";
                        FileNameTextBlock.Text = "No active download";
                        FooterLiveSpeedText.Text = "0.0 Mbps";
                        FooterProgressText.Text = "Ready to Boost";
                    }
                }
            }
            else
            {
                GameSizeSelectorPanel.Visibility = Visibility.Collapsed;
            }
        });
    }

    protected override void OnClosed(EventArgs e)
    {
        _licenseTimer?.Stop();
        WindowsProxyManager.DisableProxy();
        _gameBoostProxy.Dispose();
        _downloader.Cancel();
        _bondingManager.Dispose();
        base.OnClosed(e);
    }
}
