using AdvancedControllerProcessor.Models;

namespace AdvancedControllerProcessor.Services;

/// <summary>
/// Interface for writing to a virtual game controller.
/// </summary>
public interface IVirtualControllerService : IDisposable
{
    /// <summary>Fired when the virtual controller is created/connected.</summary>
    event Action? ControllerCreated;

    /// <summary>Fired when the virtual controller is removed/disconnected.</summary>
    event Action? ControllerRemoved;

    /// <summary>Whether the virtual controller is currently active.</summary>
    bool IsActive { get; }

    /// <summary>Create the virtual controller. Returns true on success.</summary>
    bool Create();

    /// <summary>Remove/disconnect the virtual controller.</summary>
    void Remove();

    /// <summary>
    /// Send a controller state to the virtual controller.
    /// All values must be in valid ranges:
    ///   Sticks: [-1.0, +1.0]
    ///   Triggers: [0.0, 1.0]
    /// </summary>
    void SubmitState(ControllerState state);
}
