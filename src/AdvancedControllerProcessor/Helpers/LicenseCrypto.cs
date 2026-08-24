using System.Security.Cryptography;
using System.Text;

namespace AdvancedControllerProcessor.Helpers;

/// <summary>
/// License key generation, normalization and hashing.
///
/// Key format: XXXX-XXXX-XXXX-XXXX — 16 chars from a 32-char alphabet with no
/// ambiguous glyphs (no 0/O, 1/I/L), giving ~80 bits of entropy. Keys are only
/// ever transmitted and stored as SHA-256 hashes; the plaintext exists solely
/// on the owner's machine (License Manager) and with the user it was issued to.
///
/// The pepper is embedded in the binary. It adds no protection against someone
/// who decompiles the app, but prevents trivially building rainbow tables from
/// the public database schema alone.
/// </summary>
public static class LicenseCrypto
{
    /// <summary>
    /// Shared secret mixed into every hash. MUST stay identical across the
    /// main app and the License Manager tool.
    /// </summary>
    public const string Pepper = "ACP-LIC-2026::9f3KpQ";

    /// <summary>32-char alphabet: digits 2-9 + uppercase letters minus I, L, O.</summary>
    public const string Alphabet = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";

    /// <summary>Number of characters in a normalized key (4 groups × 4).</summary>
    public const int KeyLength = 16;

    private static readonly char[] AlphabetChars = Alphabet.ToCharArray();

    /// <summary>
    /// Generate a cryptographically random key in normalized form
    /// ("XXXXXXXXXXXXXXXX", no dashes). Callers display it via
    /// <see cref="FormatKeyGrouped"/>.
    /// </summary>
    public static string GenerateKey()
    {
        // Rejection sampling over the 31-char alphabet: 256 % 31 == 9, so
        // bytes >= 248 are discarded to keep every character equiprobable.
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
                    continue; // biased tail — reject
                chars[filled++] = AlphabetChars[value % AlphabetChars.Length];
            }
        }

        return new string(chars);
    }

    /// <summary>
    /// Normalize user input: uppercase, strip everything that is not an
    /// alphabet character (dashes, spaces, lowercase, typos). Returns the
    /// 16-char canonical form or empty string when unusable.
    /// </summary>
    public static string NormalizeKey(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        Span<char> buffer = stackalloc char[KeyLength + 1];
        int n = 0;
        foreach (char c in input)
        {
            char up = char.ToUpperInvariant(c);
            if (Alphabet.IndexOf(up) >= 0)
            {
                if (n >= buffer.Length)
                    return string.Empty; // too long — reject early
                buffer[n++] = up;
            }
        }

        return n == KeyLength ? new string(buffer[..n]) : string.Empty;
    }

    /// <summary>True when the string is exactly 16 valid alphabet characters.</summary>
    public static bool IsValidFormat(string normalized) =>
        normalized.Length == KeyLength && !normalized.Any(c => Alphabet.IndexOf(c) < 0);

    /// <summary>Insert dashes for display: XXXXXXXX… → XXXX-XXXX-XXXX-XXXX.</summary>
    public static string FormatKeyGrouped(string normalized)
    {
        if (normalized.Length != KeyLength)
            return normalized;

        return string.Create(KeyLength + 3, normalized, static (span, src) =>
        {
            for (int i = 0, s = 0; i < span.Length; i++)
            {
                if (i is 4 or 9 or 14)
                    span[i] = '-';
                else
                    span[i] = src[s++];
            }
        });
    }

    /// <summary>
    /// SHA-256 of (normalizedKey + pepper) as 64 lowercase hex chars.
    /// This exact value is what lives in the licenses table and what the
    /// RPC functions compare against.
    /// </summary>
    public static string HashKey(string normalizedKey)
    {
        byte[] data = Encoding.UTF8.GetBytes(normalizedKey + Pepper);
        byte[] hash = SHA256.HashData(data);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Hash an arbitrary component string with the pepper — shared building
    /// block used by <see cref="HardwareId"/> too. 64 lowercase hex chars.
    /// </summary>
    internal static string HashComponent(string component)
    {
        byte[] data = Encoding.UTF8.GetBytes(component + Pepper);
        byte[] hash = SHA256.HashData(data);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
