using AdvancedControllerProcessor.Models;

namespace AdvancedControllerProcessor.Services;

/// <summary>
/// Valid range for the virtual-controller update rate in Hz.
/// Raw HID reports always arrive at the hardware's native rate (~1000 Hz on USB);
/// these limits bound how often processed states are submitted to the virtual pad.
/// </summary>
public static class PollingRate
{
    public const int Min = 125;
    public const int Max = 1000;
    public const int Default = Max;

    /// <summary>Preset values offered in the UI.</summary>
    public static readonly int[] Presets = [250, 500, 1000];

    public static int Clamp(int hz) => Math.Clamp(hz, Min, Max);
}

/// <summary>
/// Interface for reading physical controller input.
/// Abstraction allows testing with mock controllers.
/// </summary>
public interface IControllerService : IDisposable
{
    /// <summary>Fired when controller state is read. Called on input thread.</summary>
    event Action<ControllerState>? StateChanged;

    /// <summary>Fired when controller connection status changes.</summary>
    event Action<bool>? ConnectionChanged;

    /// <summary>
    /// Fired roughly twice per second with the MEASURED rate (Hz) at which
    /// controller states are being submitted to the virtual pad.
    /// Called on input thread.
    /// </summary>
    event Action<int>? MeasuredRateChanged;

    /// <summary>Whether a controller is currently connected and readable.</summary>
    bool IsConnected { get; }

    /// <summary>Currently detected connection type.</summary>
    ConnectionType ConnectionType { get; }

    /// <summary>
    /// Target submission rate to the virtual pad in Hz. Applied live,
    /// even while the input loop is running.
    /// </summary>
    int PollingRateHz { get; set; }

    /// <summary>Start reading input. Non-blocking, runs on background thread.</summary>
    void Start();

    /// <summary>Stop reading input.</summary>
    void Stop();
}
