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
        double phase = frame.TotalSeconds * Math.Tau / 24;
        WallpaperTravel.X = Math.Sin(phase) * WallpaperViewport.ActualWidth * 0.37;
        WallpaperTravel.Y = Math.Cos(phase) * WallpaperViewport.ActualHeight * 0.30;
        foreach (var adapter in _previewAdapters)
            adapter.RenderPreviewSample();
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
