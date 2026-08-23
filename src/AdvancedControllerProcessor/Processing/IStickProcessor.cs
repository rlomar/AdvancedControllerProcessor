using AdvancedControllerProcessor.Models;

namespace AdvancedControllerProcessor.Processing;

/// <summary>
/// Interface for all stick processors in the processing pipeline.
/// Each processor takes a single axis value and returns the transformed value.
///
/// Processing order matters for mathematical correctness:
///   Deadzone → Curve → Speed → DirectionalSpeed → Smoothing → Clamp
/// </summary>
public interface IStickProcessor
{
    /// <summary>
    /// Process a single axis value.
    /// </summary>
    /// <param name="input">Input value in [-1.0, +1.0].</param>
    /// <param name="settings">Current processing settings.</param>
    /// <returns>Processed value. Will be clamped to [-1.0, +1.0] by ClampProcessor at end of pipeline.</returns>
    float Process(float input, ProcessingSettings settings);
}
