using System.Windows;
using DonutBoost.Engine;

namespace DonutBoost;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
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
        base.OnExit(e);
    }
}
