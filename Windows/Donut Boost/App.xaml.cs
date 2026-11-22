using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using DonutBoost.Engine;

namespace DonutBoost;

public partial class App : Application
{
    private static Mutex? _singleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstanceMutex = new Mutex(true, "DonutBoost_SingleInstance_AppMutex", out bool isNewInstance);
        if (!isNewInstance)
        {
            // Focus an already visible instance. A previous crash can leave an
            // elevated, windowless process holding the mutex; don't let that
            // orphan prevent this launch from opening a usable window.
            if (ActivateVisibleInstance())
            {
                Shutdown();
                return;
            }
        }

        base.OnStartup(e);

        // Immediately clean up any stale proxy from an earlier crash or unexpected termination
        WindowsProxyManager.EnsureCleanSystemProxy();

        AppDomain.CurrentDomain.UnhandledException += (_, _) =>
        {
            WindowsProxyManager.DisableProxy();
        };

        DispatcherUnhandledException += (_, _) =>
        {
            WindowsProxyManager.DisableProxy();
        };
    }

    protected override void OnExit(ExitEventArgs e)
    {
        WindowsProxyManager.DisableProxy();
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
            catch
            {
                // Ignore processes that exit or cannot be inspected.
            }
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
