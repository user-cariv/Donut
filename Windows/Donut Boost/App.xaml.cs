using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace DonutBoost;

public partial class App : Application
{
    private static Mutex? _singleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstanceMutex = new Mutex(true, "DonutBoost_SingleInstance_AppMutex", out bool isNewInstance);
        if (!isNewInstance)
        {
            // Focus an already visible instance. A windowless process left
            // behind by a previous crash must not block a fresh launch.
            if (ActivateVisibleInstance())
            {
                Shutdown();
                return;
            }
        }

        base.OnStartup(e);

        try
        {
            var mainWindow = new MainWindow();
            MainWindow = mainWindow;
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Donut Boost could not finish opening. Please install the latest version from the Donut website.\n\nDetails: {ex.Message}",
                "Donut Boost startup error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    private static bool ActivateVisibleInstance()
    {
        foreach (var process in Process.GetProcessesByName("DonutBoost"))
        {
            if (process.Id == Environment.ProcessId) continue;
            try
            {
                IntPtr window = process.MainWindowHandle;
                if (window == IntPtr.Zero) continue;
                if (IsIconic(window)) ShowWindow(window, 9);
                SetForegroundWindow(window);
                return true;
            }
            catch { }
        }
        return false;
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr window);
}
