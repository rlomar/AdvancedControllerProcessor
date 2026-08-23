using AdvancedControllerProcessor.Models;

namespace AdvancedControllerProcessor.Processing;

/// <summary>
/// Applies direction-dependent speed multipliers to the left stick.
///
/// When enabled, the speed is determined by the dominant direction of stick movement:
///   - Forward (Y < 0): uses ForwardSpeed
///   - Backward (Y > 0): uses BackwardSpeed
///   - Left (X < 0): uses LeftSpeed
///   - Right (X > 0): uses RightSpeed
///
/// For diagonal movement, both X and Y multipliers are applied independently.
/// This allows asymmetric control: e.g., faster forward than backward in Rocket League.
///
/// Only applies when DirectionalSpeedEnabled is true.
/// When disabled, returns input unchanged.
/// </summary>
public sealed class DirectionalSpeedProcessor
{
    /// <summary>
    /// Process a full 2D stick with directional speed.
    /// </summary>
    public static StickState Process(StickState input, ProcessingSettings settings)
    {
        if (!settings.DirectionalSpeedEnabled)
            return input;

        float xMultiplier = GetHorizontalMultiplier(input.X, settings);
        float yMultiplier = GetVerticalMultiplier(input.Y, settings);

        return new StickState(
            input.X * Math.Clamp(xMultiplier, 0.1f, 3.0f),
            input.Y * Math.Clamp(yMultiplier, 0.1f, 3.0f)
        );
    }

    /// <summary>
    /// Gets the Y-axis multiplier based on direction.
    /// Y < 0 = forward (up on screen), Y > 0 = backward (down on screen).
    /// </summary>
    private static float GetVerticalMultiplier(float y, ProcessingSettings settings)
    {
        if (y < 0)
            return Math.Clamp(settings.ForwardSpeed, 0.1f, 3.0f);

        return Math.Clamp(settings.BackwardSpeed, 0.1f, 3.0f);
    }

    /// <summary>
    /// Gets the X-axis multiplier based on direction.
    /// X < 0 = left, X > 0 = right.
    /// </summary>
    private static float GetHorizontalMultiplier(float x, ProcessingSettings settings)
    {
        if (x < 0)
            return Math.Clamp(settings.LeftSpeed, 0.1f, 3.0f);

        return Math.Clamp(settings.RightSpeed, 0.1f, 3.0f);
    }
}
