using AdvancedControllerProcessor.Models;

namespace AdvancedControllerProcessor.Models;

/// <summary>
/// Rapid-fire (turbo) configuration for face and shoulder buttons plus the
/// L2/R2 analog triggers.
///
/// This is real pipeline functionality: when enabled and processing is ON,
/// each held turbo control is re-pressed at the configured rate at the virtual
/// controller — a true square-wave oscillator applied in
/// <see cref="Services.InputProcessingService.Process"/>, not a visual effect.
/// Buttons toggle their digital bit; L2/R2 toggle their analog value
/// (held pull ↔ 0).
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
    public bool TurboL2 { get; set; }
    public bool TurboR2 { get; set; }

    /// <summary>Whether the given digital button is assigned to turbo.</summary>
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

    /// <summary>
    /// Whether the given analog trigger (0 = L2, 1 = R2) is assigned to turbo.
    /// </summary>
    public bool IsTriggerTurbo(int triggerIndex) =>
        triggerIndex == 0 ? TurboL2 : triggerIndex == 1 ? TurboR2 : false;

    /// <summary>Default configuration: turbo disabled, 8 ms gap (≈125 presses/sec).</summary>
    public static ButtonTurboSettings Default() => new();
}