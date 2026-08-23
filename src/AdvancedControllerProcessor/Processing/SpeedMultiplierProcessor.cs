using AdvancedControllerProcessor.Models;

namespace AdvancedControllerProcessor.Processing;

/// <summary>
/// Applies independent X and Y speed multipliers.
///
/// The multiplier scales the stick value away from center.
/// Values are NOT clamped here — ClampProcessor handles that at the end.
///
/// Mathematical note:
///   output = input * multiplier
///   When |output| > 1.0, ClampProcessor will cap it to [-1, 1].
///   This is intentional — the clamp at the boundary is acceptable behavior.
///   The alternative (non-linear scaling) would introduce distortion.
/// </summary>
public sealed class SpeedMultiplierProcessor : IStickProcessor
{
    public float Process(float input, ProcessingSettings settings)
    {
        // This processor is called separately for X and Y.
        // The caller passes the appropriate multiplier via a wrapper or by
        // calling ProcessX/ProcessY directly.
        return input * settings.XSpeedMultiplier;
    }

    /// <summary>
    /// Process X axis with X speed multiplier.
    /// </summary>
    public static float ProcessX(float input, ProcessingSettings settings) =>
        input * Math.Clamp(settings.XSpeedMultiplier, 0.1f, 3.0f);

    /// <summary>
    /// Process Y axis with Y speed multiplier.
    /// </summary>
    public static float ProcessY(float input, ProcessingSettings settings) =>
        input * Math.Clamp(settings.YSpeedMultiplier, 0.1f, 3.0f);
}
