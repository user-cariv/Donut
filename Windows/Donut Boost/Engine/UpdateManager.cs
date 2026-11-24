using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace DonutBoost.Engine;

public class UpdateManifest
{
    [JsonPropertyName("latestVersion")]
    public string LatestVersion { get; set; } = "";

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
    private const string ReleaseBaseUrl = "https://user-cariv.github.io/Donut";
    private const long MaxInstallerBytes = 128L * 1024 * 1024;
    private static readonly HttpClient _httpClient = new(new SocketsHttpHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(30),
        MaxResponseContentBufferSize = 64 * 1024
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

    private const string ManifestUrl = ReleaseBaseUrl + "/boost-version.json";

    public static async Task<(bool HasUpdate, UpdateManifest? Manifest)> CheckForUpdatesAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(6));
            using var response = await _httpClient.GetAsync(
                $"{ManifestUrl}?t={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}", timeout.Token);
            response.EnsureSuccessStatusCode();
            string json = await response.Content.ReadAsStringAsync(timeout.Token);
            var manifest = JsonSerializer.Deserialize<UpdateManifest>(json);
            if (manifest != null && manifest.LatestVersion is { } latest &&
                Version.TryParse(NormalizeVersion(latest), out _) &&
                IsValidSha256(manifest.InstallerSha256) &&
                IsTrustedInstallerUrl(manifest.DownloadUrl, latest))
            {
                return (IsVersionNewer(latest, CurrentVersion), manifest);
            }
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested &&
            ex is HttpRequestException or OperationCanceledException or JsonException) { }
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

    private static bool IsTrustedInstallerUrl(string? value, string? version)
    {
        if (string.IsNullOrWhiteSpace(version) || !Version.TryParse(NormalizeVersion(version), out var parsed))
            return false;
        string versioned = $"{ReleaseBaseUrl}/updates/{parsed}/Install%20Donut%20Boost.exe";
        return value == versioned || value == ReleaseBaseUrl + "/Install%20Donut%20Boost.exe";
    }

    public static async Task<bool> DownloadAndInstallUpdateAsync(
        UpdateManifest manifest, 
        Action<int, long, long> onProgress,
        Action<string> onStatus,
        CancellationToken cancellationToken = default)
    {
        var downloadCandidates = new[] { manifest.DownloadUrl, manifest.FallbackUrl }
            .Where(url => IsTrustedInstallerUrl(url, manifest.LatestVersion))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (!IsValidSha256(manifest.InstallerSha256)) return false;

        string tempFolder = Path.Combine(Path.GetTempPath(), "DonutBoost_Update", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempFolder);
        string installerPath = Path.Combine(tempFolder, "Install Donut Boost.exe");
        bool installerInUse = false;
        try
        {
            bool downloaded = false;
            Exception? lastTransportError = null;
            foreach (var url in downloadCandidates)
            {
                const int maxAttempts = 3;
                for (int attempt = 1; attempt <= maxAttempts; attempt++)
                {
                    try
                    {
                        await DownloadVerifiedInstallerAsync(_httpClient, url, installerPath, manifest.InstallerSha256,
                            onProgress, onStatus, cancellationToken);
                        downloaded = true;
                        break;
                    }
                    catch (Exception ex) when (!cancellationToken.IsCancellationRequested && IsRetryableDownloadError(ex))
                    {
                        lastTransportError = ex;
                        if (attempt == maxAttempts) break;
                        onStatus($"The update connection was interrupted. Retrying ({attempt + 1}/{maxAttempts})…");
                        await Task.Delay(TimeSpan.FromSeconds(attempt), cancellationToken);
                        onProgress(0, 0, 0);
                    }
                    catch (HttpRequestException) when (url != downloadCandidates.Last() && !cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }
                }
                if (downloaded) break;
            }
            if (!downloaded && lastTransportError != null)
                throw new InvalidOperationException("The update connection kept dropping. Try Again or download the installer from the Donut website.", lastTransportError);
            if (!downloaded) return false;

            onStatus("Opening installer. Approve the Windows prompt if it appears.");
            using var installer = await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Process.Start(new ProcessStartInfo
                {
                    FileName = installerPath,
                    Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /CLOSEAPPLICATIONS /NORESTART",
                    UseShellExecute = true
                });
            }, cancellationToken);
            if (installer == null) return false;
            // Let Inno Setup request elevation and close the running app after
            // approval. Its unelevated launcher can reopen Donut for the same user.
            installerInUse = true;
            await installer.WaitForExitAsync(cancellationToken);
            installerInUse = false;
            if (installer.ExitCode != 0)
                throw new InvalidOperationException($"Windows Setup did not complete (exit code {installer.ExitCode}). Select Try Again.");
            return true;
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            throw new InvalidOperationException("The Windows approval prompt was canceled. Select Try Again to install the update.", ex);
        }
        finally
        {
            if (!installerInUse)
            {
                try { File.Delete(installerPath); Directory.Delete(tempFolder); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    private static bool IsRetryableDownloadError(Exception error) =>
        error is TimeoutException or EndOfStreamException ||
        error is HttpRequestException request &&
        (request.StatusCode is null || (int)request.StatusCode.Value is 408 or 429 or >= 500) ||
        (error is IOException && error is not InvalidDataException);

    private static async Task DownloadVerifiedInstallerAsync(HttpClient client, string url, string installerPath,
        string expectedHash, Action<int, long, long> onProgress, Action<string> onStatus, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            response.EnsureSuccessStatusCode();
            long totalBytes = response.Content.Headers.ContentLength ?? 0;
            if (totalBytes > MaxInstallerBytes)
                throw new InvalidDataException("The installer is larger than expected.");
            using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long totalRead = 0;
            int lastPercent = -1;
            byte[] buffer = new byte[64 * 1024];
            await using (var file = new FileStream(installerPath, FileMode.Create, FileAccess.Write, FileShare.None,
                buffer.Length, useAsync: true))
            {
                while (true)
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(30));
                    int bytesRead = await stream.ReadAsync(buffer.AsMemory(), timeout.Token);
                    if (bytesRead == 0) break;
                    if (totalRead + bytesRead > MaxInstallerBytes)
                        throw new InvalidDataException("The installer is larger than expected.");
                    await file.WriteAsync(buffer.AsMemory(0, bytesRead), timeout.Token);
                    hash.AppendData(buffer, 0, bytesRead);
                    totalRead += bytesRead;
                    int percent = totalBytes > 0 ? Math.Min(99, (int)(100.0 * totalRead / totalBytes)) : 0;
                    if (percent == lastPercent) continue;
                    lastPercent = percent;
                    onProgress(percent, totalRead, totalBytes);
                }
            }
            if (totalRead == 0 || (totalBytes > 0 && totalRead < totalBytes))
                throw new EndOfStreamException("The connection closed before the installer was fully downloaded.");
            onStatus("Verifying installer...");
            if ((totalBytes > 0 && totalRead != totalBytes) ||
                !string.Equals(Convert.ToHexString(hash.GetHashAndReset()), expectedHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The installer could not be verified. Select Try Again to check for a fresh update.");
        }
        catch (OperationCanceledException ex) when (!token.IsCancellationRequested)
        {
            throw new TimeoutException("The update download stopped responding. Select Try Again to retry.", ex);
        }
    }
}
