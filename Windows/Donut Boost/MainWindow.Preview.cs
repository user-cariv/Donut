using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
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

    private bool _syncingWifiLimit;

    private void StopWifiSliderAnimation(object sender, RoutedEventArgs e)
    {
        if (!_syncingWifiLimit) return;
        PreviewWifiLimit.BeginAnimation(System.Windows.Controls.Primitives.RangeBase.ValueProperty, null);
        _syncingWifiLimit = false;
    }

    private void PreviewWifiLimit_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncingWifiLimit || _downloader is null || PreviewWifiLimitText is null) return;
        SetWifiLimit(PreviewWifiLimitRate(e.NewValue), syncSlider: false);
    }

    private void SetWifiLimit(double rate, bool syncSlider = true)
    {
        if (_downloader.Throttler.CurrentLimitMbps != rate)
            _downloader.Throttler.SetLimitMbps(rate);
        if (syncSlider)
        {
            double target = rate == 0 ? 3 :
                rate <= 100 ? (rate - 50) / 50 : Math.Min(2, 1 + (rate - 100) / 100);
            double current = PreviewWifiLimit.Value;
            _syncingWifiLimit = true;
            var valueProperty = System.Windows.Controls.Primitives.RangeBase.ValueProperty;
            PreviewWifiLimit.Value = target;
            if (IsLoaded && SystemParameters.ClientAreaAnimation && current != target)
            {
                var animation = PreviewEase(target, 420);
                animation.From = current;
                animation.FillBehavior = FillBehavior.Stop;
                animation.Completed += (_, _) => _syncingWifiLimit = false;
                PreviewWifiLimit.BeginAnimation(valueProperty, animation);
            }
            else
            {
                PreviewWifiLimit.BeginAnimation(valueProperty, null);
                _syncingWifiLimit = false;
            }
        }
        PreviewWifiLimitText.Text = rate == 0 ? "Unlimited" : $"{rate:0.##} Mbps";
        UpdateFooterSpeedLimitText();
    }

    internal static double PreviewWifiLimitRate(double position)
    {
        if (position >= 2.96) return 0;
        if (position < 0) return Math.Max(0.1, Math.Round(50 + position * 50));
        if (position >= 2) return 200;
        if (position >= 1) return 100 + 20 * Math.Round((position - 1) * 5, MidpointRounding.AwayFromZero);
        return 50 + 10 * Math.Round(position * 5, MidpointRounding.AwayFromZero);
    }

    private void PreviewTorrentMode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox check) _downloader.BondTorrentPeers = check.IsChecked == true;
    }

    private double _shownWifi;
    private double _shownMobile;
    private double _targetWifi;
    private double _targetMobile;
    private TimeSpan _lastGaugeFrame;

    private void AdvancePreviewGauge(TimeSpan frame)
    {
        var elapsed = frame - _lastGaugeFrame;
        _lastGaugeFrame = frame;
        if (elapsed <= TimeSpan.Zero || (_shownWifi == _targetWifi && _shownMobile == _targetMobile))
            return;

        double weight = 1 - Math.Exp(-elapsed.TotalMilliseconds / 240);
        RenderPreviewGauge(ApproachGaugeTarget(_shownWifi, _targetWifi, weight),
            ApproachGaugeTarget(_shownMobile, _targetMobile, weight));
    }

    private static double ApproachGaugeTarget(double current, double target, double weight)
    {
        double next = current + (target - current) * weight;
        return Math.Abs(next - target) < 0.0005 ? target : next;
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
        _targetWifi = wifi / gaugeScale;
        _targetMobile = mobile / gaugeScale;
        if (!_previewRendering || !SystemParameters.ClientAreaAnimation)
            RenderPreviewGauge(_targetWifi, _targetMobile);
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
