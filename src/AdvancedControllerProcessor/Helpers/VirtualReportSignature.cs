using AdvancedControllerProcessor.Models;

namespace AdvancedControllerProcessor.Helpers;

/// <summary>
/// Compact, allocation-free fingerprints of the exact values a virtual
/// controller service would submit for a given <see cref="ControllerState"/>.
///
/// Used for change-gating: the DualSense keeps streaming HID reports at up to
/// ~1000 Hz even when the pad is idle or held still, and every report was
/// previously forwarded to ViGEmBus unconditionally. Comparing signatures
/// lets <see cref="Services.VirtualXboxControllerService"/> and
/// <see cref="Services.VirtualDualShock4Service"/> skip the whole
/// reset/set/submit cycle when nothing changed since the last delivered
/// frame — an idle pad then sends zero traffic to the bus driver instead of
/// flooding it at the full hardware rate.
///
/// Signatures must reproduce the services' exact quantization math
/// (<see cref="ShortFromXboxAxis"/>, <see cref="ByteFromUnit"/>, ...) bit for
/// bit, otherwise a gated frame could disagree with a submitted one.
/// </summary>
internal static class VirtualReportSignature
{
    /// <summary>128-bit equality key (two words). Zero allocations.</summary>
    internal readonly struct Key : IEquatable<Key>
    {
        public readonly ulong A;
        public readonly ulong B;

        public Key(ulong a, ulong b)
        {
            A = a;
            B = b;
        }

        public bool Equals(Key other) => A == other.A && B == other.B;

        public override bool Equals(object? obj) => obj is Key other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(A, B);

        public static bool operator ==(Key left, Key right) => left.Equals(right);

        public static bool operator !=(Key left, Key right) => !left.Equals(right);
    }

    /// <summary>
    /// Fingerprint of the Xbox 360 report built by
    /// <see cref="Services.VirtualXboxControllerService.SubmitState"/>.
    /// Word A: four stick axes as offset unsigned shorts (Y inverted).
    /// Word B: trigger bytes, D-Pad directions and the 14 submitted buttons.
    /// </summary>
    public static Key Xbox360(ControllerState s)
    {
        ulong a =
            (ulong)(ushort)(ShortFromXboxAxis(s.LeftStick.X) + 32768) << 0 |
            (ulong)(ushort)(ShortFromXboxAxis(-s.LeftStick.Y) + 32768) << 16 |
            (ulong)(ushort)(ShortFromXboxAxis(s.RightStick.X) + 32768) << 32 |
            (ulong)(ushort)(ShortFromXboxAxis(-s.RightStick.Y) + 32768) << 48;

        ulong b =
            (ulong)ByteFromUnit(s.L2) << 0 |
            (ulong)ByteFromUnit(s.R2) << 8 |
            (IsSet(s.DPad, DPDirection.Up) ? 1UL : 0) << 16 |
            (IsSet(s.DPad, DPDirection.Down) ? 1UL : 0) << 17 |
            (IsSet(s.DPad, DPDirection.Left) ? 1UL : 0) << 18 |
            (IsSet(s.DPad, DPDirection.Right) ? 1UL : 0) << 19 |
            (Has(s.Buttons, GamepadButton.A) ? 1UL : 0) << 20 |
            (Has(s.Buttons, GamepadButton.B) ? 1UL : 0) << 21 |
            (Has(s.Buttons, GamepadButton.X) ? 1UL : 0) << 22 |
            (Has(s.Buttons, GamepadButton.Y) ? 1UL : 0) << 23 |
            (Has(s.Buttons, GamepadButton.LeftShoulder) ? 1UL : 0) << 24 |
            (Has(s.Buttons, GamepadButton.RightShoulder) ? 1UL : 0) << 25 |
            (Has(s.Buttons, GamepadButton.LeftThumb) ? 1UL : 0) << 26 |
            (Has(s.Buttons, GamepadButton.RightThumb) ? 1UL : 0) << 27 |
            (Has(s.Buttons, GamepadButton.Start) ? 1UL : 0) << 28 |
            (Has(s.Buttons, GamepadButton.Back) ? 1UL : 0) << 29;

        return new Key(a, b);
    }

    /// <summary>
    /// Fingerprint of the DualShock 4 report built by
    /// <see cref="Services.VirtualDualShock4Service.SubmitState"/>.
    /// Word A: four stick axes as center-128 bytes, two trigger bytes and the
    /// D-Pad compass direction. Word B: the 12 submitted button bits
    /// (including PS and Touchpad special bits).
    /// </summary>
    public static Key DualShock4(ControllerState s)
    {
        ulong a =
            (ulong)AxisByteFrom(s.LeftStick.X) << 0 |
            (ulong)AxisByteFrom(s.LeftStick.Y) << 8 |
            (ulong)AxisByteFrom(s.RightStick.X) << 16 |
            (ulong)AxisByteFrom(s.RightStick.Y) << 24 |
            (ulong)ByteFromUnit(s.L2) << 32 |
            (ulong)ByteFromUnit(s.R2) << 40 |
            (ulong)(byte)s.DPad << 48;

        ulong b =
            (Has(s.Buttons, GamepadButton.Cross) ? 1UL : 0) << 0 |
            (Has(s.Buttons, GamepadButton.Circle) ? 1UL : 0) << 1 |
            (Has(s.Buttons, GamepadButton.Square) ? 1UL : 0) << 2 |
            (Has(s.Buttons, GamepadButton.Triangle) ? 1UL : 0) << 3 |
            (Has(s.Buttons, GamepadButton.L1) ? 1UL : 0) << 4 |
            (Has(s.Buttons, GamepadButton.R1) ? 1UL : 0) << 5 |
            (Has(s.Buttons, GamepadButton.L3) ? 1UL : 0) << 6 |
            (Has(s.Buttons, GamepadButton.R3) ? 1UL : 0) << 7 |
            (Has(s.Buttons, GamepadButton.Options) ? 1UL : 0) << 8 |
            (Has(s.Buttons, GamepadButton.Create) ? 1UL : 0) << 9 |
            (Has(s.Buttons, GamepadButton.PS) ? 1UL : 0) << 10 |
            (Has(s.Buttons, GamepadButton.Touchpad) ? 1UL : 0) << 11;

        return new Key(a, b);
    }

    /// <summary>Short in [-32768, +32767] for an Xbox 360 stick axis (XInput).</summary>
    private static short ShortFromXboxAxis(float value) =>
        (short)Math.Round(Math.Clamp(value, -1f, 1f) * 32767f);

    /// <summary>Byte in [0, 255] centered at 128, matching the DS4 stick axis convention.</summary>
    private static byte AxisByteFrom(float value) =>
        (byte)Math.Round(128f + Math.Clamp(value, -1f, 1f) * 127f);

    /// <summary>Byte in [0, 255] for a trigger/slider value in [0, 1].</summary>
    private static byte ByteFromUnit(float value) =>
        (byte)Math.Round(Math.Clamp(value, 0f, 1f) * 255f);

    private static bool Has(GamepadButton buttons, GamepadButton flag) => (buttons & flag) != 0;

    /// <summary>
    /// True when the hat is pressed toward the given direction (matches the
    /// corner arithmetic used by the Xbox D-Pad mapping).
    /// </summary>
    private static bool IsSet(DPDirection hat, DPDirection direction) => hat switch
    {
        DPDirection.Up => direction == DPDirection.Up,
        DPDirection.UpLeft => direction == DPDirection.Up || direction == DPDirection.Left,
        DPDirection.UpRight => direction == DPDirection.Up || direction == DPDirection.Right,
        DPDirection.Down => direction == DPDirection.Down,
        DPDirection.DownLeft => direction == DPDirection.Down || direction == DPDirection.Left,
        DPDirection.DownRight => direction == DPDirection.Down || direction == DPDirection.Right,
        DPDirection.Left => direction == DPDirection.Left,
        DPDirection.Right => direction == DPDirection.Right,
        _ => false
    };
}