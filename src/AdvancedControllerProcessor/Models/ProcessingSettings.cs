using Newtonsoft.Json;

namespace AdvancedControllerProcessor.Models;

/// <summary>
/// All processing parameters for a single stick (Left or Right).
/// This is the core configuration unit — each Profile contains one or more of these.
///
/// Default values produce pass-through behavior (no processing applied).
/// </summary>
public sealed class ProcessingSettings
{
    // ── Deadzone ──────────────────────────────────────────────

    /// <summary>Enable radial or axial deadzone processing.</summary>
    public bool DeadzoneEnabled { get; set; }

    /// <summary>
    /// Deadzone magnitude. Range [0.0, 0.5].
    /// Values inside this radius are mapped to center (0,0).
    /// </summary>
    [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
    public float Deadzone { get; set; }

    /// <summary>
    /// "Radial" — entire circle deadzone (recommended for most games).
    /// "Axial" — separate X and Y deadzones (useful for flight sims).
    /// </summary>
    public string DeadzoneType { get; set; } = "Radial";

    // ── Response Curve ────────────────────────────────────────

    /// <summary>
    /// Response curve type: "Linear", "Soft", "Aggressive", "Custom".
    /// </summary>
    public string ResponseCurve { get; set; } = "Linear";

    /// <summary>
    /// User-defined curve control points for "Custom" curve mode.
    /// First point must be (0,0), last must be (1,1).
    /// X = input, Y = output. Both in [0, 1].
    /// </summary>
    public List<CurvePoint> CustomCurvePoints { get; set; } = [];

    // ── Speed Multiplier ──────────────────────────────────────

    /// <summary>
    /// X-axis speed multiplier. Range [0.1, 3.0].
    /// Applied after deadzone and curve, before clamp.
    /// </summary>
    public float XSpeedMultiplier { get; set; } = 1.0f;

    /// <summary>
    /// Y-axis speed multiplier. Range [0.1, 3.0].
    /// Applied after deadzone and curve, before clamp.
    /// </summary>
    public float YSpeedMultiplier { get; set; } = 1.0f;

    // ── Directional Speed ─────────────────────────────────────

    /// <summary>Enable independent speed per direction.</summary>
    public bool DirectionalSpeedEnabled { get; set; }

    /// <summary>Speed when stick is pushed forward (Y negative on screen). Range [0.1, 3.0].</summary>
    public float ForwardSpeed { get; set; } = 1.0f;

    /// <summary>Speed when stick is pulled backward (Y positive on screen). Range [0.1, 3.0].</summary>
    public float BackwardSpeed { get; set; } = 1.0f;

    /// <summary>Speed when stick is pushed left (X negative). Range [0.1, 3.0].</summary>
    public float LeftSpeed { get; set; } = 1.0f;

    /// <summary>Speed when stick is pushed right (X positive). Range [0.1, 3.0].</summary>
    public float RightSpeed { get; set; } = 1.0f;

    // ── Smoothing ─────────────────────────────────────────────

    /// <summary>
    /// Enable exponential moving average smoothing.
    /// WARNING: Smoothing adds latency. Default OFF for minimum latency.
    /// </summary>
    public bool SmoothingEnabled { get; set; }

    /// <summary>
    /// Smoothing intensity. Range [0.0, 1.0].
    /// Higher = more smoothing = more latency.
    /// 0.0 = no smoothing, 1.0 = infinite smoothing (stuck).
    /// Recommended range: 0.1 – 0.5 when enabled.
    /// </summary>
    public float SmoothingAmount { get; set; }

    // ── Factory Methods ───────────────────────────────────────

    /// <summary>
    /// Creates a pass-through settings (no processing applied).
    /// </summary>
    public static ProcessingSettings PassThrough() => new()
    {
        DeadzoneEnabled = false,
        Deadzone = 0f,
        DeadzoneType = "Radial",
        ResponseCurve = "Linear",
        XSpeedMultiplier = 1f,
        YSpeedMultiplier = 1f,
        DirectionalSpeedEnabled = false,
        ForwardSpeed = 1f,
        BackwardSpeed = 1f,
        LeftSpeed = 1f,
        RightSpeed = 1f,
        SmoothingEnabled = false,
        SmoothingAmount = 0f
    };

    /// <summary>
    /// Creates safe-mode defaults: Linear curve, 1.0x speed, no deadzone, no smoothing.
    /// </summary>
    public static ProcessingSettings SafeMode() => PassThrough();
}

/// <summary>
/// A control point on the custom response curve.
/// X = input value [0.0, 1.0], Y = output value [0.0, 1.0].
/// The curve is constructed by linearly interpolating between sorted points.
/// </summary>
public sealed class CurvePoint
{
    public float X { get; set; }
    public float Y { get; set; }

    public CurvePoint() { }

    public CurvePoint(float x, float y)
    {
        X = x;
        Y = y;
    }

    public override string ToString() => $"({X:F3}, {Y:F3})";
}
