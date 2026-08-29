using System.Runtime.InteropServices;
using AdvancedControllerProcessor.Models;

namespace AdvancedControllerProcessor.Helpers;

/// <summary>
/// Optional, opt-in process-wide performance boosts:
///   - "High performance" Windows power plan (restores the original on exit)
///   - 1 ms global timer resolution via timeBeginPeriod (restored on exit)
///
/// Everything here is off by default and only activated when the user enables
/// the matching settings. Nothing is applied against the user's explicit
/// choice — this keeps the program "controller-only" unless asked otherwise.
/// </summary>
public static class SystemPerformanceBoost
{
    // Windows "High performance" power scheme GUID
    private static readonly Guid HighPerformanceScheme =
        new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");

    private static readonly object Sync = new();

    private static Guid? _originalScheme;
    private static bool _powerOverridden;
    private static bool _timerActive;

    /// <summary>
    /// Align process-wide boosts with the current settings. Idempotent;
    /// safe to call on startup, on exit, and from settings toggles.
    /// </summary>
    public static void Apply(AppSettings settings)
    {
        if (settings is null)
            return;

        lock (Sync)
        {
            ApplyTimer(settings.EnableHighResolutionTimer);
            ApplyPowerPlan(settings.EnableHighPerformancePowerPlan);
        }
    }

    /// <summary>Undo whatever was applied. Called on application exit.</summary>
    public static void Restore()
    {
        lock (Sync)
        {
            ApplyTimer(false);
            ApplyPowerPlan(false);
        }
    }

    private static void ApplyTimer(bool enable)
    {
        if (enable == _timerActive)
            return;

        try
        {
            if (enable)
            {
                _ = timeBeginPeriod(1);
                _timerActive = true;
                Logging.Info("Global timer resolution raised to 1 ms (timeBeginPeriod)");
            }
            else
            {
                _ = timeEndPeriod(1);
                _timerActive = false;
                Logging.Info("Global timer resolution restored (timeEndPeriod)");
            }
        }
        catch (Exception ex)
        {
            Logging.Warn($"Could not adjust timer resolution: {ex.Message}");
        }
    }

    private static void ApplyPowerPlan(bool useHighPerformance)
    {
        if (useHighPerformance == _powerOverridden)
            return;

        try
        {
            if (useHighPerformance)
            {
                _originalScheme = GetActiveScheme();
                SetActiveScheme(HighPerformanceScheme);
                _powerOverridden = true;
                Logging.Info("Active power plan set to High performance (restored on exit)");
            }
            else
            {
                Guid? previous = _originalScheme;
                _originalScheme = null;
                _powerOverridden = false;

                if (previous is { } scheme)
                {
                    SetActiveScheme(scheme);
                    Logging.Info("Original Windows power plan restored");
                }
            }
        }
        catch (Exception ex)
        {
            Logging.Warn($"Could not change power plan: {ex.Message}");
        }
    }

    private static Guid? GetActiveScheme()
    {
        nint schemePtr = IntPtr.Zero;
        uint result = PowerGetActiveScheme(IntPtr.Zero, out schemePtr);
        if (result != 0 || schemePtr == IntPtr.Zero)
            return null;

        try
        {
            return Marshal.PtrToStructure<Guid>(schemePtr);
        }
        finally
        {
            _ = LocalFree(schemePtr);
        }
    }

    private static void SetActiveScheme(Guid scheme)
    {
        uint result = PowerSetActiveScheme(IntPtr.Zero, ref scheme);
        if (result == 0)
            return;

        // 0x80070005 = E_ACCESSDENIED: the app is not running as administrator.
        Logging.Warn(result == 0x80070005
            ? "Power plan change requires administrator rights (skipped)"
            : $"PowerSetActiveScheme failed with error 0x{result:X8}");
    }

    // ── Win32 ─────────────────────────────────────────────

    [DllImport("powrprof.dll", SetLastError = true)]
    private static extern uint PowerGetActiveScheme(nint hPowerKey, out nint activePolicyGuid);

    [DllImport("powrprof.dll", SetLastError = true)]
    private static extern uint PowerSetActiveScheme(nint hPowerKey, ref Guid schemeGuid);

    [DllImport("kernel32.dll")]
    private static extern nint LocalFree(nint hMem);

    [DllImport("winmm.dll", SetLastError = true)]
    private static extern uint timeBeginPeriod(uint uPeriod);

    [DllImport("winmm.dll", SetLastError = true)]
    private static extern uint timeEndPeriod(uint uPeriod);
}