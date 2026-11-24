using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DonutBoost.Models;

namespace DonutBoost;

public partial class MainWindow
{
    private static readonly string NetMergeDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "net-merge");
    private static readonly string NetMergeCli = Path.Combine(NetMergeDirectory, "net-merge.exe");
    private readonly DispatcherTimer _systemMergeTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    private bool _readingMergeStatus;
    private bool _changingMerge;

    private void StartSystemMergeMonitor()
    {
        _systemMergeTimer.Tick += async (_, _) => await RefreshSystemMergeAsync();
        _systemMergeTimer.Start();
        _ = RefreshSystemMergeAsync();
    }

    private async Task<string> ReadSystemMergeAsync()
    {
        if (!File.Exists(NetMergeCli)) return "Install Net Merge";
        string status = await RunNetMergeAsync("status");
        if (status.StartsWith("net-merge is OFF.", StringComparison.OrdinalIgnoreCase)) return "System merge off";
        if (status.StartsWith("net-merge is ON (", StringComparison.OrdinalIgnoreCase))
        {
            string mode = status["net-merge is ON (".Length..].Split(',')[0].Trim();
            if (mode.Equals("tun", StringComparison.OrdinalIgnoreCase) ||
                mode.Equals("system-wide", StringComparison.OrdinalIgnoreCase))
                return "System merge on";
            if (mode.Equals("socks", StringComparison.OrdinalIgnoreCase))
                return "Net Merge proxy on";
        }
        return "Merge status unavailable";
    }

    private async Task RefreshSystemMergeAsync()
    {
        if (_readingMergeStatus || _changingMerge) return;
        _readingMergeStatus = true;
        try
        {
            string state = await ReadSystemMergeAsync();
            if (!_windowClosed && !_changingMerge) SystemMergeButton.Content = state;
        }
        catch
        {
            if (!_windowClosed && !_changingMerge) SystemMergeButton.Content = "Merge status unavailable";
        }
        finally { _readingMergeStatus = false; }
    }

    private async void SystemMerge_Click(object sender, RoutedEventArgs e)
    {
        if (_changingMerge) return;
        _changingMerge = true;
        SystemMergeButton.IsEnabled = false;
        SystemMergeButton.Content = "Checking merge…";
        try
        {
            string state = await ReadSystemMergeAsync();
            if (state == "Install Net Merge")
            {
                Process.Start(new ProcessStartInfo("https://github.com/net-merge/net-merge/releases/latest") { UseShellExecute = true });
            }
            else if (state == "System merge on")
            {
                await RunNetMergeAsync("off");
            }
            else if (state == "System merge off")
            {
                string result = await RunNetMergeAsync("on", allowFailure: true);
                if ((await ReadSystemMergeAsync()) != "System merge on")
                {
                    if (!result.Contains("net-merge is not running;", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException(result);
                    using var mergeProcess = Process.Start(new ProcessStartInfo(NetMergeCli, "run")
                    {
                        UseShellExecute = true,
                        Verb = "runas",
                        WindowStyle = ProcessWindowStyle.Minimized,
                        WorkingDirectory = NetMergeDirectory
                    });
                }
            }
            else
            {
                MessageBox.Show(this, "Check Net Merge in the Windows notification area. If its proxy is on, turn it off before starting system merge.",
                    "Donut Boost", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            if (!_windowClosed)
                MessageBox.Show(this, ex.Message, "Net Merge", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _changingMerge = false;
            SystemMergeButton.IsEnabled = true;
            if (!_windowClosed) await RefreshSystemMergeAsync();
        }
    }

    private static async Task<string> RunNetMergeAsync(string command, bool allowFailure = false)
    {
        using var process = Process.Start(new ProcessStartInfo(NetMergeCli, command)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = NetMergeDirectory
        }) ?? throw new InvalidOperationException("Net Merge could not start.");
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill();
            throw new TimeoutException("Net Merge did not respond.");
        }
        string response = $"{await output}\n{await error}".Trim();
        if (process.ExitCode != 0 && !allowFailure)
            throw new InvalidOperationException(response.Length == 0 ? "Net Merge returned an error." : response);
        return response;
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
