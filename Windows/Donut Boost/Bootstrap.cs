namespace DonutBoost;

internal static class Bootstrap
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length > 0 && args[0].StartsWith("chrome-extension://", StringComparison.Ordinal))
            return BrowserCapture.RunHostAsync().GetAwaiter().GetResult();

        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }
}
