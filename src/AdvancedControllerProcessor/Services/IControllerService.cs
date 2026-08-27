using AdvancedControllerProcessor.Models;

namespace AdvancedControllerProcessor.Services;

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

    /// <summary>Start reading input. Non-blocking, runs on background thread.</summary>
    void Start();

    /// <summary>Stop reading input.</summary>
    void Stop();
}
