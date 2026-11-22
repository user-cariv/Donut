namespace DonutBoost.Engine;

/// <summary>
/// Donut Boost uses adapter-bound sockets for its own downloads and must never
/// alter the user's Windows proxy configuration.
/// </summary>
public static class WindowsProxyManager
{
    public static bool IsProxyActive => false;

    // Kept as compatibility shims for existing UI/cleanup call sites.
    // They intentionally leave Windows proxy settings untouched.
    public static void EnsureCleanSystemProxy() { }
    public static bool EnableProxy(string host = "127.0.0.1", int port = 9090) => false;
    public static bool DisableProxy() => true;
}
