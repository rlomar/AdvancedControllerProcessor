namespace AdvancedControllerProcessor.Processing;

/// <summary>
/// Soft response curve: output = input^1.5
/// Provides more precision near the center (small movements stay small).
/// Good for fine aiming and small adjustments.
///
/// Mathematical properties:
///   input=0.0 -> output=0.0
///   input=0.5 -> output=0.354 (softer response)
///   input=1.0 -> output=1.0
/// </summary>
public sealed class SoftCurve : IResponseCurve
{
    public static readonly SoftCurve Instance = new();

    private const float Exponent = 1.5f;

    public float Evaluate(float input) => MathF.Pow(input, Exponent);
}
