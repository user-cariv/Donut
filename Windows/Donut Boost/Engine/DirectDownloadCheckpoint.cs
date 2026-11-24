using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DonutBoost.Engine;

internal sealed record DirectDownloadCheckpoint(string PartialName, string EntityTag, long TotalBytes, long ContiguousBytes)
{
    public static string GetPath(string url, string folder)
    {
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url + "\n" + Path.GetFullPath(folder))));
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DonutBoost", "direct-resume", key + ".json");
    }

    public static DirectDownloadCheckpoint? Load(string metadataPath, string folder, string tag, long total)
    {
        try
        {
            var file = new FileInfo(metadataPath);
            if (!file.Exists || file.Length > 4096) return null;
            var saved = JsonSerializer.Deserialize<DirectDownloadCheckpoint>(File.ReadAllBytes(metadataPath));
            if (saved is null || saved.EntityTag != tag || saved.TotalBytes != total ||
                saved.ContiguousBytes < 0 || saved.ContiguousBytes > total ||
                string.IsNullOrEmpty(saved.PartialName) || saved.PartialName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                !saved.PartialName.EndsWith(".donutpart", StringComparison.Ordinal))
                return null;

            var partial = new FileInfo(Path.Combine(folder, saved.PartialName));
            return partial.Exists && partial.Length == total &&
                !partial.Attributes.HasFlag(FileAttributes.ReparsePoint) ? saved : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public void Save(string metadataPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(metadataPath)!);
        string pending = metadataPath + ".tmp";
        File.WriteAllBytes(pending, JsonSerializer.SerializeToUtf8Bytes(this));
        File.Move(pending, metadataPath, overwrite: true);
    }
}
