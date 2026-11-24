using System.Drawing;
using System.IO;
using Forms = System.Windows.Forms;

namespace DonutBoost;

public partial class MainWindow
{
    private Forms.NotifyIcon? _notificationIcon;
    private Forms.ContextMenuStrip? _notificationMenu;
    private Icon? _notificationGlyph;

    private void HideToNotificationArea()
    {
        try
        {
            if (_notificationIcon is null)
            {
                _notificationGlyph = System.Drawing.Icon.ExtractAssociatedIcon(
                    Path.Combine(AppContext.BaseDirectory, "DonutBoost.exe")) ?? (Icon)SystemIcons.Application.Clone();
                _notificationMenu = new Forms.ContextMenuStrip();
                _notificationMenu.Items.Add("Open Donut Boost", null, (_, _) => RestoreFromBackground());
                _notificationMenu.Items.Add("Exit Donut Boost", null, (_, _) => Close());
                _notificationIcon = new Forms.NotifyIcon
                {
                    Icon = _notificationGlyph,
                    Text = "Donut Boost is running in the background",
                    ContextMenuStrip = _notificationMenu,
                    Visible = true
                };
                _notificationIcon.MouseDoubleClick += (_, args) =>
                {
                    if (args.Button == Forms.MouseButtons.Left) RestoreFromBackground();
                };
            }

            Hide();
        }
        catch (Exception ex)
        {
            DisposeNotificationArea();
            OnLogReceived($"Could not open the notification area: {ex.Message}");
        }
    }

    internal void RestoreFromBackground()
    {
        if (_windowClosed) return;
        if (!IsVisible) Show();
        if (WindowState == System.Windows.WindowState.Minimized)
            WindowState = System.Windows.WindowState.Normal;
        Activate();
    }

    private void DisposeNotificationArea()
    {
        _notificationIcon?.Dispose();
        _notificationIcon = null;
        _notificationMenu?.Dispose();
        _notificationMenu = null;
        _notificationGlyph?.Dispose();
        _notificationGlyph = null;
    }
}
