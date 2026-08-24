using AdvancedControllerProcessor.Services;
using Xunit;

namespace AdvancedControllerProcessor.Tests;

/// <summary>
/// Tests for the polling-rate gate that controls how often parsed controller
/// states are submitted to the virtual pad (DualSenseControllerService).
/// Simulated time is in Stopwatch ticks (10,000 ticks = 1 ms).
/// </summary>
public class RateGateTests
{
    private const long TicksPerSecond = TimeSpan.TicksPerSecond;
    private const long MsTick = 10_000; // Stopwatch ticks per millisecond

    [Fact]
    public void ShouldSubmit_FirstReport_AlwaysSubmits()
    {
        long last = 0;

        Assert.True(DualSenseControllerService.ShouldSubmit(ref last, nowTicks: 0, periodTicks: 0));
        Assert.True(DualSenseControllerService.ShouldSubmit(ref last, nowTicks: 5 * MsTick, periodTicks: 2 * MsTick));
    }

    [Fact]
    public void ShouldSubmit_WithinPeriod_Rejects()
    {
        long last = 0; // just submitted at t=0

        Assert.False(DualSenseControllerService.ShouldSubmit(ref last, nowTicks: 1 * MsTick, periodTicks: 4 * MsTick));
        Assert.False(DualSenseControllerService.ShouldSubmit(ref last, nowTicks: 3 * MsTick, periodTicks: 4 * MsTick));
        // Schedule must not have moved
        Assert.Equal(0, last);
    }

    [Fact]
    public void ShouldSubmit_AtDueTime_SubmitsAndAdvancesByPeriod()
    {
        long last = 0;

        Assert.True(DualSenseControllerService.ShouldSubmit(ref last, nowTicks: 4 * MsTick, periodTicks: 4 * MsTick));
        Assert.Equal(4 * MsTick, last);
    }

    [Fact]
    public void ShouldSubmit_IsDriftFree()
    {
        // Reports arriving every 1 ms, target 500 Hz (2 ms period).
        // Drift-free scheduling keeps the average at exactly the target.
        const long period = TicksPerSecond / 500;
        long last = 0;
        int submitted = 0;
        long now = 0;

        for (int i = 0; i < 2000; i++) // 2 seconds of reports
        {
            now += MsTick;
            if (DualSenseControllerService.ShouldSubmit(ref last, now, period))
                submitted++;
        }

        Assert.InRange(submitted, 998, 1002); // 1000 expected over 2 s
    }

    [Fact]
    public void ShouldSubmit_AfterLongStall_DoesNotBurst()
    {
        const long period = TicksPerSecond / 250; // 4 ms
        long last = 0;

        // Simulate a 500 ms stall (e.g. reconnect sleep)
        long afterStall = 500 * MsTick;

        Assert.True(DualSenseControllerService.ShouldSubmit(ref last, afterStall, period));
        // Schedule capped at most one period behind real time
        Assert.Equal(afterStall - period, last);

        // One immediate catch-up submission, then steady 1-per-period cadence
        Assert.True(DualSenseControllerService.ShouldSubmit(ref last, afterStall + 1 * MsTick, period));
        Assert.False(DualSenseControllerService.ShouldSubmit(ref last, afterStall + 2 * MsTick, period));
        Assert.False(DualSenseControllerService.ShouldSubmit(ref last, afterStall + 3 * MsTick, period));
        Assert.True(DualSenseControllerService.ShouldSubmit(ref last, afterStall + 4 * MsTick, period));
    }

    [Fact]
    public void ComputeMeasuredHz_ConvertsCountOverWindow()
    {
        // 500 submissions in a 1-second window
        Assert.Equal(500, DualSenseControllerService.ComputeMeasuredHz(500, TicksPerSecond));

        // 100 submissions in a 200 ms window -> 500 Hz
        Assert.Equal(500, DualSenseControllerService.ComputeMeasuredHz(100, TicksPerSecond / 5));

        // Guard against divide-by-zero
        Assert.Equal(0, DualSenseControllerService.ComputeMeasuredHz(100, 0));
    }

    [Theory]
    [InlineData(124, 125)]
    [InlineData(125, 125)]
    [InlineData(249, 249)]
    [InlineData(250, 250)]
    [InlineData(500, 500)]
    [InlineData(1000, 1000)]
    [InlineData(1500, 1000)]
    [InlineData(0, 125)]
    public void PollingRate_Clamp_BoundsValue(int input, int expected)
    {
        Assert.Equal(expected, PollingRate.Clamp(input));
    }
}
