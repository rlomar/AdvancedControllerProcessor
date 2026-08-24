using AdvancedControllerProcessor.Helpers;
using Xunit;

namespace AdvancedControllerProcessor.Tests;

/// <summary>
/// Statistics collector used by the dashboard latency monitor. The hot path
/// must stay allocation-free; the aggregates must be exact for single-threaded
/// use and self-consistent under concurrency.
/// </summary>
public class LatencyStatsTests
{
    [Fact]
    public void EmptySnapshot_ReturnsZeros()
    {
        var stat = new LatencyStatistic();

        var (avg, max, p95, count) = stat.Snapshot();

        Assert.Equal(0, avg);
        Assert.Equal(0, max);
        Assert.Equal(0, p95);
        Assert.Equal(0, count);
    }

    [Fact]
    public void SingleSample_IsReportedExactly()
    {
        var stat = new LatencyStatistic();
        stat.Record(250);

        var (avg, max, p95, count) = stat.Snapshot();

        Assert.Equal(1, count);
        Assert.Equal(250, avg);
        Assert.Equal(250, max);
        Assert.Equal(250, p95);
    }

    [Fact]
    public void KnownSequence_ProducesExactAverageMaxAndP95()
    {
        var stat = new LatencyStatistic();
        // 100 samples: 1..100 µs → avg = 50.5, max = 100,
        // p95 over the last ≤256 samples = 95th value of sorted set = 95.
        for (int i = 1; i <= 100; i++)
            stat.Record(i);

        var (avg, max, p95, count) = stat.Snapshot();

        Assert.Equal(100, count);
        Assert.Equal(50.5, avg, precision: 5);
        Assert.Equal(100, max);
        Assert.InRange(p95, 94, 96); // percentile convention may pick 94/95/96
    }

    [Fact]
    public void NegativeValues_ClampToZero()
    {
        var stat = new LatencyStatistic();
        stat.Record(-500);

        var (avg, max, _, count) = stat.Snapshot();

        Assert.Equal(1, count);
        Assert.Equal(0, avg);
        Assert.Equal(0, max);
    }

    [Fact]
    public void RingBuffer_KeepsOnlyRecentWindowForPercentile()
    {
        var stat = new LatencyStatistic();

        for (int i = 1; i <= 300; i++)
            stat.Record(i * 10);

        // Total count exceeds the 256-slot ring; recent window = last 256
        // values (45..300)*10. P95 of that window ≈ value at index ~242 →
        // between 2860 and 2970 — NOT the global 295..300 range boundary.
        var (_, _, p95, count) = stat.Snapshot();

        Assert.Equal(300, count);
        Assert.InRange(p95, 2850, 3000);
        Assert.True(p95 >= 2860, $"p95 should come from the recent window, got {p95}");
    }

    [Fact]
    public void Reset_ClearsEverything()
    {
        var stat = new LatencyStatistic();
        stat.Record(1234);
        stat.Record(5678);
        stat.Reset();

        var (avg, max, p95, count) = stat.Snapshot();

        Assert.Equal(0, count);
        Assert.Equal(0, avg);
        Assert.Equal(0, max);
        Assert.Equal(0, p95);
    }

    [Fact]
    public async Task ConcurrentRecording_CountsEverySample()
    {
        var stat = new LatencyStatistic();
        const int threads = 8, perThread = 5_000;

        await Task.Run(() =>
        {
            Parallel.For(0, threads, _ =>
            {
                for (int i = 0; i < perThread; i++)
                    stat.Record(i % 1000 + 1);
            });
        });

        var (_, max, _, count) = stat.Snapshot();
        Assert.Equal(threads * perThread, count);
        Assert.Equal(1000, max);
    }

    [Fact]
    public void Record_HotPathIsAllocationFree()
    {
        var stat = new LatencyStatistic();
        stat.Record(1); // warm-up

        GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();

        for (int i = 0; i < 100_000; i++)
            stat.Record(i);

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated == 0, $"Hot path allocated {allocated} bytes");
    }
}
