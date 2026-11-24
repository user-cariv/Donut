using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DonutBoost.Models;
using GaugePath = System.Windows.Shapes.Path;

namespace DonutBoost;

public partial class MainWindow
{
    private void ShowPreviewDownloads_Click(object sender, RoutedEventArgs e)
    {
        SystemLogCard.Visibility = Visibility.Collapsed;
        PreviewDownloadColumn.Visibility = Visibility.Visible;
        PreviewConnectionsColumn.Visibility = Visibility.Visible;
        RevealPreview(PreviewDownloadColumn);
        RevealPreview(PreviewConnectionsColumn);
    }

    private void ShowPreviewLog_Click(object sender, RoutedEventArgs e)
    {
        PreviewDownloadColumn.Visibility = Visibility.Collapsed;
        PreviewConnectionsColumn.Visibility = Visibility.Collapsed;
        SystemLogCard.Visibility = Visibility.Visible;
        RevealPreview(SystemLogCard);
    }

    private void PreviewMode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string tag } && Enum.TryParse(tag, out BondingMode mode))
        {
            _bondingManager.SetMode(mode);
            UpdateAdaptersUI();
        }
    }

    private void PreviewWifiLimit_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_downloader is null || PreviewWifiLimitText is null) return;
        double rate = PreviewWifiLimitRate(e.NewValue);
        if (_downloader.Throttler.CurrentLimitMbps != rate)
            _downloader.Throttler.SetLimitMbps(rate);
        PreviewWifiLimitText.Text = rate == 0 ? "Unlimited" : $"{rate:F0} Mbps";
        UpdateFooterSpeedLimitText();
    }

    internal static double PreviewWifiLimitRate(double position)
    {
        if (position >= 2.96) return 0;
        if (position >= 2) return 200;
        if (position >= 1) return 100 + 20 * Math.Round((position - 1) * 5, MidpointRounding.AwayFromZero);
        return 50 + 10 * Math.Round(position * 5, MidpointRounding.AwayFromZero);
    }

    private void PreviewTorrentMode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox check) _downloader.BondTorrentPeers = check.IsChecked == true;
    }

    private System.Windows.Threading.DispatcherTimer? _gaugeTransition;
    private double _shownWifi;
    private double _shownMobile;

    private void AnimatePreviewGauge(double wifi, double mobile)
    {
        _gaugeTransition?.Stop();
        if (!SystemParameters.ClientAreaAnimation || (wifi == _shownWifi && mobile == _shownMobile))
        {
            RenderPreviewGauge(wifi, mobile);
            return;
        }

        double startWifi = _shownWifi;
        double startMobile = _shownMobile;
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        var timer = new System.Windows.Threading.DispatcherTimer(
            System.Windows.Threading.DispatcherPriority.Render, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        timer.Tick += (_, _) =>
        {
            double progress = Math.Min(1, System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds / 200);
            double eased = progress * progress * (3 - 2 * progress);
            RenderPreviewGauge(startWifi + (wifi - startWifi) * eased, startMobile + (mobile - startMobile) * eased);
            if (progress >= 1) timer.Stop();
        };
        _gaugeTransition = timer;
        timer.Start();
    }

    private void RenderPreviewGauge(double wifi, double mobile)
    {
        _shownWifi = wifi;
        _shownMobile = mobile;
        SetArc(WifiRateArc, 110, wifi);
        SetArc(MobileRateArc, 90, mobile);
    }

    private void UpdatePreviewGauge(IReadOnlyList<(NetworkAdapterInfo Adapter, double BytesPerSecond)> sample)
    {
        double wifi = sample.Where(item => item.Adapter.IsWifi).Sum(item => item.BytesPerSecond);
        double mobile = sample.Where(item => item.Adapter.IsMobilePhone && !item.Adapter.IsWifi).Sum(item => item.BytesPerSecond);
        double total = sample.Sum(item => item.BytesPerSecond);
        double gaugeScale = Math.Max(12_500_000, total);
        AnimatePreviewGauge(wifi / gaugeScale, mobile / gaugeScale);
        foreach (var item in sample) item.Adapter.AddPreviewSample(item.BytesPerSecond);
    }

    private static void SetArc(GaugePath path, double radius, double fraction)
    {
        if (fraction <= 0)
        {
            path.Data = Geometry.Empty;
            return;
        }
        double angle = Math.Min(fraction, 0.998) * Math.Tau;
        var figure = new PathFigure { StartPoint = new Point(140, 140 - radius), IsFilled = false };
        figure.Segments.Add(new ArcSegment(new Point(140 + Math.Sin(angle) * radius, 140 - Math.Cos(angle) * radius),
            new Size(radius, radius), 0, angle > Math.PI, SweepDirection.Clockwise, true));
        path.Data = new PathGeometry(new[] { figure });
    }
}
