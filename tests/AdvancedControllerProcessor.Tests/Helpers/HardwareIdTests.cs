using AdvancedControllerProcessor.Helpers;
using Xunit;

namespace AdvancedControllerProcessor.Tests;

/// <summary>
/// Device fingerprint hashing. The full pipeline (registry + fallbacks) needs
/// a live machine; the pure mixing core is what must be provably stable and
/// order-sensitive.
/// </summary>
public class HardwareIdTests
{
    [Fact]
    public void ComputeHash_IsDeterministic()
    {
        string a1 = HardwareId.ComputeHash("guid-123", "AMD Ryzen 7");
        string a2 = HardwareId.ComputeHash("guid-123", "AMD Ryzen 7");

        Assert.Equal(a1, a2);
    }

    [Fact]
    public void ComputeHash_ChangesWhenAnyComponentChanges()
    {
        string baseline = HardwareId.ComputeHash("guid-123", "CPU-A");

        Assert.NotEqual(baseline, HardwareId.ComputeHash("guid-124", "CPU-A"));
        Assert.NotEqual(baseline, HardwareId.ComputeHash("guid-123", "CPU-B"));
    }

    [Fact]
    public void ComputeHash_ComponentBoundaryIsUnambiguous()
    {
        // ("AB","C") and ("A","BC") concatenate to the same string — the
        // internal separator must prevent them from colliding.
        Assert.NotEqual(
            HardwareId.ComputeHash("AB", "C"),
            HardwareId.ComputeHash("A", "BC"));
    }

    [Fact]
    public void GetDeviceHash_IsStableWithinProcessAndWellFormed()
    {
        string h1 = HardwareId.GetDeviceHash();
        string h2 = HardwareId.GetDeviceHash();

        Assert.Equal(h1, h2);
        Assert.Matches("^[0-9a-f]{64}$", h1);
    }

    [Fact]
    public void GetShortDeviceId_IsDerivedFromFullHash()
    {
        string hash = HardwareId.GetDeviceHash();
        string shortId = HardwareId.GetShortDeviceId();

        string head = hash[..8].ToUpperInvariant();
        Assert.Equal($"{head[..4]}-{head[4..]}", shortId);
    }
}
