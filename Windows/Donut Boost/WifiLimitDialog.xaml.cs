using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Button = System.Windows.Controls.Button;

namespace DonutBoost;

public partial class WifiLimitDialog : Window
{
    private double _selection;
    private static readonly DependencyProperty GaugeFractionProperty = DependencyProperty.Register(
        "GaugeFraction", typeof(double), typeof(WifiLimitDialog),
        new PropertyMetadata(0d, (owner, args) => ((WifiLimitDialog)owner).DrawGauge((double)args.NewValue)));
    public double LimitMbps { get; private set; }

    public WifiLimitDialog(double currentLimitMbps = 30, bool isMobile = false)
    {
        InitializeComponent();
        if (isMobile)
        {
            Title = DialogHeading.Text = "Mobile data speed limit";
            ConnectionIcon.Data = Geometry.Parse("M5,0 H11 Q13,0 13,2 V18 Q13,20 11,20 H5 Q3,20 3,18 V2 Q3,0 5,0 Z M6,3 H10 M7,17 H9");
        }
        SelectPreset(currentLimitMbps);
        if (currentLimitMbps != 0 && !Presets.Children.OfType<Button>().Any(button => (string)button.Tag == CurrentSpeedText(currentLimitMbps)))
            CustomSpeed.Text = CurrentSpeedText(currentLimitMbps);
    }

    private static string CurrentSpeedText(double value) => value.ToString("0.########", CultureInfo.CurrentCulture);

    private void Preset_Click(object sender, RoutedEventArgs e) =>
        SelectPreset(double.Parse((string)((Button)sender).Tag, CultureInfo.InvariantCulture));

    private void Unlimited_Click(object sender, RoutedEventArgs e) => SelectPreset(0);

    private void SelectPreset(double value)
    {
        _selection = value;
        if (CustomSpeed.Text.Length == 0)
            RenderRate(value, highlightPreset: true);
        else
            CustomSpeed.Clear();
    }

    private bool TryGetRate(out double value)
    {
        value = _selection;
        return string.IsNullOrWhiteSpace(CustomSpeed.Text) ||
               (double.TryParse(CustomSpeed.Text.Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.CurrentCulture, out value)
                && value is >= 0.1 and <= 100_000);
    }

    private void CustomSpeed_Changed(object sender, TextChangedEventArgs e)
    {
        if (InputError is null) return;
        bool valid = TryGetRate(out double value);
        InputError.Visibility = valid ? Visibility.Collapsed : Visibility.Visible;
        if (valid) RenderRate(value, string.IsNullOrWhiteSpace(CustomSpeed.Text));
    }

    private void RenderRate(double value, bool highlightPreset)
    {
        Readout.Text = value == 0 ? "Unlimited" : value.ToString("0.##", CultureInfo.CurrentCulture);
        Readout.FontSize = Readout.Text.Length > 6 ? 32 : 48;
        ReadoutUnit.Visibility = value == 0 ? Visibility.Collapsed : Visibility.Visible;
        foreach (Button button in Presets.Children)
        {
            button.ClearValue(BackgroundProperty);
            button.ClearValue(ForegroundProperty);
            if (!highlightPreset || (string)button.Tag != CurrentSpeedText(value)) continue;
            button.Background = (Brush)FindResource("Gold");
            button.Foreground = Brushes.Black;
        }

        double target = value == 0 ? 1 : Math.Clamp((value - 10) / 40, 0, 1);
        double current = (double)GetValue(GaugeFractionProperty);
        if (IsLoaded && SystemParameters.ClientAreaAnimation &&
            target == (double)GetAnimationBaseValue(GaugeFractionProperty))
            return;
        if (IsLoaded && SystemParameters.ClientAreaAnimation && current != target)
        {
            SetValue(GaugeFractionProperty, target);
            BeginAnimation(GaugeFractionProperty, new DoubleAnimation(current, target, TimeSpan.FromMilliseconds(420))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop
            });
        }
        else
        {
            BeginAnimation(GaugeFractionProperty, null);
            SetValue(GaugeFractionProperty, target);
            DrawGauge(target);
        }
    }

    private void DrawGauge(double fraction)
    {
        double angle = Math.PI * fraction;
        Point tip = new(130 - 118 * Math.Cos(angle), 128 - 118 * Math.Sin(angle));
        Canvas.SetLeft(RateDot, tip.X - RateDot.Width / 2);
        Canvas.SetTop(RateDot, tip.Y - RateDot.Height / 2);
        RateArc.Data = angle == 0 ? Geometry.Empty : new PathGeometry(new[]
        {
            new PathFigure(new Point(12, 128), new[] { new ArcSegment(tip, new Size(118, 118), 0, false, SweepDirection.Clockwise, true) }, false)
        });
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetRate(out double rate))
        {
            CustomSpeed.Focus();
            return;
        }
        LimitMbps = rate;
        DialogResult = true;
    }

}
