using AdvancedControllerProcessor.Models;

namespace AdvancedControllerProcessor.Services;

/// <summary>
/// Interface for the processing pipeline that transforms raw controller state.
/// </summary>
public interface IInputProcessingService
{
    /// <summary>
    /// Whether processing is enabled. When disabled, raw input passes through unmodified.
    /// Toggled by F8 hotkey.
    /// </summary>
    bool ProcessingEnabled { get; set; }

    /// <summary>Current profile being used for processing.</summary>
    Profile CurrentProfile { get; set; }

    /// <summary>
    /// Process a raw controller state through the full pipeline.
    /// Returns the processed state ready for the virtual controller.
    /// </summary>
    ControllerState Process(ControllerState rawInput);

    /// <summary>
    /// Reset smoothing state. Call when switching profiles or entering safe mode.
    /// </summary>
    void ResetSmoothing();
}
