using System.Security.Cryptography;
using System.Text;

namespace LicenseManager;

/// <summary>
/// License key generation and hashing.
/// MUST stay byte-identical to Helpers/LicenseCrypto.cs in the main app —
/// both sides must produce the same hashes for the same keys.
/// </summary>
public static class LicenseCrypto
{
    public const string Pepper = "ACP-LIC-2026::9f3KpQ";
    public const string Alphabet = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";
    public const int KeyLength = 16;

    private static readonly char[] AlphabetChars = Alphabet.ToCharArray();

    public static string GenerateKey()
    {
        // MUST mirror the main app exactly: rejection sampling over the
        // 31-char alphabet (bytes >= 248 rejected to avoid modulo bias).
        Span<char> chars = stackalloc char[KeyLength];
        Span<byte> random = stackalloc byte[KeyLength * 2];
        int filled = 0;
        while (filled < KeyLength)
        {
            RandomNumberGenerator.Fill(random);
            for (int i = 0; i < random.Length && filled < KeyLength; i++)
            {
                int value = random[i];
                if (value >= 248)
                    continue;
                chars[filled++] = AlphabetChars[value % AlphabetChars.Length];
            }
        }

        return new string(chars);
    }

    /// <summary>Normalize a key that was typed/pasted (uppercase, strip noise).</summary>
    public static string Normalize(string input) =>
        new(input.ToUpperInvariant().Where(Alphabet.Contains).Take(KeyLength).ToArray());

    public static bool IsValidFormat(string normalized) =>
        normalized.Length == KeyLength && !normalized.Any(c => Alphabet.IndexOf(c) < 0);

    public static string FormatGrouped(string normalized) =>
        normalized.Length != KeyLength
            ? normalized
            : $"{normalized[..4]}-{normalized[4..8]}-{normalized[8..12]}-{normalized[12..16]}";

    public static string HashKey(string normalizedKey)
    {
        byte[] data = Encoding.UTF8.GetBytes(normalizedKey + Pepper);
        return Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
    }
}
