using System.Diagnostics;
using System.IO;
using System.Text;

namespace DonutBoost.Engine;

/// <summary>
/// Optional, reversible preference for Windows' current-session default route.
/// Launcher traffic stays under Windows routing; Donut downloads are unaffected.
/// </summary>
public static class WifiRoutePreferenceManager
{
    private static readonly string BackupPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DonutBoost", "network-route-metrics.json");

    public static bool HasSavedMetrics => File.Exists(BackupPath);

    public static async Task<bool> RunAsync(bool restore, int wifiIndex, IReadOnlyCollection<int> mobileIndexes)
    {
        if (!restore && (wifiIndex <= 0 || mobileIndexes.Count == 0 || mobileIndexes.Any(index => index <= 0)))
            throw new ArgumentException("Valid Wi-Fi and tether interface indexes are required.");

        string mode = restore ? "restore" : "prefer";
        string mobileList = string.Join(",", mobileIndexes.Distinct());
        string backupBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(BackupPath));
        string script = $$"""
            $ErrorActionPreference = 'Stop'
            $backup = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{{backupBase64}}'))
            if ('{{mode}}' -eq 'restore') {
              if (-not (Test-Path -LiteralPath $backup)) { exit 2 }
              $saved = Get-Content -LiteralPath $backup -Raw | ConvertFrom-Json
              foreach ($row in $saved) {
                Set-NetIPInterface -InterfaceIndex ([uint32]$row.InterfaceIndex) -AddressFamily $row.AddressFamily -AutomaticMetric $row.AutomaticMetric -InterfaceMetric ([uint32]$row.InterfaceMetric) -PolicyStore ActiveStore -ErrorAction Stop
              }
              Remove-Item -LiteralPath $backup -Force
              exit 0
            }
            $wifi = [uint32]{{wifiIndex}}
            $mobile = @({{mobileList}})
            $indices = @($wifi) + @($mobile | ForEach-Object { [uint32]$_ }) | Select-Object -Unique
            $saved = @(Get-NetIPInterface -PolicyStore ActiveStore | Where-Object { $indices -contains $_.InterfaceIndex } | Select-Object InterfaceIndex,AddressFamily,AutomaticMetric,InterfaceMetric)
            if (-not $saved -or -not ($saved | Where-Object { $_.InterfaceIndex -eq $wifi -and $_.AddressFamily -eq 'IPv4' })) { throw 'Wi-Fi interface settings were not found.' }
            $dir = Split-Path -Parent $backup
            New-Item -ItemType Directory -Path $dir -Force | Out-Null
            if (-not (Test-Path -LiteralPath $backup)) { $saved | ConvertTo-Json | Set-Content -LiteralPath $backup -Encoding UTF8 }
            Set-NetIPInterface -InterfaceIndex $wifi -AddressFamily IPv4 -AutomaticMetric Disabled -InterfaceMetric 5 -PolicyStore ActiveStore -ErrorAction Stop
            Set-NetIPInterface -InterfaceIndex $wifi -AddressFamily IPv6 -AutomaticMetric Disabled -InterfaceMetric 5 -PolicyStore ActiveStore -ErrorAction SilentlyContinue
            foreach ($index in $mobile) {
              Set-NetIPInterface -InterfaceIndex ([uint32]$index) -AddressFamily IPv4 -AutomaticMetric Disabled -InterfaceMetric 500 -PolicyStore ActiveStore -ErrorAction Stop
              Set-NetIPInterface -InterfaceIndex ([uint32]$index) -AddressFamily IPv6 -AutomaticMetric Disabled -InterfaceMetric 500 -PolicyStore ActiveStore -ErrorAction SilentlyContinue
            }
            exit 0
            """;

        string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var start = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell\\v1.0\\powershell.exe"),
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
            Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}"
        };
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start elevated Windows PowerShell.");
        await Task.Run(process.WaitForExit);
        return process.ExitCode == 0;
    }
}
