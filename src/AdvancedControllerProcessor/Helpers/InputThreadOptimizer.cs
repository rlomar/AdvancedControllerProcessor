using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AdvancedControllerProcessor.Helpers;

/// <summary>
/// Pins the input thread to the fastest cores (skipping Intel E-cores),
/// registers it with MMCSS "Games" scheduling, and disables Windows power
/// throttling for the process.
///
/// Why: on hybrid CPUs (Intel 12th-gen+) the OS occasionally schedules
/// background-class threads on efficiency cores, adding several ms of
/// input delay spikes that players perceive as intermittent heaviness.
/// </summary>
public static class InputThreadOptimizer
{
    private static long _affinityMask;
    private static bool _processOptimizationsApplied;

    /// <summary>
    /// Resolve optimizations up front so they can be applied to a thread
    /// before it starts. Safe to call multiple times; process-wide parts
    /// run once.
    /// </summary>
    public static void Prepare()
    {
        if (_affinityMask == 0)
        {
            try
            {
                _affinityMask = GetPerformanceCoreAffinityMask();
                if (_affinityMask != 0)
                    Logging.Info($"Performance-core affinity mask: 0x{_affinityMask:X}");
            }
            catch (Exception ex)
            {
                Logging.Warn($"Could not query core topology: {ex.Message}");
            }
        }

        ApplyProcessWideOptimizations();
    }

    /// <summary>
    /// Apply the prepared optimizations to the CALLING thread.
    /// Must be invoked from the input thread itself (kernel pseudo-handle).
    /// </summary>
    public static void ApplyToThisThread()
    {
        if (_affinityMask == 0)
            return;

        try
        {
            _ = SetThreadAffinityMask(GetCurrentThread(), (UIntPtr)_affinityMask);
        }
        catch (Exception ex)
        {
            Logging.Warn($"Could not set thread affinity: {ex.Message}");
        }
    }

    /// <summary>
    /// MMCSS "Games" registration + power-throttling opt-out. Runs once.
    /// </summary>
    private static void ApplyProcessWideOptimizations()
    {
        if (_processOptimizationsApplied)
            return;
        _processOptimizationsApplied = true;

        // MMCSS grants the calling thread scheduler-coordinated priority —
        // strictly stronger than plain High process priority.
        try
        {
            if (AvSetMmThreadCharacteristics("Games", out var index) != IntPtr.Zero)
                Logging.Info("MMCSS 'Games' registered");
            else
                _ = index;
        }
        catch (Exception ex)
        {
            Logging.Warn($"MMCSS registration unavailable: {ex.Message}");
        }

        // Opt out of power throttling so Windows never down-clocks the
        // process to save energy mid-session.
        try
        {
            var info = new PROCESS_POWER_THROTTLING_STATE
            {
                Version = 1,
                ControlMask = PROCESS_POWER_THROTTLING_EXECUTION_SPEED,
                StateMask = 0 // 0 = throttling disabled for this process
            };
            var handle = Process.GetCurrentProcess().Handle;
            _ = SetProcessInformation(handle, ProcessInformationClass.PowerThrottling,
                ref info, (uint)Marshal.SizeOf<PROCESS_POWER_THROTTLING_STATE>());
        }
        catch (Exception ex)
        {
            Logging.Warn($"Power-throttling opt-out unavailable: {ex.Message}");
        }
    }

    /// <summary>
    /// Builds an affinity mask covering only the most performant logical
    /// processors, based on per-core efficiency classes enumerated in kernel
    /// order. Returns 0 when the platform is uniform (caller keeps default).
    /// Internal for unit testing.
    /// </summary>
    internal static long BuildAffinityMask(IReadOnlyList<int> efficiencyClassPerCore, int logicalPerCore)
    {
        if (efficiencyClassPerCore.Count == 0)
            return 0;

        int best = efficiencyClassPerCore.Max();

        // Uniform platform: nothing to skip.
        if (efficiencyClassPerCore.All(c => c == best))
            return 0;

        long mask = 0;
        int bit = 0;
        foreach (int cls in efficiencyClassPerCore)
        {
            if (cls == best)
                mask |= (1L << logicalPerCore) - 1 << bit;
            bit += logicalPerCore;
        }
        return mask;
    }

    /// <summary>
    /// Queries the kernel for per-core efficiency classes and returns the
    /// affinity mask of the highest-performance cores. Returns 0 when the
    /// platform is uniform or the query fails (caller keeps default).
    /// </summary>
    private static long GetPerformanceCoreAffinityMask()
    {
        const int RelationProcessorCore = 0;

        int length = 0;
        _ = GetLogicalProcessorInformationEx(RelationProcessorCore, IntPtr.Zero, ref length);
        if (length == 0)
            return 0;

        nint buffer = Marshal.AllocHGlobal(length);
        try
        {
            if (!GetLogicalProcessorInformationEx(RelationProcessorCore, buffer, ref length))
                return 0;

            var classes = new List<int>();
            int maxThreadsPerCore = 1;
            nint cursor = buffer;
            nint end = buffer + length;

            while (cursor < end)
            {
                uint relationship = (uint)Marshal.ReadInt32(cursor);
                uint size = (uint)Marshal.ReadInt32(cursor, 4);
                if (size == 0)
                    break;

                if (relationship == RelationProcessorCore)
                {
                    // PROCESSOR_RELATIONSHIP layout (after the 8-byte header):
                    //   +0  BYTE  Flags
                    //   +1  BYTE  EfficiencyClass (higher = faster)
                    //   ...
                    //   +24 GROUP_AFFINITY[0].Mask (ULONGLONG)
                    classes.Add(Marshal.ReadByte(cursor, 8 + 1));
                    ulong groupMask = (ulong)Marshal.ReadInt64(cursor, 8 + 24);
                    int bits = 0;
                    while (groupMask != 0) { groupMask &= groupMask - 1; bits++; }
                    if (bits > maxThreadsPerCore) maxThreadsPerCore = bits;
                }

                cursor += (nint)size; // entries are DWORD-aligned
            }

            if (classes.Count == 0)
                return 0;

            return BuildAffinityMask(classes, Math.Max(1, maxThreadsPerCore));
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    // ── Win32 ─────────────────────────────────────────────

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetLogicalProcessorInformationEx(
        int relationshipClass, IntPtr buffer, ref int returnedLength);

    [DllImport("kernel32.dll")]
    private static extern nint GetCurrentThread();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern UIntPtr SetThreadAffinityMask(nint hThread, UIntPtr dwThreadAffinityMask);

    [DllImport("avrt.dll", SetLastError = true)]
    private static extern IntPtr AvSetMmThreadCharacteristics(
        string taskName, out uint taskIndex);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetProcessInformation(
        IntPtr hProcess,
        ProcessInformationClass infoClass,
        ref PROCESS_POWER_THROTTLING_STATE info,
        uint size);

    private enum ProcessInformationClass
    {
        MemoryProtection = 0,
        Protection = 1,
        PowerThrottling = 2
    }

    private struct PROCESS_POWER_THROTTLING_STATE
    {
        public uint Version;
        public uint ControlMask;
        public uint StateMask;
    }

    private const uint PROCESS_POWER_THROTTLING_EXECUTION_SPEED = 0x1;
}