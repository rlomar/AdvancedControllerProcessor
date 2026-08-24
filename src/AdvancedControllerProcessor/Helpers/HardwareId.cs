using System.IO;
using System.Security.Cryptography;
using Microsoft.Win32;

namespace AdvancedControllerProcessor.Helpers;

/// <summary>
/// Stable per-machine fingerprint used to bind a license key to one device.
///
/// Sources (first available wins, combined deterministically):
///   1. HKLM\SOFTWARE\Microsoft\Cryptography\MachineGuid — stable across
///      reboots and hardware tweaks, changes only on OS reinstall.
///   2. CPU name from HKLM\HARDWARE\DESCRIPTION\... — extra entropy.
///
/// If both registry reads fail (locked-down kiosk etc.), a random seed is
/// generated once and persisted next to the executable so the identity stays
/// stable on that installation.
///
/// Only the SHA-256 hash ever leaves the machine — the raw components never do.
/// </summary>
public static class HardwareId
{
    private const string MachineGuidPath = @"SOFTWARE\Microsoft\Cryptography";
    private const string MachineGuidValue = "MachineGuid";
    private const string CpuNamePath = @"HARDWARE\DESCRIPTION\System\CentralProcessor\0";
    private const string CpuNameValue = "ProcessorNameString";

    private static readonly object Sync = new();
    private static string? _cachedHash;
    private static string? _cachedShortId;

    /// <summary>Full 64-hex-char device hash sent to the license server.</summary>
    public static string GetDeviceHash()
    {
        if (_cachedHash is not null)
            return _cachedHash;

        lock (Sync)
        {
            if (_cachedHash is not null)
                return _cachedHash;

            string guid = ReadMachineGuid() ?? ReadOrCreateSeed();
            string cpu = ReadCpuName() ?? "unknown-cpu";

            // Separator prevents component-boundary ambiguity:
            // ("AB","C") and ("A","BC") must not collide.
            _cachedHash = LicenseCrypto.HashComponent(guid + "|" + cpu);
            return _cachedHash;
        }
    }

    /// <summary>
    /// Short human-friendly form shown in the activation window
    /// (informational — the full hash is what gets registered).
    /// </summary>
    public static string GetShortDeviceId()
    {
        if (_cachedShortId is not null)
            return _cachedShortId;

        string hash = GetDeviceHash();
        string head = hash[..8].ToUpperInvariant();
        _cachedShortId = $"{head[..4]}-{head[4..]}";
        return _cachedShortId;
    }

    /// <summary>
    /// Pure core exposed for unit tests: deterministic, order-sensitive mix.
    /// </summary>
    internal static string ComputeHash(string machineGuid, string cpuName) =>
        LicenseCrypto.HashComponent(machineGuid + "|" + cpuName);

    private static string? ReadMachineGuid()
    {
        try
        {
            return Registry.LocalMachine
                .OpenSubKey(MachineGuidPath)?
                .GetValue(MachineGuidValue) as string;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? ReadCpuName()
    {
        try
        {
            return Registry.LocalMachine
                .OpenSubKey(CpuNamePath)?
                .GetValue(CpuNameValue) as string;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string ReadOrCreateSeed()
    {
        try
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "hwid.dat");
            if (File.Exists(path))
            {
                string existing = File.ReadAllText(path).Trim();
                if (existing.Length > 0)
                    return existing;
            }

            string seed = Guid.NewGuid().ToString("D");
            File.WriteAllText(path, seed);
            return seed;
        }
        catch (Exception ex)
        {
            Logging.Warn($"[HardwareId] Could not persist fallback seed: {ex.Message}");
            // Last resort: process-lifetime GUID. Activation still works but a
            // restart would look like a new device — acceptable degraded mode.
            return Guid.NewGuid().ToString("D");
        }
    }
}
