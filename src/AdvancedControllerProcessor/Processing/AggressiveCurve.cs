namespace AdvancedControllerProcessor.Processing;

/// <summary>
/// Aggressive response curve: output = input^0.7
/// Provides faster acceleration near the center (small movements become larger).
/// Useful when you want quicker stick response.
///
/// Mathematical properties:
///   input=0.0 -> output=0.0
///   input=0.5 -> output=0.616 (faster response)
///   input=1.0 -> output=1.0
/// </summary>
public sealed class AggressiveCurve : IResponseCurve
{
    public static readonly AggressiveCurve Instance = new();

    private const float Exponent = 0.7f;

    public float Evaluate(float input) => MathF.Pow(input, Exponent);
}
