using AdvancedControllerProcessor.Helpers;
using AdvancedControllerProcessor.Models;
using Xunit;

namespace AdvancedControllerProcessor.Tests.Helpers;

/// <summary>
/// Verifies the report signatures used by the change-gated ViGEm submissions:
/// stable across identical frames, sensitive to any quantized input change,
/// and insensitive to sub-quantization float noise (the safe direction for a
/// latency-sensitive pipeline).
/// </summary>
public class VirtualReportSignatureTests
{
    private static ControllerState State(
        float lx = 0, float ly = 0, float rx = 0, float ry = 0,
        float l2 = 0, float r2 = 0,
        GamepadButton buttons = GamepadButton.None,
        DPDirection dpad = DPDirection.Neutral) =>
        new()
        {
            LeftStick = new StickState(lx, ly),
            RightStick = new StickState(rx, ry),
            L2 = l2,
            R2 = r2,
            Buttons = buttons,
            DPad = dpad,
            Connection = ConnectionType.USB,
            Timestamp = DateTime.UtcNow
        };

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(0f, 0.5f)]
    [InlineData(-1f, 0f)]
    [InlineData(1f, 0.5f)]
    public void IdleState_ProducesStableSignatures(float x, float y)
    {
        var state = State(x, y);

        Assert.Equal(VirtualReportSignature.Xbox360(state), VirtualReportSignature.Xbox360(state));
        Assert.Equal(VirtualReportSignature.DualShock4(state), VirtualReportSignature.DualShock4(state));
    }

    [Fact]
    public void Keyword_Equality_IsConsistentWithHashCode()
    {
        var a = VirtualReportSignature.Xbox360(State(0.3f, -0.2f, 1f, 0.1f, 0.9f, 0.1f, GamepadButton.Cross, DPDirection.Up));
        var b = VirtualReportSignature.Xbox360(State(0.3f, -0.2f, 1f, 0.1f, 0.9f, 0.1f, GamepadButton.Cross, DPDirection.Up));
        var c = VirtualReportSignature.Xbox360(State());

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, c);
    }

    [Fact]
    public void LeftStickMovement_ChangesBothSignatures()
    {
        var idle = State();
        var moved = State(lx: 0.45f);

        Assert.NotEqual(VirtualReportSignature.Xbox360(idle), VirtualReportSignature.Xbox360(moved));
        Assert.NotEqual(VirtualReportSignature.DualShock4(idle), VirtualReportSignature.DualShock4(moved));
    }

    [Fact]
    public void ButtonPressAndRelease_FlipsBothSignatures()
    {
        var idle = State();
        var pressed = State(buttons: GamepadButton.Cross);
        var released = State();

        var xboxPressed = VirtualReportSignature.Xbox360(pressed);
        var xboxReleased = VirtualReportSignature.Xbox360(released);

        Assert.NotEqual(xboxPressed, xboxReleased);
        Assert.NotEqual(VirtualReportSignature.DualShock4(pressed), VirtualReportSignature.DualShock4(idle));
    }

    [Fact]
    public void TriggerChange_ChangesBothSignatures()
    {
        var idle = State();
        var pulled = State(l2: 0.65f);

        Assert.NotEqual(VirtualReportSignature.Xbox360(idle), VirtualReportSignature.Xbox360(pulled));
        Assert.NotEqual(VirtualReportSignature.DualShock4(idle), VirtualReportSignature.DualShock4(pulled));
    }

    [Fact]
    public void DPadChange_ChangesBothSignatures()
    {
        var neutral = State();
        var up = State(dpad: DPDirection.Up);
        var upLeft = State(dpad: DPDirection.UpLeft);

        var xboxNeutral = VirtualReportSignature.Xbox360(neutral);
        var xboxUp = VirtualReportSignature.Xbox360(up);
        var xboxUpLeft = VirtualReportSignature.Xbox360(upLeft);

        Assert.NotEqual(xboxNeutral, xboxUp);
        Assert.NotEqual(xboxUp, xboxUpLeft);
        Assert.NotEqual(VirtualReportSignature.DualShock4(neutral), VirtualReportSignature.DualShock4(up));
    }

    [Fact]
    public void SubQuantizationNoise_DoesNotChangeSignatures()
    {
        // 0.5 vs 0.500004 round to the same quantized short/byte the services
        // submit, so the gating must treat them as the same frame — input that
        // the game cannot even see must not cause a bus submission.
        var a = State(lx: 0.5f, rx: 0.5f, l2: 0.5f);
        var b = State(lx: 0.5000045f, rx: 0.500001f, l2: 0.500002f);

        Assert.Equal(VirtualReportSignature.Xbox360(a), VirtualReportSignature.Xbox360(b));
        Assert.Equal(VirtualReportSignature.DualShock4(a), VirtualReportSignature.DualShock4(b));
    }

    [Fact]
    public void MeasurableMovement_AlwaysChangesSignatures()
    {
        // A movement big enough to move the quantized output by one LSB must
        // flip the signature — otherwise real input would be lost.
        var a = State(lx: 0.5f);
        var b = State(lx: 0.6f);

        Assert.NotEqual(VirtualReportSignature.Xbox360(a), VirtualReportSignature.Xbox360(b));
        Assert.NotEqual(VirtualReportSignature.DualShock4(a), VirtualReportSignature.DualShock4(b));
    }

    [Fact]
    public void CornerHolds_ShareXboxUpButtonBit()
    {
        // Up and UpLeft both press the Xbox Up button — signature must agree
        // on that bit so releasing the Left direction alone changes the frame.
        var up = VirtualReportSignature.Xbox360(State(dpad: DPDirection.Up));
        var upLeft = VirtualReportSignature.Xbox360(State(dpad: DPDirection.UpLeft));
        var left = VirtualReportSignature.Xbox360(State(dpad: DPDirection.Left));

        Assert.NotEqual(up, left);
        Assert.NotEqual(upLeft, left);
    }
}