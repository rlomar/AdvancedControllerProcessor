namespace AdvancedControllerProcessor.Models;

/// <summary>
/// Complete controller state from a single poll.
/// Contains all input data normalized to standard ranges.
///
/// Struct (not class): this value is produced on every HID report
/// (~125-1000x/second). Heap-allocating it caused constant GC churn that
/// surfaced as periodic input stutters and a mushy stick feel during long
/// sessions.
///
/// Stick axes:  [-1.0, +1.0]  (center = 0.0)
/// Triggers:    [0.0, 1.0]    (released = 0.0, pressed = 1.0)
/// </summary>
public readonly record struct ControllerState
{
    /// <summary>Left analog stick position.</summary>
    public StickState LeftStick { get; init; } // default = (0, 0) = Center

    /// <summary>Right analog stick position.</summary>
    public StickState RightStick { get; init; } // default = (0, 0) = Center

    /// <summary>Left trigger (L2) analog value. Range [0.0, 1.0].</summary>
    public float L2 { get; init; }

    /// <summary>Right trigger (R2) analog value. Range [0.0, 1.0].</summary>
    public float R2 { get; init; }

    /// <summary>Pressed buttons as bit flags.</summary>
    public GamepadButton Buttons { get; init; }

    /// <summary>D-Pad hat switch state.</summary>
    public DPDirection DPad { get; init; }

    /// <summary>How the controller is connected.</summary>
    public ConnectionType Connection { get; init; }

    /// <summary>Timestamp of when this state was read.</summary>
    public DateTime Timestamp { get; init; }

    /// <summary>
    /// Get D-Pad as a stick value for XInput compatibility.
    /// Returns normalized (-1, -1) to (1, 1) based on hat position.
    /// </summary>
    public StickState DPadAsStick => DPad switch
    {
        DPDirection.Up => new StickState(0f, -1f),
        DPDirection.UpRight => new StickState(0.707f, -0.707f),
        DPDirection.Right => new StickState(1f, 0f),
        DPDirection.DownRight => new StickState(0.707f, 0.707f),
        DPDirection.Down => new StickState(0f, 1f),
        DPDirection.DownLeft => new StickState(-0.707f, 0.707f),
        DPDirection.Left => new StickState(-1f, 0f),
        DPDirection.UpLeft => new StickState(-0.707f, -0.707f),
        _ => StickState.Center
    };

    public override string ToString() =>
        $"LS={LeftStick} RS={RightStick} L2={L2:F2} R2={R2:F2} Btn={Buttons} DP={DPad}";
}

/// <summary>
/// D-Pad directional states. Matches DualSense hat switch values.
/// </summary>
public enum DPDirection : byte
{
    Up = 0,
    UpRight = 1,
    Right = 2,
    DownRight = 3,
    Down = 4,
    DownLeft = 5,
    Left = 6,
    UpLeft = 7,
    Neutral = 8
}
