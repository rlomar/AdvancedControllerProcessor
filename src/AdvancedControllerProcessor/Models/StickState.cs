namespace AdvancedControllerProcessor.Models;

/// <summary>
/// 2D analog stick state. Value type to avoid allocations in the hot path.
/// Both X and Y are in range [-1.0, +1.0]:
///   X: -1.0 = full left,  0.0 = center, +1.0 = full right
///   Y: -1.0 = full up,    0.0 = center, +1.0 = full down
/// </summary>
public readonly record struct StickState(float X = 0f, float Y = 0f)
{
    public static readonly StickState Center = new(0f, 0f);

    /// <summary>
    /// Euclidean magnitude from center. Range [0.0, ~1.41].
    /// </summary>
    public float Magnitude => MathF.Sqrt(X * X + Y * Y);

    /// <summary>
    /// Angle in radians from positive X axis.
    /// </summary>
    public float Angle => MathF.Atan2(Y, X);

    public override string ToString() => $"({X:F3}, {Y:F3})";
}
