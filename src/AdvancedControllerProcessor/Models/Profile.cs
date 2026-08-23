using Newtonsoft.Json;

namespace AdvancedControllerProcessor.Models;

/// <summary>
/// Complete controller processing profile.
/// Contains all settings for left stick, right stick, and optional features.
/// Serialized to/from JSON for persistence.
/// </summary>
public sealed class Profile
{
    /// <summary>Display name of this profile (e.g., "Rocket League").</summary>
    public string Name { get; set; } = "Default";

    /// <summary>Optional description for this profile.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Left stick processing settings. Always active when processing is enabled.</summary>
    public ProcessingSettings LeftStick { get; set; } = ProcessingSettings.PassThrough();

    /// <summary>Right stick settings. Default: pass-through.</summary>
    public RightStickSettings RightStick { get; set; } = RightStickSettings.Default();

    /// <summary>Whether trigger values are processed (future feature).</summary>
    public bool TriggerProcessingEnabled { get; set; }

    /// <summary>
    /// Optional: process name to auto-activate this profile when the game is running.
    /// Empty string = no auto-profile.
    /// Example: "RocketLeague"
    /// </summary>
    public string AutoProfileProcessName { get; set; } = string.Empty;

    // ── Presets ───────────────────────────────────────────────

    /// <summary>
    /// Creates the default empty profile (all pass-through).
    /// </summary>
    public static Profile Default() => new()
    {
        Name = "Default",
        Description = "Default profile with no processing applied",
        LeftStick = ProcessingSettings.PassThrough(),
        RightStick = RightStickSettings.Default(),
        TriggerProcessingEnabled = false
    };

    /// <summary>
    /// Rocket League baseline profile. Minimal deadzone, no speed boost.
    /// This is a starting point for experimentation, not an optimized config.
    /// </summary>
    public static Profile RocketLeague() => new()
    {
        Name = "Rocket League",
        Description = "Baseline profile for Rocket League — 3% deadzone, linear curve, no speed boost",
        LeftStick = new ProcessingSettings
        {
            DeadzoneEnabled = true,
            Deadzone = 0.03f,
            DeadzoneType = "Radial",
            ResponseCurve = "Linear",
            XSpeedMultiplier = 1.0f,
            YSpeedMultiplier = 1.0f,
            DirectionalSpeedEnabled = false,
            ForwardSpeed = 1.0f,
            BackwardSpeed = 1.0f,
            LeftSpeed = 1.0f,
            RightSpeed = 1.0f,
            SmoothingEnabled = false,
            SmoothingAmount = 0f
        },
        RightStick = RightStickSettings.Default(),
        TriggerProcessingEnabled = false,
        AutoProfileProcessName = "RocketLeague"
    };

    /// <summary>
    /// Creates a deep clone of this profile for safe editing.
    /// </summary>
    public Profile Clone()
    {
        var json = JsonConvert.SerializeObject(this);
        return JsonConvert.DeserializeObject<Profile>(json) ?? Default();
    }
}
