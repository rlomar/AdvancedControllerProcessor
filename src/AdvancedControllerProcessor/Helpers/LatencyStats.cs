using System.Threading;

namespace AdvancedControllerProcessor.Helpers;

/// <summary>
/// Allocation-free, thread-safe running statistics for one latency metric.
///
/// Hot path (<see cref="Record"/>) is O(1), uses only Interlocked operations
/// and never allocates — it runs on the input thread up to ~1000×/second.
/// A fixed 256-slot ring buffer keeps the most recent samples so percentiles
/// reflect current behaviour rather than the whole session history.
///
/// <see cref="Snapshot"/> is called from the UI thread (~2 Hz) and may observe
/// slightly torn concurrent samples — acceptable for display statistics.
/// </summary>
public sealed class LatencyStatistic
{
    private const int SampleSlots = 256;

    private readonly long[] _samples = new long[SampleSlots];
    private long _count;
    private long _sum;
    private long _max;
    private long _cursor;

    /// <summary>Record one sample in microseconds. Negative values clamp to 0.</summary>
    public void Record(long valueUs)
    {
        if (valueUs < 0)
            valueUs = 0;

        Interlocked.Increment(ref _count);
        Interlocked.Add(ref _sum, valueUs);

        // Lock-free max update (CAS loop).
        var spin = new SpinWait();
        while (true)
        {
            long current = Volatile.Read(ref _max);
            if (valueUs <= current ||
                Interlocked.CompareExchange(ref _max, valueUs, current) == current)
                break;
            spin.SpinOnce();
        }

        long slot = Interlocked.Increment(ref _cursor) - 1;
        Volatile.Write(ref _samples[(int)(slot % SampleSlots)], valueUs);
    }

    /// <summary>Aggregate over all samples plus P95 of the most recent ones.</summary>
    public (double Average, long Max, double P95, long Count) Snapshot()
    {
        long count = Interlocked.Read(ref _count);
        if (count == 0)
            return (0, 0, 0, 0);

        long sum = Interlocked.Read(ref _sum);
        long max = Interlocked.Read(ref _max);
        long cursor = Interlocked.Read(ref _cursor);

        double average = (double)sum / count;

        long recent = Math.Min(count, SampleSlots);
        if (recent <= 1)
            return (average, max, max, count);

        Span<long> buffer = stackalloc long[SampleSlots];
        int n = 0;
        for (long i = cursor - 1; i >= cursor - recent && n < buffer.Length; i--)
            buffer[n++] = Volatile.Read(ref _samples[(int)(i % SampleSlots)]);

        if (n == 0)
            return (average, max, max, count);

        buffer[..n].Sort();
        int idx = Math.Min(n - 1, (int)Math.Ceiling(n * 0.95) - 1);
        return (average, max, buffer[idx], count);
    }

    /// <summary>Clear all statistics (e.g. when processing toggles).</summary>
    public void Reset()
    {
        Interlocked.Exchange(ref _count, 0);
        Interlocked.Exchange(ref _sum, 0);
        Interlocked.Exchange(ref _max, 0);
        Interlocked.Exchange(ref _cursor, 0);
        Array.Clear(_samples);
    }
}

/// <summary>
/// The two latency metrics tracked by the input pipeline:
///   Pipeline — time spent inside Process()+SubmitState() per frame.
///   Wait     — time a report is held by submission pacing before delivery.
/// </summary>
public static class Latency
{
    public static readonly LatencyStatistic Pipeline = new();
    public static readonly LatencyStatistic Wait = new();
}
