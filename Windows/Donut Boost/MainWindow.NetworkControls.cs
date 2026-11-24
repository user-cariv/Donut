using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using DonutBoost.Models;

namespace DonutBoost;

public partial class MainWindow
{
    private EventWaitHandle? _stopSystemMerge;

    private async void SystemMerge_Click(object sender, RoutedEventArgs e)
    {
        if (_stopSystemMerge is { } current)
        {
            _systemMergeActive = false;
            SystemMergeButton.Content = "Stopping merge…";
            SystemMergeButton.IsEnabled = false;
            _speedTestCancellation?.Cancel();
            current.Set();
            return;
        }

        SystemMergeButton.Content = "Starting merge…";
        SystemMergeButton.IsEnabled = false;
        string session = Guid.NewGuid().ToString("N");
        using var ready = new EventWaitHandle(false, EventResetMode.ManualReset, $@"Local\DonutBoostMerge-{session}-Ready");
        using var stop = new EventWaitHandle(false, EventResetMode.ManualReset, $@"Local\DonutBoostMerge-{session}-Stop");
        _stopSystemMerge = stop;
        try
        {
            string engineFolder = Path.Combine(AppContext.BaseDirectory, "system-merge");
            string engine = Path.Combine(engineFolder, "DonutBoost.SystemMerge.exe");
            if (!File.Exists(engine) || !File.Exists(Path.Combine(engineFolder, "wintun.dll")))
                throw new InvalidOperationException("The System merge engine is missing. Install Donut Boost using the Windows installer.");

            var launch = new ProcessStartInfo(engine)
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = engineFolder,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            launch.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
            launch.ArgumentList.Add(session);
            using var worker = Process.Start(launch) ?? throw new InvalidOperationException("The System merge engine could not start.");
            Task exited = worker.WaitForExitAsync();
            Task<bool> startup = Task.Run(() => WaitHandle.WaitAny(new[] { ready, stop }, TimeSpan.FromSeconds(20)) == 0);
            await Task.WhenAny(exited, startup);
            if (exited.IsCompleted) stop.Set();
            bool started = await startup;
            if (started && !worker.HasExited && !_windowClosed)
            {
                _systemMergeActive = true;
                LauncherRoutingHint.Text = "System merge is on. Start or resume a launcher download to open new connections across available links.";
                SystemMergeButton.Content = "System merge on";
                SystemMergeButton.IsEnabled = true;
                await exited;
            }
            else
            {
                stop.Set();
                await exited;
                if (!_windowClosed)
                    throw new InvalidOperationException("System merge could not start. Check that your network adapters are connected and try again.");
            }
            if (worker.ExitCode != 0 && !_windowClosed)
                throw new InvalidOperationException("System merge stopped unexpectedly.");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // The user declined Windows elevation.
        }
        catch (Exception ex)
        {
            if (!_windowClosed)
                MessageBox.Show(this, ex.Message, "Donut Boost", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            stop.Set();
            _stopSystemMerge = null;
            _systemMergeActive = false;
            _speedTestCancellation?.Cancel();
            if (!_windowClosed)
            {
                LauncherRoutingHint.Text = "Monitors supported launchers. Turn on System merge to route new launcher connections across available links.";
                SystemMergeButton.Content = "System merge off";
                SystemMergeButton.IsEnabled = true;
            }
        }
    }

    private void EditAdapterLimits_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: NetworkAdapterInfo adapter }) return;

        var panel = new StackPanel { Margin = new Thickness(20) };
        var dialog = new Window
        {
            Owner = this, Title = $"{adapter.Name} limits", Width = 360,
            SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false,
            Background = Background, Foreground = Foreground, Content = panel
        };
        TextBox AddField(string label, string value)
        {
            var input = new TextBox { Text = value, Padding = new Thickness(6), Margin = new Thickness(0, 0, 0, 10) };
            panel.Children.Add(new Label { Content = label, Target = input, Foreground = dialog.Foreground });
            panel.Children.Add(input);
            return input;
        }

        panel.Children.Add(new TextBlock
        {
            Text = "Applies to Donut downloads. Use 0 for unlimited. Daily budgets reset at local midnight.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8)
        });
        var speed = AddField("Download speed (Mbps)", adapter.DownloadLimitMbps.ToString("0.##"));
        var transfer = AddField("Data per transfer (MB)", adapter.DownloadDataLimitMb.ToString());
        var daily = AddField("Data per day (MB)", adapter.DailyDownloadLimitMb.ToString());
        var save = new Button { Content = "Save limits", IsDefault = true, Style = (Style)FindResource("SecondaryButtonStyle") };
        panel.Children.Add(save);
        save.Click += (_, _) =>
        {
            if (!double.TryParse(speed.Text.Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.CurrentCulture, out double mbps) ||
                !IsValidNetworkSpeedLimit(mbps))
            {
                MessageBox.Show(dialog, "Enter 0 or a speed from 0.1 to 100,000 Mbps.", dialog.Title);
                speed.Focus();
                return;
            }
            if (!ReadBudget(transfer, out long transferMb) || !ReadBudget(daily, out long dailyMb)) return;
            adapter.SetDownloadLimitMbps(mbps);
            adapter.SetDownloadDataLimitMb(transferMb);
            adapter.SetDailyDownloadLimitMb(dailyMb);
            if (mbps == 0) _savedAdapterLimits.Remove(adapter.Id);
            else _savedAdapterLimits[adapter.Id] = mbps;
            if (transferMb == 0) _savedAdapterDataLimits.Remove(adapter.Id);
            else _savedAdapterDataLimits[adapter.Id] = transferMb;
            if (dailyMb == 0) _dailyAdapterLimits.Remove(adapter.Id);
            else _dailyAdapterLimits[adapter.Id] = dailyMb;
            SaveSpeedLimits();
            dialog.DialogResult = true;
        };
        dialog.Loaded += (_, _) => speed.Focus();
        dialog.ShowDialog();

        bool ReadBudget(TextBox input, out long limit)
        {
            if (long.TryParse(input.Text.Trim(), NumberStyles.None, CultureInfo.CurrentCulture, out limit) &&
                limit is >= 0 and <= 10_000_000) return true;
            MessageBox.Show(dialog, "Enter a whole number from 0 to 10,000,000 MB.", dialog.Title);
            input.Focus();
            return false;
        }
    }
}
