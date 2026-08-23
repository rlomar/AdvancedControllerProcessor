using AdvancedControllerProcessor.Models;
using AdvancedControllerProcessor.Services;
using Xunit;

namespace AdvancedControllerProcessor.Tests;

/// <summary>
/// Guards the zero-allocation guarantee of the input hot path.
///
/// The pipeline runs 250-1000x/second on the input thread. Any heap
/// allocation there triggers periodic Gen0 GC pauses that surface as
/// intermittent input stutter during long sessions (the "game randomly
/// gets heavy" bug). These tests fail if a regression reintroduces
/// allocations into parse/process.
/// </summary>
public class HotPathAllocationTests
{
    private static byte[] MakeUsbReport()
    {
        var buffer = new byte[64];
        buffer[0] = 0x01;   // Report ID
        buffer[1] = 0x80;   // LSX center
        buffer[2] = 0x80;   // LSY center
        buffer[3] = 0x90;   // RSX slightly right
        buffer[4] = 0x70;   // RSY slightly up
        buffer[5] = 0x10;   // L2
        buffer[6] = 0xF0;   // R2
        buffer[8] = 0x08;   // D-Pad neutral, no face buttons
        return buffer;
    }

    private static byte[] MakeBluetoothReport()
    {
        var buffer = new byte[78];
        buffer[0] = 0x31;   // Report ID
        buffer[1] = 0xC0;   // LSX
        buffer[2] = 0x40;   // LSY
        buffer[3] = 0x80;   // RSX center
        buffer[4] = 0x80;   // RSY center
        buffer[5] = 0x08;   // D-Pad neutral
        buffer[8] = 0x20;   // L2 axis
        buffer[9] = 0xA0;   // R2 axis
        return buffer;
    }

    [Fact]
    public void ParseUsbReport_IsAllocationFree()
    {
        var buffer = MakeUsbReport();
        DualSenseControllerService.ParseReport(buffer, ConnectionType.USB); // JIT warm-up

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10_000; i++)
            DualSenseControllerService.ParseReport(buffer, ConnectionType.USB);

        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
    }

    [Fact]
    public void ParseBluetoothReport_IsAllocationFree()
    {
        var buffer = MakeBluetoothReport();
        DualSenseControllerService.ParseReport(buffer, ConnectionType.Bluetooth); // JIT warm-up

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10_000; i++)
            DualSenseControllerService.ParseReport(buffer, ConnectionType.Bluetooth);

        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
    }

    [Fact]
    public void Process_FullPipeline_IsAllocationFree()
    {
        var service = new InputProcessingService
        {
            ProcessingEnabled = true,
            CurrentProfile = Profile.Default()
        };

        var state = new ControllerState
        {
            LeftStick = new StickState(0.5f, -0.5f),
            RightStick = new StickState(-0.25f, 0.25f),
            L2 = 0.3f,
            R2 = 0.7f,
            Buttons = GamepadButton.Cross | GamepadButton.L1,
            DPad = DPDirection.Neutral,
            Connection = ConnectionType.USB
        };

        service.Process(state); // JIT warm-up + curve cache fill

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10_000; i++)
            service.Process(state);

        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
    }

    [Fact]
    public void ParseUsbReport_ProducesSaneValues()
    {
        var buffer = MakeUsbReport();
        var state = DualSenseControllerService.ParseReport(buffer, ConnectionType.USB);

        Assert.Equal(0f, state.LeftStick.X);
        Assert.Equal(0f, state.LeftStick.Y);
        Assert.InRange(state.RightStick.X, 0.01f, 0.2f);
        Assert.InRange(state.RightStick.Y, -0.2f, -0.01f);
        Assert.InRange(state.L2, 0.05f, 0.11f);
        Assert.Equal(ConnectionType.USB, state.Connection);
    }
}
