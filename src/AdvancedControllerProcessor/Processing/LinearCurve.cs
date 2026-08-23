namespace AdvancedControllerProcessor.Processing;

/// <summary>
/// Linear response curve: output = input (identity function).
/// No modification to stick response.
/// </summary>
public sealed class LinearCurve : IResponseCurve
{
    public static readonly LinearCurve Instance = new();

    public float Evaluate(float input) => input;
}
