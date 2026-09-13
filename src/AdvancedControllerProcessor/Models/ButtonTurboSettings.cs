using AdvancedControllerProcessor.Models;

namespace AdvancedControllerProcessor.Models;

/// <summary>
/// Rapid-fire (turbo) configuration for face and shoulder buttons.
///
/// This is real pipeline functionality: when enabled and processing is ON,
/// each held turbo button is re-pressed at the configured rate at the virtual
/// controller — a true square-wave oscillator applied in
/// <see cref="Services.InputProcessingService.Process"/>, not a visual effect.
/// </summary>
public sealed class ButtonTurboSettings
{
    /// <summary>Master switch. Turbo does nothing until this is on.</summary>
    public bool TurboEnabled { get; set; }

    /// <summary>
    /// Milliseconds between consecutive state flips (press → release → press).
    /// Anything below the engine floor (~1 ms — the fastest the virtual pad
    /// reports) is honored only as far as the pad allows. Default 8 ms (≈125 presses/s).
    /// </summary>
    public double TurboIntervalMs { get; set; } = 8.0;

    public bool TurboA { get; set; }
    public bool TurboB { get; set; }
    public bool TurboX { get; set; }
    public bool TurboY { get; set; }
    public bool TurboLB { get; set; }
    public bool TurboRB { get; set; }

    /// <summary>Whether the given button is assigned to turbo.</summary>
    public bool IsButtonTurbo(GamepadButton button) => button switch
    {
        GamepadButton.A => TurboA,
        GamepadButton.B => TurboB,
        GamepadButton.X => TurboX,
        GamepadButton.Y => TurboY,
        GamepadButton.LeftShoulder => TurboLB,
        GamepadButton.RightShoulder => TurboRB,
        _ => false
    };

    /// <summary>Default configuration: turbo disabled, 8 ms gap (≈125 presses/sec).</summary>
    public static ButtonTurboSettings Default() => new();
}