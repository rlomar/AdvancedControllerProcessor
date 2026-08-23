namespace AdvancedControllerProcessor.Processing;

/// <summary>
/// Interface for response curves that map input intensity to output intensity.
/// All curves map [0.0, 1.0] → [0.0, 1.0].
///
/// Input is always the absolute value of the axis.
/// The sign is re-applied after the curve evaluation.
///
/// Implementations:
///   - LinearCurve:       output = input (identity)
///   - SoftCurve:         gentle acceleration, more precision near center
///   - AggressiveCurve:   fast acceleration, less precision near center
///   - CustomCurve:       user-defined piecewise-linear interpolation
/// </summary>
public interface IResponseCurve
{
    /// <summary>
    /// Evaluate the curve at the given input value.
    /// </summary>
    /// <param name="input">Absolute input value in [0.0, 1.0].</param>
    /// <returns>Output value in [0.0, 1.0].</returns>
    float Evaluate(float input);
}
