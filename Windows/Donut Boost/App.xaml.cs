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
            // Another instance is already running; exit gracefully without conflict
            Shutdown();
            return;
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
}
