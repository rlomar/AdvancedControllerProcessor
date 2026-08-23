namespace AdvancedControllerProcessor.Models;

/// <summary>
/// Type of virtual controller exposed to games via ViGEmBus.
/// </summary>
public enum VirtualControllerType
{
    /// <summary>
    /// Xbox 360 pad (XInput). Best compatibility with Windows games.
    /// </summary>
    Xbox360,

    /// <summary>
    /// DualShock 4 pad. Native PlayStation-style input including
    /// PS and Touchpad buttons, but games must support DS4/DirectInput.
    /// </summary>
    DualShock4
}
