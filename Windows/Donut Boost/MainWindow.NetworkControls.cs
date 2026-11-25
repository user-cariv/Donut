using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DonutBoost.Models;

namespace DonutBoost;

public partial class MainWindow
{
    private EventWaitHandle? _stopSystemMerge;

    private async void StartSystemMergeOnFirstRender(object? sender, EventArgs e)
    {
        ContentRendered -= StartSystemMergeOnFirstRender;
        if (_windowClosed) return;

        try
        {
            await RunSystemMergeAsync();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            if (!_windowClosed) OnLogReceived("Windows administrator approval was declined.");
        }
        catch (Exception ex)
        {
            if (!_windowClosed) OnLogReceived($"System merge: {ex.Message}");
        }
    }

    private async Task RunSystemMergeAsync()
    {
        string session = Guid.NewGuid().ToString("N");
        using var ready = new EventWaitHandle(false, EventResetMode.ManualReset, $@"Local\DonutBoostMerge-{session}-Ready");
        using var stop = new EventWaitHandle(false, EventResetMode.ManualReset, $@"Local\DonutBoostMerge-{session}-Stop");
        using var report = MemoryMappedFile.CreateNew($@"Local\DonutBoostMerge-{session}-Report", 4096, MemoryMappedFileAccess.ReadWrite);
        using var view = report.CreateViewAccessor(0, 4096, MemoryMappedFileAccess.Read);
        int lastReport = 0;
        string? latestReport = null;
        void ReadWorkerReport()
        {
            int sequence = view.ReadInt32(0);
            int length = view.ReadInt32(4);
            if (sequence == lastReport || (sequence & 1) != 0 || length is <= 0 or > 4088) return;
            var bytes = new byte[length];
            view.ReadArray(8, bytes, 0, length);
            if (view.ReadInt32(0) != sequence) return;
            lastReport = sequence;
            latestReport = Encoding.UTF8.GetString(bytes);
            if (!_windowClosed) OnLogReceived($"[Merge] {latestReport}");
        }

        var reportTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        reportTimer.Tick += (_, _) => ReadWorkerReport();
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
            reportTimer.Start();
            Task exited = worker.WaitForExitAsync();
            Task<bool> startup = Task.Run(() => WaitHandle.WaitAny(new[] { ready, stop }, TimeSpan.FromSeconds(45)) == 0);
            await Task.WhenAny(exited, startup);
            if (exited.IsCompleted) stop.Set();
            bool started = await startup;
            if (started && !worker.HasExited && !_windowClosed)
            {
                await exited;
            }
            else
            {
                stop.Set();
                await exited;
                ReadWorkerReport();
                if (!_windowClosed)
                    throw new InvalidOperationException(latestReport ?? "System merge could not start. Check your network adapters and reopen Donut Boost.");
            }
            ReadWorkerReport();
            if (worker.ExitCode != 0 && !_windowClosed)
                throw new InvalidOperationException("System merge stopped unexpectedly.");
        }
        finally
        {
            reportTimer.Stop();
            stop.Set();
            _stopSystemMerge = null;
        }
    }

    private void EditAdapterLimits_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: NetworkAdapterInfo adapter }) return;
        if (adapter.IsWifi || adapter.IsMobilePhone)
        {
            bool isMobile = adapter.IsMobilePhone;
            double currentLimit = isMobile ? adapter.DownloadLimitMbps : _downloader.Throttler.CurrentLimitMbps;
            if (!isMobile && adapter.DownloadLimitMbps > 0)
                currentLimit = currentLimit == 0 ? adapter.DownloadLimitMbps : Math.Min(currentLimit, adapter.DownloadLimitMbps);
            var speedDialog = new WifiLimitDialog(currentLimit, isMobile) { Owner = this };
            if (speedDialog.ShowDialog() == true)
            {
                double adapterLimit = isMobile ? speedDialog.LimitMbps : 0;
                adapter.SetDownloadLimitMbps(adapterLimit);
                if (adapterLimit == 0) _savedAdapterLimits.Remove(adapter.Id);
                else _savedAdapterLimits[adapter.Id] = adapterLimit;
                if (!isMobile) SetWifiLimit(speedDialog.LimitMbps);
                SaveSpeedLimits();
            }
            if (!speedDialog.EditDataBudgets) return;
        }

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
        var speed = adapter.IsWifi || adapter.IsMobilePhone ? null : AddField("Download speed (Mbps)", adapter.DownloadLimitMbps.ToString("0.##"));
        var transfer = AddField("Data per transfer (MB)", adapter.DownloadDataLimitMb.ToString());
        var daily = AddField("Data per day (MB)", adapter.DailyDownloadLimitMb.ToString());
        var save = new Button { Content = "Save limits", IsDefault = true, Style = (Style)FindResource("SecondaryButtonStyle") };
        panel.Children.Add(save);
        save.Click += (_, _) =>
        {
            double mbps = adapter.DownloadLimitMbps;
            if (speed != null && (!double.TryParse(speed.Text.Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.CurrentCulture, out mbps) ||
                !IsValidNetworkSpeedLimit(mbps)))
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
        dialog.Loaded += (_, _) => (speed ?? transfer).Focus();
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
