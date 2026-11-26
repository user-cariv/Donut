using System.Windows;
using System.Windows.Controls;
using DonutBoost.Engine;

namespace DonutBoost;

public partial class MainWindow
{
    private bool ConfigureUsenetServer()
    {
        var current = _downloader.UsenetSettings;
        var host = new TextBox { Text = current?.Host ?? string.Empty };
        var port = new TextBox { Text = (current?.Port ?? 563).ToString() };
        var user = new TextBox { Text = current?.User ?? string.Empty };
        var password = new PasswordBox { Password = current?.Password ?? string.Empty };
        var connections = new TextBox { Text = (current?.Connections ?? 4).ToString() };
        var panel = new StackPanel { Margin = new Thickness(16) };
        foreach (var (label, control) in new (string, Control)[]
        {
            ("Provider hostname", host), ("TLS port", port), ("Username", user),
            ("Password", password), ("Maximum connections", connections)
        })
        {
            panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 8, 0, 2) });
            panel.Children.Add(control);
        }
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap };
        panel.Children.Add(error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var save = new Button { Content = "Use provider", IsDefault = true, Margin = new Thickness(0, 10, 8, 0), Padding = new Thickness(8, 4, 8, 4) };
        buttons.Children.Add(save);
        buttons.Children.Add(new Button { Content = "Cancel", IsCancel = true, Margin = new Thickness(0, 10, 0, 0), Padding = new Thickness(8, 4, 8, 4) });
        panel.Children.Add(buttons);
        var dialog = new Window
        {
            Title = "Usenet provider", Owner = this, Content = panel, Width = 380,
            ResizeMode = ResizeMode.NoResize, SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false
        };
        save.Click += (_, _) =>
        {
            string serverHost = host.Text.Trim();
            if (Uri.CheckHostName(serverHost) == UriHostNameType.Unknown ||
                !int.TryParse(port.Text, out int tlsPort) || tlsPort is < 1 or > 65535 ||
                !int.TryParse(connections.Text, out int count) || count is < 1 or > 16)
            {
                error.Text = "Enter a provider hostname, its TLS port, and a connection limit from 1 to 16.";
                return;
            }
            _downloader.UsenetSettings = new UsenetServer(serverHost, tlsPort, user.Text.Trim(), password.Password, count);
            dialog.DialogResult = true;
        };
        return dialog.ShowDialog() == true;
    }
}
