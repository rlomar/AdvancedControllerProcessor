namespace AdvancedControllerProcessor.Models;

/// <summary>
/// Connection type of the physical DualSense controller.
/// USB and Bluetooth have different HID report layouts.
/// </summary>
public enum ConnectionType
{
    Unknown = 0,
    USB = 1,
    Bluetooth = 2
}
