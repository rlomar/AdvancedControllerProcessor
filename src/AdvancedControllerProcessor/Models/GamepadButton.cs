namespace AdvancedControllerProcessor.Models;

/// <summary>
/// Xbox 360 compatible button flags.
/// Used for both input reading and virtual controller output.
/// Matches the XInput bit layout.
/// </summary>
[Flags]
public enum GamepadButton : ushort
{
    None = 0,

    DPadUp = 1 << 0,
    DPadDown = 1 << 1,
    DPadLeft = 1 << 2,
    DPadRight = 1 << 3,

    Start = 1 << 4,
    Back = 1 << 5,

    LeftThumb = 1 << 6,
    RightThumb = 1 << 7,

    LeftShoulder = 1 << 8,
    RightShoulder = 1 << 9,

    // Xbox A/B/X/Y — mapped from DualSense Cross/Circle/Square/Triangle
    A = 1 << 12,       // Cross
    B = 1 << 13,       // Circle
    X = 1 << 14,       // Square
    Y = 1 << 15,       // Triangle

    // DualSense specific (mapped to unused XInput bits for passthrough)
    PS = 1 << 10,
    Touchpad = 1 << 11,
    Mute = 1 << 10,    // Alias — Mute is DualSense-only, mapped to PS slot in V1

    // Convenience aliases for DualSense → Xbox mapping
    Cross = A,
    Circle = B,
    Square = X,
    Triangle = Y,
    L1 = LeftShoulder,
    R1 = RightShoulder,
    L3 = LeftThumb,
    R3 = RightThumb,
    Options = Start,
    Create = Back,
}
