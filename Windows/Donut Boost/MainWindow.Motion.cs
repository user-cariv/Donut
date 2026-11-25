using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using DonutBoost.Models;

namespace DonutBoost;

public partial class MainWindow
{
    private readonly ObservableCollection<NetworkAdapterInfo> _previewAdapters = new();
    private ButtonBase? _hoverControl;
    private ButtonBase? _pressedControl;

    private void InitializePreviewMotion()
    {
        AdaptersItemsControl.ItemsSource = _previewAdapters;
        AddHandler(Mouse.PreviewMouseMoveEvent, new MouseEventHandler(PreviewPointerMoved), true);
        AddHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler(PreviewPointerPressed), true);
        AddHandler(Mouse.PreviewMouseUpEvent, new MouseButtonEventHandler(PreviewPointerReleased), true);
        MouseLeave += (_, _) => ResetPreviewPointer();
        Deactivated += (_, _) => ResetPreviewPointer();
        Loaded += StartWallpaperMotion;
    }

    private readonly System.Diagnostics.Stopwatch _wallpaperElapsed = new();
    private bool _previewRendering;

    private void StartWallpaperMotion(object sender, RoutedEventArgs e)
    {
        Loaded -= StartWallpaperMotion;
        StateChanged += (_, _) => RefreshPreviewRendering();
        IsVisibleChanged += (_, _) => RefreshPreviewRendering();
        Closed += (_, _) => SetPreviewRendering(false);
        RefreshPreviewRendering();
    }

    private void RefreshPreviewRendering() =>
        SetPreviewRendering(IsVisible && WindowState != WindowState.Minimized && SystemParameters.ClientAreaAnimation);

    private void SetPreviewRendering(bool active)
    {
        if (active == _previewRendering) return;
        _previewRendering = active;
        if (active)
        {
            _wallpaperElapsed.Start();
            CompositionTarget.Rendering += RenderPreviewFrame;
        }
        else
        {
            CompositionTarget.Rendering -= RenderPreviewFrame;
            _wallpaperElapsed.Stop();
        }
    }

    private void RenderPreviewFrame(object? sender, EventArgs e)
    {
        var frame = _wallpaperElapsed.Elapsed;
        AdvancePreviewGauge(frame);
        AdvanceWallpaper(frame);
        foreach (var adapter in _previewAdapters)
            adapter.RenderPreviewSample();
    }

    private void AdvanceWallpaper(TimeSpan frame)
    {
        double phase = frame.TotalSeconds * Math.Tau / 24;
        double direction = Math.Sin(phase);
        double width = WallpaperViewport.ActualWidth;
        double height = WallpaperViewport.ActualHeight;
        WallpaperTravel.X = direction * width * 0.39;
        WallpaperTravel.Y = Math.Sin(phase + Math.PI / 5) * height * 0.36;

        // Separate near the turns and merge as the travel path crosses the center.
        double split = Math.Pow(direction, 4);
        double inward = -direction * split;
        WallpaperUpperTravel.X = inward * Math.Min(width * 0.24, 310);
        WallpaperUpperTravel.Y = direction * split * Math.Min(height * 0.15, 120);
        WallpaperLowerTravel.X = inward * Math.Min(width * 0.08, 105);
        WallpaperLowerTravel.Y = inward * Math.Min(height * 0.40, 290);
        WallpaperGlow.Opacity = 1 - split * 0.34;
        WallpaperUpperGlow.Opacity = split * 0.60;
        WallpaperLowerGlow.Opacity = split * 0.54;
    }
    private void SyncPreviewAdapters(IReadOnlyList<NetworkAdapterInfo> adapters)
    {
        for (int index = _previewAdapters.Count - 1; index >= 0; index--)
            if (!adapters.Contains(_previewAdapters[index]))
                _previewAdapters.RemoveAt(index);

        for (int index = 0; index < adapters.Count; index++)
        {
            int previous = _previewAdapters.IndexOf(adapters[index]);
            if (previous < 0)
                _previewAdapters.Insert(index, adapters[index]);
            else if (previous != index)
                _previewAdapters.Move(previous, index);
        }
    }

    private void PreviewPointerMoved(object sender, MouseEventArgs e)
    {
        var control = FindPreviewButton(e.OriginalSource);
        if (_hoverControl == control) return;
        if (_hoverControl != _pressedControl) ScalePreviewControl(_hoverControl, 1);
        _hoverControl = control;
        if (_hoverControl != _pressedControl) ScalePreviewControl(_hoverControl, 1.025);
    }

    private void PreviewPointerPressed(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        _pressedControl = FindPreviewButton(e.OriginalSource);
        ScalePreviewControl(_pressedControl, 0.96);
    }

    private void PreviewPointerReleased(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        ScalePreviewControl(_pressedControl, _pressedControl == _hoverControl ? 1.025 : 1);
        _pressedControl = null;
    }

    private void ResetPreviewPointer()
    {
        ScalePreviewControl(_hoverControl, 1);
        ScalePreviewControl(_pressedControl, 1);
        _hoverControl = _pressedControl = null;
    }

    private static ButtonBase? FindPreviewButton(object source)
    {
        for (var visual = source as Visual; visual != null; visual = VisualTreeHelper.GetParent(visual) as Visual)
            if (visual is ButtonBase button)
                return button.IsEnabled && button is not RepeatButton ? button : null;
        return null;
    }

    private static void ScalePreviewControl(ButtonBase? control, double scale)
    {
        if (!SystemParameters.ClientAreaAnimation || control is null) return;
        if (control.RenderTransform is not ScaleTransform transform)
        {
            if (!control.RenderTransform.Value.IsIdentity) return;
            transform = new ScaleTransform(1, 1);
            control.RenderTransformOrigin = new Point(0.5, 0.5);
            control.RenderTransform = transform;
        }
        transform.BeginAnimation(ScaleTransform.ScaleXProperty, PreviewEase(scale, 160));
        transform.BeginAnimation(ScaleTransform.ScaleYProperty, PreviewEase(scale, 160));
    }

    private static void RevealPreview(FrameworkElement view)
    {
        if (!SystemParameters.ClientAreaAnimation) return;
        var fade = PreviewEase(1, 200);
        fade.From = 0.65;
        view.BeginAnimation(OpacityProperty, fade);
    }

    private double _previewModeTarget = -1;

    private void PreviewModeTrack_SizeChanged(object sender, SizeChangedEventArgs e) =>
        MovePreviewModePill(animate: false);

    private void MovePreviewModePill(bool animate = true)
    {
        var transform = (TranslateTransform)PreviewModePill.RenderTransform;
        int segment = PreviewMobile.IsChecked == true ? 2 : PreviewWifi.IsChecked == true ? 1 : 0;
        double target = PreviewModeTrack.ActualWidth * segment / 3;
        if (target == _previewModeTarget) return;
        _previewModeTarget = target;
        if (animate && SystemParameters.ClientAreaAnimation)
        {
            transform.BeginAnimation(TranslateTransform.XProperty, PreviewEase(target, 210));
            return;
        }
        transform.BeginAnimation(TranslateTransform.XProperty, null);
        transform.X = target;
    }

    private void ShowRealUpdateBanner()
    {
        if (AppUpdateBanner.Visibility == Visibility.Visible) return;
        AppUpdateBanner.Visibility = Visibility.Visible;
        RevealPreview(AppUpdateBanner);
    }

    private static DoubleAnimation PreviewEase(double value, int milliseconds) => new()
    {
        To = value,
        Duration = TimeSpan.FromMilliseconds(milliseconds),
        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
    };
}
