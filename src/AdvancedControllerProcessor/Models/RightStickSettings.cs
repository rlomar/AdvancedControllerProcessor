namespace AdvancedControllerProcessor.Models;

/// <summary>
/// Right stick configuration. Separate from left stick settings
/// to allow pass-through (no processing) while left stick is processed.
///
/// Default: ProcessingEnabled = false (pure pass-through).
/// This is critical for games like Rocket League where the right stick
/// controls the camera and should not be modified by speed/curve settings.
/// </summary>
public sealed class RightStickSettings
{
    /// <summary>
    /// When false: right stick is passed through unmodified.
    /// When true: ProcessingSettings are applied to the right stick.
    /// Default: false.
    /// </summary>
    public bool ProcessingEnabled { get; set; }

    /// <summary>
    /// Processing settings for the right stick.
    /// Only used when ProcessingEnabled is true.
    /// When null, defaults to PassThrough.
    /// </summary>
    public ProcessingSettings? Settings { get; set; }

    /// <summary>
    /// Creates default right stick settings (pass-through).
    /// </summary>
    public static RightStickSettings Default() => new()
    {
        ProcessingEnabled = false,
        Settings = null
    };
}
