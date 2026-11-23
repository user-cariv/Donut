using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace DonutBoost.Engine;

public class UpdateManifest
{
    [JsonPropertyName("latestVersion")]
    public string LatestVersion { get; set; } = "1.0.0";

    [JsonPropertyName("releaseDate")]
    public string ReleaseDate { get; set; } = "";

    [JsonPropertyName("releaseNotes")]
    public string ReleaseNotes { get; set; } = "";

    [JsonPropertyName("downloadUrl")]
    public string DownloadUrl { get; set; } = "";

    [JsonPropertyName("fallbackUrl")]
    public string FallbackUrl { get; set; } = "";

    [JsonPropertyName("installerSha256")]
    public string InstallerSha256 { get; set; } = "";
}

/// <summary>
/// Handles in-app seamless auto-updates for Donut Boost.
/// Checks GitHub / Website for new releases, notifies existing users inside the app,
/// and downloads/installs the new version silently or with 1-click.
/// </summary>
public class UpdateManager
{
    private static readonly HttpClient _httpClient = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    static UpdateManager()
    {
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("DonutBoost-AutoUpdater/1.0");
    }

    public static string CurrentVersion
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version;
            return v?.ToString() ?? "1.0.0.0";
        }
    }

    private static readonly string[] ManifestUrls = new[]
    {
        "https://user-cariv.github.io/Donut/boost-version.json",
        "https://raw.githubusercontent.com/user-cariv/Donut/main/Website/boost-version.json"
    };

    public static async Task<(bool HasUpdate, UpdateManifest? Manifest)> CheckForUpdatesAsync()
    {
        foreach (var url in ManifestUrls)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
                // Add timestamp query parameter to bypass HTTP caching
                string cacheBusterUrl = $"{url}?t={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
                var response = await _httpClient.GetAsync(cacheBusterUrl, cts.Token);
                if (response.IsSuccessStatusCode)
                {
                    string json = await response.Content.ReadAsStringAsync(cts.Token);
                    var manifest = JsonSerializer.Deserialize<UpdateManifest>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (manifest != null && !string.IsNullOrWhiteSpace(manifest.LatestVersion))
                    {
                        if (IsVersionNewer(manifest.LatestVersion, CurrentVersion))
                        {
                            // Only offer an update if the build pipeline supplied
                            // the checksum for the installer that it published.
                            if (!IsValidSha256(manifest.InstallerSha256)) continue;
                            return (true, manifest);
                        }
                        return (false, manifest);
                    }
                }
            }
            catch
            {
                // Try fallback URL
            }
        }
        return (false, null);
    }

    public static bool IsVersionNewer(string latestStr, string currentStr)
    {
        if (Version.TryParse(NormalizeVersion(latestStr), out var latest)
            && Version.TryParse(NormalizeVersion(currentStr), out var current))
        {
            return latest > current;
        }
        return false;
    }

    private static string NormalizeVersion(string v)
    {
        v = v.Trim().TrimStart('v', 'V');
        var parts = v.Split('.');
        if (parts.Length == 1) return $"{parts[0]}.0.0";
        if (parts.Length == 2) return $"{parts[0]}.{parts[1]}.0";
        return v;
    }

    private static bool IsValidSha256(string? value) =>
        value != null && value.Length == 64 && value.All(Uri.IsHexDigit);

    public static async Task<bool> DownloadAndInstallUpdateAsync(
        UpdateManifest manifest, 
        Action<int, long, long> onProgress, 
        CancellationToken cancellationToken = default)
    {
        string[] downloadCandidates = new[]
        {
            manifest.DownloadUrl,
            manifest.FallbackUrl,
            "https://user-cariv.github.io/Donut/Install%20Donut%20Boost.exe",
            "https://raw.githubusercontent.com/user-cariv/Donut/main/Website/Install%20Donut%20Boost.exe"
        };

        string tempFolder = Path.Combine(Path.GetTempPath(), "DonutBoost_Update");
        Directory.CreateDirectory(tempFolder);
        string installerPath = Path.Combine(tempFolder, "Install Donut Boost.exe");

        if (!IsValidSha256(manifest.InstallerSha256)) return false;

        bool downloaded = false;

        foreach (var url in downloadCandidates)
        {
            if (string.IsNullOrWhiteSpace(url)) continue;

            try
            {
                if (!Uri.TryCreate(url, UriKind.Absolute, out var downloadUri) || downloadUri.Scheme != Uri.UriSchemeHttps)
                    continue;

                using var response = await _httpClient.GetAsync(downloadUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (!response.IsSuccessStatusCode) continue;

                long totalBytes = response.Content.Headers.ContentLength ?? 0;
                using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var fileStream = new FileStream(installerPath, FileMode.Create, FileAccess.Write, FileShare.None);

                byte[] buffer = new byte[64 * 1024];
                long totalRead = 0;
                int bytesRead;

                while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                    totalRead += bytesRead;

                    if (totalBytes > 0)
                    {
                        int percent = (int)((double)totalRead / totalBytes * 100);
                        onProgress?.Invoke(percent, totalRead, totalBytes);
                    }
                }

                fileStream.Flush();
                fileStream.Close();

                string actualHash;
                await using (var verifyStream = File.OpenRead(installerPath))
                {
                    actualHash = Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(verifyStream, cancellationToken));
                }

                if (!string.Equals(actualHash, manifest.InstallerSha256, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(installerPath);
                    continue;
                }

                downloaded = true;
                break;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // Try next download candidate
            }
        }

        if (!downloaded || !File.Exists(installerPath))
        {
            return false;
        }

        // Launch the update installer
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = installerPath,
                Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /CLOSEAPPLICATIONS /NORESTART",
                UseShellExecute = true,
                Verb = "runas"
            };
            using var installer = Process.Start(psi);
            if (installer == null) return false;

            // Starting with runas succeeds only after UAC is accepted. Exit now so
            // Inno Setup can replace the running executable without a close race.
            Application.Current.Dispatcher.Invoke(() => Application.Current.Shutdown());
            return true;
        }
        catch
        {
            // Keep the running app open if elevation is declined or the installer fails.
            return false;
        }
    }
}
