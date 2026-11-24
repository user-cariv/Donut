using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace DonutBoost.Engine;

public enum LicenseTier
{
    None = 0,
    OneDay = 1,
    SevenDays = 2,
    OneMonth = 3,
    Lifetime = 4,
    FreeTrial = 5
}

public class LicenseRecord
{
    public string Code { get; set; } = string.Empty;
    public LicenseTier Tier { get; set; } = LicenseTier.None;
    public DateTime ActivatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime LastVerifiedUtc { get; set; }
    public long LastVerifiedTick { get; set; }
    public string HardwareId { get; set; } = string.Empty;
    public string SignedEntitlement { get; set; } = string.Empty;
    // Retained only so existing DPAPI records can be read during migration.
    public string Signature { get; set; } = string.Empty;
}

public class LicenseInfo
{
    public bool IsActive { get; set; }
    public LicenseTier Tier { get; set; } = LicenseTier.None;
    public string TierDisplayName { get; set; } = "No License";
    public DateTime? ActivatedAtUtc { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public string HardwareId { get; set; } = string.Empty;
    public string StatusMessage { get; set; } = string.Empty;
    public bool IsLifetime => Tier == LicenseTier.Lifetime;

    public string BadgeText => Tier switch
    {
        LicenseTier.Lifetime => "⚡ PRO: LIFETIME",
        LicenseTier.OneDay => $"⏳ PRO: {FormatRemainingHours()}",
        LicenseTier.SevenDays => $"⏳ PRO: {FormatRemainingDays()}",
        LicenseTier.OneMonth => $"⏳ PRO: {FormatRemainingDays()}",
        LicenseTier.FreeTrial => $"🎁 TRIAL: {FormatRemainingHours()}",
        _ => "🔒 UNLICENSED"
    };

    private string FormatRemainingDays()
    {
        if (!ExpiresAtUtc.HasValue) return "Active";
        var span = ExpiresAtUtc.Value - DateTime.UtcNow;
        if (span.TotalDays >= 1) return $"{(int)Math.Ceiling(span.TotalDays)}d Left";
        return $"{(int)Math.Max(1, span.TotalHours)}h Left";
    }

    private string FormatRemainingHours()
    {
        if (!ExpiresAtUtc.HasValue) return "Active";
        var span = ExpiresAtUtc.Value - DateTime.UtcNow;
        if (span.TotalHours >= 1) return $"{(int)Math.Ceiling(span.TotalHours)}h Left";
        return $"{(int)Math.Max(1, span.TotalMinutes)}m Left";
    }
}

/// <summary>
/// Verifies server-signed entitlements and binds them to this Windows profile's
/// device identifier. The server remains the authority for issuance and revocation.
/// </summary>
public class LicenseManager
{
    private const string Alphabet = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";
    private readonly string _licenseFilePath;
    private readonly string _hwid;
    private const string LicenseKeyId = "2026-11";
    private const string LicensePublicKey = """
-----BEGIN PUBLIC KEY-----
MIIBojANBgkqhkiG9w0BAQEFAAOCAY8AMIIBigKCAYEAxUKV5jni0ItfTJ72zE6H
2BhJ/N3PRdapP5987I8j0WPsQWXO8RLlI7azc/KztneHk2GjI6m5/EJxLQN34JpE
p+MIO+3XQwlhi8ytCh8mqIB1cAgMneLkRmQKfA0Kq+gUUzpGin4sjBHkM+ZTcU5a
UuigoaKOhWAu6WZv1lgtnDHt+F+VyHZSdGLuXjiWRlxKv1i2EyW/O5iTyqZmPqIC
bgbHrSuHfbD5nu51ypO+pGjr4PkizNOcPI0bqpu32DyjbTJGPqAM7VB5ks8XLUb4
TZ2y0uooB6z9A6e6mTd81YGvoHP8rwnfqdoFYihMi+D+dQRgeDluqJsL29EeyIHz
Zwxg8Qpykn27lqS5FrDEv+s5HkL9x7KoxjT/HdlHGK3eLhe9dI5IVI6phkuAXjFu
7so0BNfGTpnhDezyMt4qlRINuGZ2W3/CbSOwLkcza07YVjh22nxAVWY4636gdERS
tPRpdmdCnoXb0QekBsuClGx5pQzWtpw3lrr1pkQKjJ/VAgMBAAE=
-----END PUBLIC KEY-----
""";
    private static readonly IReadOnlyDictionary<string, string> LicensePublicKeys = new Dictionary<string, string>
    {
        [LicenseKeyId] = LicensePublicKey
    };

    public LicenseInfo CurrentLicense { get; private set; } = new() { IsActive = false };

    public LicenseManager()
    {
        _hwid = GenerateMachineHwid();

        string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DonutBoost");
        Directory.CreateDirectory(appData);
        _licenseFilePath = Path.Combine(appData, "license.dat");
        LoadLicense();
    }

    public string MachineHwid => _hwid;

    /// <summary>
    /// Cryptographically validates the active license and guarantees core engine authorization.
    /// Called directly by MultiChannelDownloader before opening streams.
    /// </summary>
    public bool ValidateActiveSessionToken()
    {
        try
        {
            if (!File.Exists(_licenseFilePath)) return false;
            byte[] encrypted = File.ReadAllBytes(_licenseFilePath);
            byte[] decrypted = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            var record = JsonSerializer.Deserialize<LicenseRecord>(Encoding.UTF8.GetString(decrypted));
            if (record == null || !ValidateEntitlement(record.SignedEntitlement, out var claim)) return false;
            return string.Equals(claim.HardwareId, _hwid, StringComparison.OrdinalIgnoreCase)
                && string.Equals(record.HardwareId, _hwid, StringComparison.OrdinalIgnoreCase)
                && claim.Tier == (int)record.Tier
                && claim.ExpiresAtUtc == record.ExpiresAtUtc
                && claim.ExpiresAtUtc > DateTime.UtcNow;
        }
        catch { return false; }
    }
    public LicenseInfo LoadLicense()
    {
        try
        {
            if (!File.Exists(_licenseFilePath))
            {
                CurrentLicense = new LicenseInfo { IsActive = false, HardwareId = _hwid, StatusMessage = "No license found. Enter a redeem code to unlock." };
                return CurrentLicense;
            }

            byte[] encrypted = File.ReadAllBytes(_licenseFilePath);
            byte[] decrypted = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            string json = Encoding.UTF8.GetString(decrypted);

            var record = JsonSerializer.Deserialize<LicenseRecord>(json);
            if (record == null)
            {
                CurrentLicense = new LicenseInfo { IsActive = false, HardwareId = _hwid, StatusMessage = "Corrupt license data." };
                return CurrentLicense;
            }

            // 1. Verify HWID binding
            if (!string.Equals(record.HardwareId, _hwid, StringComparison.OrdinalIgnoreCase))
            {
                CurrentLicense = new LicenseInfo { IsActive = false, HardwareId = _hwid, StatusMessage = "License is bound to another PC." };
                return CurrentLicense;
            }

            DateTime now = DateTime.UtcNow;

            // 2. Anti-Clock Rollback Detection:
            // If system clock was turned backwards by more than 10 minutes to bypass expiration, lockout immediately!
            if (record.LastVerifiedUtc > DateTime.MinValue && now < record.LastVerifiedUtc.AddMinutes(-10))
            {
                CurrentLicense = new LicenseInfo
                {
                    IsActive = false,
                    HardwareId = _hwid,
                    StatusMessage = "System clock tampering detected. Please set your Windows clock to current time."
                };
                return CurrentLicense;
            }

            // 3. Verify Expiration
            if (record.ExpiresAtUtc <= now)
            {
                CurrentLicense = new LicenseInfo
                {
                    IsActive = false,
                    Tier = record.Tier,
                    TierDisplayName = GetTierName(record.Tier),
                    ActivatedAtUtc = record.ActivatedAtUtc,
                    ExpiresAtUtc = record.ExpiresAtUtc,
                    HardwareId = _hwid,
                    StatusMessage = $"Your {GetTierName(record.Tier)} expired on {record.ExpiresAtUtc.ToLocalTime():yyyy-MM-dd HH:mm}. Please enter a new code."
                };
                return CurrentLicense;
            }

            if (!string.IsNullOrEmpty(record.SignedEntitlement))
            {
                if (!ValidateEntitlement(record.SignedEntitlement, out var claims)
                    || !string.Equals(claims.HardwareId, _hwid, StringComparison.OrdinalIgnoreCase)
                    || claims.Tier != (int)record.Tier
                    || claims.ExpiresAtUtc != record.ExpiresAtUtc
                    || claims.ExpiresAtUtc <= now)
                {
                    CurrentLicense = new LicenseInfo { IsActive = false, HardwareId = _hwid, StatusMessage = "The saved activation signature is invalid." };
                    return CurrentLicense;
                }
            }

            // Update monotonic verification timestamp
            record.LastVerifiedUtc = now;
            record.LastVerifiedTick = Environment.TickCount64;
            try
            {
                string updatedJson = JsonSerializer.Serialize(record);
                byte[] raw = Encoding.UTF8.GetBytes(updatedJson);
                byte[] reEncrypted = ProtectedData.Protect(raw, null, DataProtectionScope.CurrentUser);
                File.WriteAllBytes(_licenseFilePath, reEncrypted);
            }
            catch { }

            CurrentLicense = new LicenseInfo
            {
                IsActive = true,
                Tier = record.Tier,
                TierDisplayName = GetTierName(record.Tier),
                ActivatedAtUtc = record.ActivatedAtUtc,
                ExpiresAtUtc = record.ExpiresAtUtc,
                HardwareId = _hwid,
                StatusMessage = string.IsNullOrEmpty(record.SignedEntitlement)
                    ? "Existing activation requires online verification."
                    : "Server-signed activation is valid."
            };
            return CurrentLicense;
        }
        catch (Exception ex)
        {
            CurrentLicense = new LicenseInfo { IsActive = false, HardwareId = _hwid, StatusMessage = $"License verification error: {ex.Message}" };
            return CurrentLicense;
        }
    }

    private static readonly HttpClient _authClient = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    private sealed record EntitlementClaims(string HardwareId, int Tier, DateTime ActivatedAtUtc, DateTime ExpiresAtUtc, string Subject);

    private static bool ValidateEntitlement(string token, out EntitlementClaims claims)
    {
        claims = default!;
        try
        {
            string[] parts = token.Split('.');
            if (parts.Length != 3) return false;
            using var header = JsonDocument.Parse(DecodeBase64Url(parts[0]));
            if (header.RootElement.GetProperty("alg").GetString() != "RS256") return false;
            string keyId = header.RootElement.GetProperty("kid").GetString() ?? "";
            if (!LicensePublicKeys.TryGetValue(keyId, out var publicKey)) return false;

            byte[] signedBytes = Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}");
            byte[] signature = DecodeBase64Url(parts[2]);
            using var rsa = RSA.Create();
            rsa.ImportFromPem(publicKey);
            if (!rsa.VerifyData(signedBytes, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)) return false;

            using var payload = JsonDocument.Parse(DecodeBase64Url(parts[1]));
            var root = payload.RootElement;
            if (root.GetProperty("iss").GetString() != "donut-boost") return false;
            string hardwareId = root.GetProperty("hwid").GetString() ?? "";
            string subject = root.GetProperty("sub").GetString() ?? "";
            int tier = root.GetProperty("tier").GetInt32();
            DateTime activated = DateTime.Parse(root.GetProperty("activated_at").GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
            DateTime expires = DateTime.Parse(root.GetProperty("expires_at").GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
            if (hardwareId.Length is < 1 or > 128 || subject.Length == 0 || tier is < 1 or > 5 || expires <= activated) return false;
            claims = new EntitlementClaims(hardwareId, tier, activated, expires, subject);
            return true;
        }
        catch { return false; }
    }

    private static byte[] DecodeBase64Url(string value)
    {
        string base64 = value.Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight(base64.Length + ((4 - base64.Length % 4) % 4), '=');
        return Convert.FromBase64String(base64);
    }

    public (bool Success, string Message, LicenseInfo Info) RedeemCode(string rawCode)
    {
        return RedeemCodeAsync(rawCode).GetAwaiter().GetResult();
    }

    public async Task<(bool Success, string Message, LicenseInfo Info)> RedeemCodeAsync(
        string rawCode, Func<bool>? confirmRestore = null)
    {
        string cleaned = (rawCode ?? string.Empty).ToUpperInvariant().Replace("-", string.Empty).Replace(" ", string.Empty).Trim();
        if (cleaned.Length != 16 || cleaned.Any(character => !Alphabet.Contains(character)))
            return (false, "Enter a valid activation code.", CurrentLicense);

        var cloud = await ClaimCodeWithCloudAsync(cleaned, _hwid);
        if (cloud.CanRestore && confirmRestore?.Invoke() == true)
            cloud = await ClaimCodeWithCloudAsync(cleaned, _hwid, restore: true);
        if (!cloud.Success || !cloud.ActivatedAtUtc.HasValue || !cloud.ExpiresAtUtc.HasValue || string.IsNullOrEmpty(cloud.Entitlement))
            return (false, cloud.Message, CurrentLicense);

        DateTime now = DateTime.UtcNow;
        var record = new LicenseRecord
        {
            Code = cleaned,
            Tier = cloud.Tier,
            ActivatedAtUtc = cloud.ActivatedAtUtc.Value,
            ExpiresAtUtc = cloud.ExpiresAtUtc.Value,
            LastVerifiedUtc = now,
            LastVerifiedTick = Environment.TickCount64,
            HardwareId = _hwid,
            SignedEntitlement = cloud.Entitlement
        };
        try
        {
            byte[] raw = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(record));
            File.WriteAllBytes(_licenseFilePath, ProtectedData.Protect(raw, null, DataProtectionScope.CurrentUser));
            LoadLicense();
            return CurrentLicense.IsActive
                ? (true, $"Success! Activated {GetTierName(record.Tier)}.", CurrentLicense)
                : (false, CurrentLicense.StatusMessage, CurrentLicense);
        }
        catch
        {
            return (false, "Could not securely save the activation on this device.", CurrentLicense);
        }
    }

    private sealed record CloudClaimResult(bool Success, string Message, LicenseTier Tier, DateTime? ActivatedAtUtc, DateTime? ExpiresAtUtc, string Entitlement, bool Retryable = false, bool CanRestore = false);

    private async Task<CloudClaimResult> ClaimCodeWithCloudAsync(string cleanCode, string hwid, bool trial = false, bool restore = false)
    {
        try
        {
            const string supabaseUrl = "https://qigzqfntaokxnsfcejiy.supabase.co/functions/v1/redeem-boost-license";
            const string publicKey = "sb_publishable_e4pO853gqnhrfzYp19XCNQ_h6DLr3a0";
            using var request = new HttpRequestMessage(HttpMethod.Post, supabaseUrl);
            request.Headers.Add("apikey", publicKey);
            request.Content = new StringContent(JsonSerializer.Serialize(new
            {
                operation = trial ? "trial" : restore ? "restore" : "redeem",
                code = trial ? null : cleanCode,
                hardwareId = hwid
            }), Encoding.UTF8, "application/json");
            using var response = await _authClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
                {
                    try
                    {
                        using var errorDocument = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                        if (errorDocument.RootElement.TryGetProperty("code", out var code)
                            && code.GetString() == "ACTIVATION_ALREADY_USED")
                            return new(false, "This code has already been activated on this device.", LicenseTier.None, null, null, "", CanRestore: true);
                    }
                    catch { }
                }
                return new(false, "The licensing server could not verify this activation. Please try again later.", LicenseTier.None, null, null, "",
                    (int)response.StatusCode >= 500 || response.StatusCode is System.Net.HttpStatusCode.RequestTimeout or System.Net.HttpStatusCode.TooManyRequests);
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = document.RootElement;
            string entitlement = root.GetProperty("entitlement").GetString() ?? "";
            if (!ValidateEntitlement(entitlement, out var claims)
                || !string.Equals(claims.HardwareId, hwid, StringComparison.OrdinalIgnoreCase)
                || claims.ExpiresAtUtc <= DateTime.UtcNow)
                return new(false, "This build could not verify the server's signed activation. Contact Donut Support for help restoring this code.", LicenseTier.None, null, null, "");
            return new(true, "Activation verified.", (LicenseTier)claims.Tier, claims.ActivatedAtUtc, claims.ExpiresAtUtc, entitlement);
        }
        catch (HttpRequestException)
        {
            return new(false, "An internet connection is required to verify this activation.", LicenseTier.None, null, null, "", true);
        }
        catch
        {
            return new(false, "Donut could not verify this activation. Please try again later.", LicenseTier.None, null, null, "");
        }
    }
    private static DateTime? ReadUtcDate(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String)
            return null;

        return DateTime.TryParse(value.GetString(), CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : null;
    }

    public async Task<(bool Success, string Message)> VerifyStoredLicenseCloudAsync()
    {
        try
        {
            if (!File.Exists(_licenseFilePath)) return (false, "No saved activation was found.");
            byte[] encrypted = File.ReadAllBytes(_licenseFilePath);
            byte[] decrypted = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            var record = JsonSerializer.Deserialize<LicenseRecord>(Encoding.UTF8.GetString(decrypted));
            if (record == null || record.Tier == LicenseTier.None || !string.Equals(record.HardwareId, _hwid, StringComparison.OrdinalIgnoreCase))
                return (false, "The saved activation could not be verified.");
            bool trial = record.Tier == LicenseTier.FreeTrial;
            string code = trial ? string.Empty : (record.Code ?? string.Empty).ToUpperInvariant().Replace("-", string.Empty).Replace(" ", string.Empty).Trim();
            var claim = await ClaimCodeWithCloudAsync(code, _hwid, trial, restore: !trial);
            if (!claim.Success || claim.Tier != record.Tier || !claim.ActivatedAtUtc.HasValue || !claim.ExpiresAtUtc.HasValue)
            {
                if (claim.Retryable
                    && ValidateEntitlement(record.SignedEntitlement, out var cached)
                    && string.Equals(cached.HardwareId, _hwid, StringComparison.OrdinalIgnoreCase)
                    && cached.Tier == (int)record.Tier
                    && cached.ExpiresAtUtc > DateTime.UtcNow)
                {
                    var offline = LoadLicense();
                    return (offline.IsActive, offline.IsActive ? "Using the saved signed activation while the server is unavailable." : offline.StatusMessage);
                }
                CurrentLicense = new LicenseInfo { IsActive = false, Tier = record.Tier, TierDisplayName = GetTierName(record.Tier), HardwareId = _hwid, StatusMessage = claim.Message };
                return (false, claim.Message);
            }
            record.ActivatedAtUtc = claim.ActivatedAtUtc.Value;
            record.ExpiresAtUtc = claim.ExpiresAtUtc.Value;
            record.SignedEntitlement = claim.Entitlement;
            record.LastVerifiedUtc = DateTime.UtcNow;
            record.LastVerifiedTick = Environment.TickCount64;
            byte[] updated = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(record));
            File.WriteAllBytes(_licenseFilePath, ProtectedData.Protect(updated, null, DataProtectionScope.CurrentUser));
            var current = LoadLicense();
            return (current.IsActive, current.StatusMessage);
        }
        catch (HttpRequestException)
        {
            CurrentLicense.IsActive = false;
            CurrentLicense.StatusMessage = "An internet connection is required to verify this activation.";
            return (false, CurrentLicense.StatusMessage);
        }
        catch
        {
            CurrentLicense.IsActive = false;
            CurrentLicense.StatusMessage = "This activation could not be verified with Donut's licensing server.";
            return (false, CurrentLicense.StatusMessage);
        }
    }

    public async Task<(bool Success, string Message, LicenseInfo Info)> ActivateTrialAsync()
    {
        var cloud = await ClaimCodeWithCloudAsync(string.Empty, _hwid, trial: true);
        if (!cloud.Success || !cloud.ActivatedAtUtc.HasValue || !cloud.ExpiresAtUtc.HasValue)
            return (false, cloud.Message, CurrentLicense);
        var record = new LicenseRecord
        {
            Code = "FREE-TRIAL-PASS", Tier = LicenseTier.FreeTrial,
            ActivatedAtUtc = cloud.ActivatedAtUtc.Value, ExpiresAtUtc = cloud.ExpiresAtUtc.Value,
            LastVerifiedUtc = DateTime.UtcNow, LastVerifiedTick = Environment.TickCount64,
            HardwareId = _hwid, SignedEntitlement = cloud.Entitlement
        };
        try
        {
            byte[] raw = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(record));
            File.WriteAllBytes(_licenseFilePath, ProtectedData.Protect(raw, null, DataProtectionScope.CurrentUser));
            LoadLicense();
            return CurrentLicense.IsActive
                ? (true, "24-hour free trial activated.", CurrentLicense)
                : (false, CurrentLicense.StatusMessage, CurrentLicense);
        }
        catch { return (false, "Could not securely save the trial on this device.", CurrentLicense); }
    }
    public static string GetTierName(LicenseTier tier) => tier switch
    {
        LicenseTier.OneDay => "1 Day Pass (24 Hours)",
        LicenseTier.SevenDays => "7 Days Pass",
        LicenseTier.OneMonth => "1 Month Pass (30 Days)",
        LicenseTier.Lifetime => "Lifetime License (Permanent)",
        LicenseTier.FreeTrial => "24-Hour Free Pass",
        _ => "None"
    };

    private static string GenerateMachineHwid()
    {
        try
        {
            string machineGuid = string.Empty;
            using (var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                                        .OpenSubKey(@"SOFTWARE\Microsoft\Cryptography"))
            {
                machineGuid = key?.GetValue("MachineGuid")?.ToString() ?? string.Empty;
            }

            string combined = $"{Environment.MachineName}|{Environment.UserName}|{machineGuid}|{Environment.ProcessorCount}|{IntPtr.Size}";
            using var sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(combined));
            string hex = Convert.ToHexString(hash);
            return $"{hex.Substring(0, 4)}-{hex.Substring(4, 4)}-{hex.Substring(8, 4)}";
        }
        catch
        {
            // String.GetHashCode is randomized between processes in modern
            // .NET. Use a deterministic fallback so an activation remains
            // bound to the same profile when MachineGuid cannot be read.
            string fallback = $"{Environment.MachineName}|{Environment.UserDomainName}|{Environment.UserName}|{Environment.ProcessorCount}|{IntPtr.Size}|{Environment.OSVersion.Version}";
            string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fallback)));
            return $"{hash[..4]}-{hash[4..8]}-{hash[8..12]}";
        }
    }

}
