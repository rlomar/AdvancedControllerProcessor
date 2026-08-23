using AdvancedControllerProcessor.Models;

namespace AdvancedControllerProcessor.ViewModels;

/// <summary>
/// ViewModel for the controller status display on the Dashboard.
/// </summary>
public sealed class ControllerViewModel : ViewModelBase
{
    private bool _isPhysicalConnected;
    private bool _isVirtualActive;
    private string _controllerName = "No controller";
    private ConnectionType _connectionType = ConnectionType.Unknown;
    private string _statusText = "Disconnected";

    public bool IsPhysicalConnected
    {
        get => _isPhysicalConnected;
        set => SetProperty(ref _isPhysicalConnected, value);
    }

    public bool IsVirtualActive
    {
        get => _isVirtualActive;
        set => SetProperty(ref _isVirtualActive, value);
    }

    public string ControllerName
    {
        get => _controllerName;
        set => SetProperty(ref _controllerName, value);
    }

    public ConnectionType ConnectionType
    {
        get => _connectionType;
        set
        {
            if (SetProperty(ref _connectionType, value))
                OnPropertyChanged(nameof(ConnectionTypeText));
        }
    }

    public string ConnectionTypeText => _connectionType switch
    {
        ConnectionType.USB => "USB",
        ConnectionType.Bluetooth => "Bluetooth",
        _ => "Unknown"
    };

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public string PhysicalStatusIcon => IsPhysicalConnected ? "\u25CF" : "\u25CB"; // Filled/empty circle
    public string PhysicalStatusColor => IsPhysicalConnected ? "#22C55E" : "#EF4444"; // Green/Red
    public string VirtualStatusIcon => IsVirtualActive ? "\u25CF" : "\u25CB";
    public string VirtualStatusColor => IsVirtualActive ? "#22C55E" : "#EF4444";

    public void UpdateConnection(bool connected, ConnectionType type, string name)
    {
        IsPhysicalConnected = connected;
        ConnectionType = type;
        ControllerName = connected ? name : "No controller";
        StatusText = connected ? $"Connected ({ConnectionTypeText})" : "Disconnected";
        OnPropertyChanged(nameof(PhysicalStatusIcon));
        OnPropertyChanged(nameof(PhysicalStatusColor));
    }

    public void UpdateVirtual(bool active)
    {
        IsVirtualActive = active;
        OnPropertyChanged(nameof(VirtualStatusIcon));
        OnPropertyChanged(nameof(VirtualStatusColor));
    }
}
