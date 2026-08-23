using AdvancedControllerProcessor.Models;

namespace AdvancedControllerProcessor.Processing;

/// <summary>
/// Clamps a single axis value to the valid range [-1.0, +1.0].
///
/// This MUST be the last processor in the pipeline.
/// It ensures no value outside the valid range reaches the virtual controller.
/// </summary>
public sealed class ClampProcessor
{
    /// <summary>
    /// Clamp a single value to [-1.0, +1.0].
    /// </summary>
    public static float Clamp(float value) =>
        Math.Clamp(value, -1f, 1f);

    /// <summary>
    /// Clamp a full stick state to [-1.0, +1.0] on both axes.
    /// </summary>
    public static StickState ClampStick(StickState value) =>
        new(Clamp(value.X), Clamp(value.Y));
}
