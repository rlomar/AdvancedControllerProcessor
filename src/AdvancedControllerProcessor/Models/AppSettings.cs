using Newtonsoft.Json;

namespace AdvancedControllerProcessor.Models;

/// <summary>
/// Application-level configuration. Saved separately from profiles.
/// Contains UI preferences, hotkeys, and startup options.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Name of the last used profile file.</summary>
    public string LastProfile { get; set; } = "Default";

    /// <summary>Start the application when Windows starts.</summary>
    public bool StartWithWindows { get; set; }

    /// <summary>Minimize to system tray instead of closing.</summary>
    public bool MinimizeToTray { get; set; } = true;

    /// <summary>Start processing automatically when a controller is detected.</summary>
    public bool AutoStartProcessing { get; set; } = true;

    /// <summary>Hotkey to toggle processing ON/OFF. Default: F8.</summary>
    public int HotkeyToggleProcessing { get; set; } = 119; // Virtual key code for F8

    /// <summary>Hotkey for safe mode reset. Default: F9.</summary>
    public int HotkeySafeMode { get; set; } = 120; // Virtual key code for F9

    /// <summary>Window position X. -1 = centered.</summary>
    public double WindowLeft { get; set; } = -1;

    /// <summary>Window position Y. -1 = centered.</summary>
    public double WindowTop { get; set; } = -1;

    /// <summary>Window width.</summary>
    public double WindowWidth { get; set; } = 900;

    /// <summary>Window height.</summary>
    public double WindowHeight { get; set; } = 650;

    /// <summary>Is the window maximized.</summary>
    public bool WindowMaximized { get; set; }

    /// <summary>
    /// Device path of the selected DualSense controller.
    /// Empty = auto-select first controller found.
    /// </summary>
    public string SelectedControllerDevicePath { get; set; } = string.Empty;

    /// <summary>Enable HidHide to hide the physical controller from games.</summary>
    public bool EnableHidHide { get; set; }

    /// <summary>Type of the virtual controller created for games.</summary>
    public VirtualControllerType VirtualControllerType { get; set; } = VirtualControllerType.Xbox360;

    /// <summary>
    /// Target virtual-pad submission rate in Hz (250–1000).
    /// Raw HID reading always runs at native hardware speed; this gates how
    /// often processed states reach the virtual controller.
    /// </summary>
    public int PollingRateHz { get; set; } = Services.PollingRate.Default;

    /// <summary>
    /// Activated license key (normalized form). Empty until the user has
    /// activated. Only the key is stored — validation always happens online.
    /// </summary>
    public string LicenseKey { get; set; } = string.Empty;

    /// <summary>Log level: "None", "Error", "Info".</summary>
    public string LogLevel { get; set; } = "Info";

    /// <summary>
    /// Creates default application settings.
    /// </summary>
    public static AppSettings Default() => new();
}
