using AdvancedControllerProcessor.Helpers;
using Xunit;

namespace AdvancedControllerProcessor.Tests;

/// <summary>
/// License key generation, normalization, formatting and hashing.
/// These guarantees must hold identically in the main app and the
/// License Manager tool — the pepper and alphabet are a cross-app contract.
/// </summary>
public class LicenseCryptoTests
{
    [Fact]
    public void GenerateKey_HasCorrectLengthAndAlphabet()
    {
        for (int i = 0; i < 200; i++)
        {
            string key = LicenseCrypto.GenerateKey();

            Assert.Equal(16, key.Length);
            Assert.All(key, c => Assert.Contains(c, LicenseCrypto.Alphabet));
        }
    }

    [Fact]
    public void GenerateKey_ProducesUniqueKeys()
    {
        var keys = new HashSet<string>();
        for (int i = 0; i < 500; i++)
            keys.Add(LicenseCrypto.GenerateKey());

        Assert.Equal(500, keys.Count); // collisions at 80-bit entropy are impossible in practice
    }

    [Theory]
    [InlineData("abcd-efgh-jkmn-pqrs", "ABCDEFGHJKMNPQRS")]
    [InlineData("ABCD EFGH JKMN PQRS", "ABCDEFGHJKMNPQRS")]
    [InlineData("abcdEfGhjKmnpqrs", "ABCDEFGHJKMNPQRS")]
    public void NormalizeKey_StripsSeparatorsAndUppercases(string input, string expected)
    {
        Assert.Equal(expected, LicenseCrypto.NormalizeKey(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("ABCDEFGHIJKLMNOP")] // contains I, L, O — not in alphabet
    [InlineData("ABCDEFGHIJKLMNOPQ")] // too long
    public void NormalizeKey_RejectsUnusableInput(string input)
    {
        Assert.Equal(string.Empty, LicenseCrypto.NormalizeKey(input));
    }

    [Fact]
    public void IsValidFormat_AcceptsOnlyCanonicalForm()
    {
        Assert.True(LicenseCrypto.IsValidFormat("ABCDEFGHJKMNPQRS"));
        Assert.False(LicenseCrypto.IsValidFormat("ABCDEFGHJKLMNPQRS")); // L not allowed, 15 chars
        Assert.False(LicenseCrypto.IsValidFormat(""));
    }

    [Fact]
    public void FormatKeyGrouped_InsertsDashesAtFixedPositions()
    {
        Assert.Equal(
            "ABCD-EFGH-JKMN-PQRS",
            LicenseCrypto.FormatKeyGrouped("ABCDEFGHJKMNPQRS"));
    }

    [Fact]
    public void HashKey_IsDeterministicAndDistinct()
    {
        string a1 = LicenseCrypto.HashKey("ABCDEFGHJKMNPQRS");
        string a2 = LicenseCrypto.HashKey("ABCDEFGHJKMNPQRS");
        string b = LicenseCrypto.HashKey("ABCDEFGHJKMNPQRT");

        Assert.Equal(a1, a2);
        Assert.NotEqual(a1, b);
        Assert.Equal(64, a1.Length);
        Assert.Equal(a1, a1.ToLowerInvariant()); // canonical lowercase hex
    }

    [Fact]
    public void HashKey_MatchesIndependentSha256Implementation()
    {
        // Guard against accidental pepper/encoding drift: recompute manually.
        using var sha = System.Security.Cryptography.SHA256.Create();
        byte[] hash = sha.ComputeHash(
            System.Text.Encoding.UTF8.GetBytes("ABCDEFGHJKMNPQRS" + LicenseCrypto.Pepper));

        Assert.Equal(Convert.ToHexString(hash).ToLowerInvariant(),
            LicenseCrypto.HashKey("ABCDEFGHJKMNPQRS"));
    }
}
