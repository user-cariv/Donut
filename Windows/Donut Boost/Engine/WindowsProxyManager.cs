using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;

namespace DonutBoost.Engine;

/// <summary>
/// Manages Windows internet proxy settings, ensuring the Windows system proxy
/// is always clean and never breaks Google Chrome, Edge, or game launcher connections.
/// </summary>
public static class WindowsProxyManager
{
    [DllImport("wininet.dll", SetLastError = true)]
    private static extern bool InternetSetOption(IntPtr hInternet, int dwOption, IntPtr lpBuffer, int dwBufferLength);

    private const int INTERNET_OPTION_SETTINGS_CHANGED = 39;
    private const int INTERNET_OPTION_REFRESH = 37;
    private const string InternetSettingsKey = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";

    private static bool _isHooked = false;
    private static Timer? _watchdogTimer;
    public static bool IsProxyActive => false;

    public static void EnsureCleanSystemProxy()
    {
        DisableProxy();
        StartWatchdog();
    }

    static WindowsProxyManager()
    {
        EnsureExitHook();
        StartWatchdog();
    }

    private static void StartWatchdog()
    {
        if (_watchdogTimer == null)
        {
            // Continuously verify every 3 seconds that Windows proxy is NEVER enabled
            _watchdogTimer = new Timer(_ => DisableProxy(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3));
        }
    }

    private static void EnsureExitHook()
    {
        if (!_isHooked)
        {
            _isHooked = true;
            AppDomain.CurrentDomain.ProcessExit += (_, _) => DisableProxy();
        }
    }

    /// <summary>
    /// Intentionally a no-op: Donut Boost does NOT hijack the Windows system proxy,
    /// preventing any socket errors, dropped Wi-Fi, or browser interference.
    /// </summary>
    public static bool EnableProxy(string host = "127.0.0.1", int port = 9090)
    {
        DisableProxy();
        return true;
    }

    public static bool DisableProxy()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(InternetSettingsKey, writable: true);
            if (key != null)
            {
                object? curVal = key.GetValue("ProxyEnable");
                bool isSet = curVal is int v && v == 1;
                string? s = key.GetValue("ProxyServer") as string;
                bool hasServer = !string.IsNullOrEmpty(s);

                if (isSet || hasServer)
                {
                    key.SetValue("ProxyEnable", 0, RegistryValueKind.DWord);
                    key.SetValue("ProxyServer", "", RegistryValueKind.String);
                    NotifySystem();
                }
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void NotifySystem()
    {
        try
        {
            InternetSetOption(IntPtr.Zero, INTERNET_OPTION_SETTINGS_CHANGED, IntPtr.Zero, 0);
            InternetSetOption(IntPtr.Zero, INTERNET_OPTION_REFRESH, IntPtr.Zero, 0);
        }
        catch { }
    }
}
