using AdvancedControllerProcessor.Models;
using AdvancedControllerProcessor.Processing;

namespace AdvancedControllerProcessor.Services;

/// <summary>
/// Orchestrates the full stick processing pipeline:
///
///   Raw Input -> Deadzone -> Response Curve -> Speed -> DirectionalSpeed -> Smoothing -> Clamp
///
/// For Left Stick: full pipeline based on ProcessingSettings.
/// For Right Stick: pass-through by default, optional full pipeline.
/// Buttons/Triggers/DPad: always pass-through (no processing).
///
/// Thread-safe: only Process() and ResetSmoothing() access mutable state,
/// and they are expected to be called from a single input thread.
/// </summary>
public sealed class InputProcessingService : IInputProcessingService
{
    private readonly SmoothingProcessor _leftSmoothing = new();
    private readonly SmoothingProcessor _rightSmoothing = new();

    public bool ProcessingEnabled { get; set; }
    public Profile CurrentProfile { get; set; } = Profile.Default();

    /// <summary>
    /// Process raw controller state through the full pipeline.
    /// When ProcessingEnabled is false, raw input passes through unmodified.
    /// </summary>
    public ControllerState Process(ControllerState rawInput)
    {
        if (!ProcessingEnabled)
        {
            // Pass-through: return raw input directly
            return rawInput;
        }

        var leftStick = ProcessLeftStick(rawInput.LeftStick);
        var rightStick = ProcessRightStick(rawInput.RightStick);

        return new ControllerState
        {
            LeftStick = leftStick,
            RightStick = rightStick,
            L2 = rawInput.L2,
            R2 = rawInput.R2,
            Buttons = rawInput.Buttons,
            DPad = rawInput.DPad,
            Connection = rawInput.Connection,
            Timestamp = rawInput.Timestamp
        };
    }

    /// <summary>
    /// Reset smoothing state for both sticks.
    /// Call when switching profiles, entering safe mode, or toggling processing.
    /// </summary>
    public void ResetSmoothing()
    {
        _leftSmoothing.Reset();
        _rightSmoothing.Reset();
    }

    /// <summary>
    /// Process left stick through the full pipeline.
    ///
    /// Pipeline order:
    ///   1. Deadzone (Radial or Axial)
    ///   2. Response Curve (Linear/Soft/Aggressive/Custom)
    ///   3. Speed Multiplier (X/Y independent)
    ///   4. Directional Speed (optional Forward/Back/Left/Right)
    ///   5. Smoothing (optional EMA)
    ///   6. Clamp [-1, +1]
    /// </summary>
    private StickState ProcessLeftStick(StickState raw)
    {
        var settings = CurrentProfile.LeftStick;

        // 1. Deadzone
        StickState step1 = settings.DeadzoneEnabled
            ? (settings.DeadzoneType == "Axial"
                ? DeadzoneProcessor.ProcessAxial(raw, settings.Deadzone, settings.Deadzone)
                : DeadzoneProcessor.ProcessRadial(raw, settings.Deadzone))
            : raw;

        // 2. Response Curve
        var curve = ResolveCurve(settings.ResponseCurve, settings.CustomCurvePoints);
        StickState step2 = new StickState(
            ApplyCurveWithSign(step1.X, curve),
            ApplyCurveWithSign(step1.Y, curve));

        // 3. Speed Multiplier
        StickState step3 = new StickState(
            SpeedMultiplierProcessor.ProcessX(step2.X, settings),
            SpeedMultiplierProcessor.ProcessY(step2.Y, settings));

        // 4. Directional Speed
        StickState step4 = DirectionalSpeedProcessor.Process(step3, settings);

        // 5. Smoothing
        StickState step5 = _leftSmoothing.Process(step4, settings);

        // 6. Clamp
        return ClampProcessor.ClampStick(step5);
    }

    /// <summary>
    /// Process right stick. By default this is pass-through.
    /// Only applies full pipeline if RightStick.ProcessingEnabled is true.
    /// </summary>
    private StickState ProcessRightStick(StickState raw)
    {
        var rightSettings = CurrentProfile.RightStick;

        if (!rightSettings.ProcessingEnabled || rightSettings.Settings is null)
            return raw; // Pass-through

        var settings = rightSettings.Settings;

        // Same pipeline as left stick
        StickState step1 = settings.DeadzoneEnabled
            ? (settings.DeadzoneType == "Axial"
                ? DeadzoneProcessor.ProcessAxial(raw, settings.Deadzone, settings.Deadzone)
                : DeadzoneProcessor.ProcessRadial(raw, settings.Deadzone))
            : raw;

        var curve = ResolveCurve(settings.ResponseCurve, settings.CustomCurvePoints);
        StickState step2 = new StickState(
            ApplyCurveWithSign(step1.X, curve),
            ApplyCurveWithSign(step1.Y, curve));

        StickState step3 = new StickState(
            SpeedMultiplierProcessor.ProcessX(step2.X, settings),
            SpeedMultiplierProcessor.ProcessY(step2.Y, settings));

        StickState step4 = DirectionalSpeedProcessor.Process(step3, settings);

        StickState step5 = _rightSmoothing.Process(step4, settings);

        return ClampProcessor.ClampStick(step5);
    }

    /// <summary>
    /// Resolve a response curve from its name and custom points.
    /// </summary>
    private static IResponseCurve ResolveCurve(string curveName, List<CurvePoint>? customPoints)
    {
        return curveName switch
        {
            "Soft" => SoftCurve.Instance,
            "Aggressive" => AggressiveCurve.Instance,
            "Custom" => new CustomCurve(customPoints ?? []),
            _ => LinearCurve.Instance // "Linear" or unknown
        };
    }

    /// <summary>
    /// Apply a response curve to a signed value.
    /// The curve operates on absolute values, sign is preserved.
    /// This ensures the curve doesn't flip the stick direction.
    /// </summary>
    private static float ApplyCurveWithSign(float signedValue, IResponseCurve curve)
    {
        float abs = MathF.Abs(signedValue);
        float curved = curve.Evaluate(abs);
        return MathF.Sign(signedValue) * curved;
    }
}
