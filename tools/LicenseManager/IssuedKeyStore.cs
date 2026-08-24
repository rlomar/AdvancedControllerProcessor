using System.IO;

namespace LicenseManager;

/// <summary>
/// Append-only local log of every key this PC issued, so the owner can
/// re-copy a plaintext key later. The server only ever stores hashes.
/// File: %APPDATA%\LicenseManager\issued-keys.txt — lines: hash|plain|label|date
/// </summary>
public static class IssuedKeyStore
{
    private static string StorePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "LicenseManager", "issued-keys.txt");

    public static void Append(string hash, string plainGrouped, string label)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
            string safeLabel = (label ?? "").Replace("|", "/");
            File.AppendAllText(StorePath,
                $"{hash}|{plainGrouped}|{safeLabel}|{DateTime.Now:yyyy-MM-dd HH:mm}" + Environment.NewLine);
        }
        catch
        {
            // Never let logging break key creation.
        }
    }

    public static string? TryGet(string hash)
    {
        try
        {
            if (!File.Exists(StorePath)) return null;
            foreach (string line in File.ReadLines(StorePath))
            {
                var parts = line.Split('|');
                if (parts.Length >= 2 && parts[0] == hash)
                    return parts[1];
            }
        }
        catch
        {
            // Treated as missing.
        }
        return null;
    }
}
