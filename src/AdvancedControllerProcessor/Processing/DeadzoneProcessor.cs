using AdvancedControllerProcessor.Models;

namespace AdvancedControllerProcessor.Processing;

/// <summary>
/// Applies deadzone to a single axis.
///
/// Radial deadzone: uses Euclidean distance from center for the 2D stick.
///   Values inside the deadzone circle are mapped to center (0).
///   Values outside are rescaled so the deadzone edge maps to 0.
///
/// Axial deadzone: X and Y axes are processed independently.
///   Useful for flight sims where you want separate null zones per axis.
///
/// This processor is applied per-axis, so for radial deadzone the caller
/// (InputProcessingService) must pass the full stick state through a
/// radial-aware method. For per-axis processing, use Axial mode.
/// </summary>
public sealed class DeadzoneProcessor : IStickProcessor
{
    public float Process(float input, ProcessingSettings settings)
    {
        if (!settings.DeadzoneEnabled)
            return input;

        float dz = Math.Clamp(settings.Deadzone, 0f, 0.5f);

        if (settings.DeadzoneType == "Axial")
            return ProcessAxial(input, dz);

        // For per-axis calls in radial mode, we still process individually
        // but the rescaling uses the same formula
        return ProcessRadialSingle(input, dz);
    }

    /// <summary>
    /// Processes a full 2D stick with radial deadzone.
    /// Call this instead of processing X and Y separately for correct radial behavior.
    /// </summary>
    public static StickState ProcessRadial(StickState input, float deadzone)
    {
        float dz = Math.Clamp(deadzone, 0f, 0.5f);
        float magnitude = input.Magnitude;

        if (magnitude < dz)
            return StickState.Center;

        // Rescale so deadzone edge maps to 0 and magnitude 1 maps to 1
        float t = (magnitude - dz) / (1f - dz);
        float scale = t / magnitude;

        return new StickState(
            input.X * scale,
            input.Y * scale
        );
    }

    /// <summary>
    /// Axial deadzone: process each axis independently.
    /// </summary>
    public static StickState ProcessAxial(StickState input, float deadzoneX, float deadzoneY)
    {
        float dzX = Math.Clamp(deadzoneX, 0f, 0.5f);
        float dzY = Math.Clamp(deadzoneY, 0f, 0.5f);

        float x = ApplyAxial(input.X, dzX);
        float y = ApplyAxial(input.Y, dzY);

        return new StickState(x, y);
    }

    private static float ProcessRadialSingle(float input, float dz)
    {
        float abs = MathF.Abs(input);
        if (abs < dz)
            return 0f;

        float t = (abs - dz) / (1f - dz);
        return SignSafe(input) * t;
    }

    private static float ProcessAxial(float input, float dz)
    {
        float abs = MathF.Abs(input);
        if (abs < dz)
            return 0f;

        float t = (abs - dz) / (1f - dz);
        return SignSafe(input) * t;
    }

    private static float ApplyAxial(float input, float dz)
    {
        float abs = MathF.Abs(input);
        if (abs < dz)
            return 0f;

        float t = (abs - dz) / (1f - dz);
        return SignSafe(input) * t;
    }

    /// <summary>
    /// MathF.Sign throws ArithmeticException on NaN. This branch-based
    /// equivalent maps NaN to 0 instead, keeping the input loop alive.
    /// </summary>
    private static float SignSafe(float v) => v < 0f ? -1f : v > 0f ? 1f : 0f;
}

