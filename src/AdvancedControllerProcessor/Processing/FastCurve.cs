namespace AdvancedControllerProcessor.Processing;

/// <summary>
/// Fast response curve: output = input^0.5 (square-root).
/// Strongest acceleration offered: small movements become much larger,
/// maximum perceived lightness/speed near the center.
///
/// Mathematical properties:
///   input=0.0 -> output=0.0
///   input=0.25 -> output=0.5
///   input=0.5 -> output=0.707 (much faster than raw)
///   input=1.0 -> output=1.0
///
/// Trade-off: fine precision near center is reduced — tiny stick movements
/// translate into big in-game movement. Best paired with a small deadzone
/// if the controller has drift.
/// </summary>
public sealed class FastCurve : IResponseCurve
{
    public static readonly FastCurve Instance = new();

    private const float Exponent = 0.5f;

    public float Evaluate(float input) => MathF.Pow(input, Exponent);
}
