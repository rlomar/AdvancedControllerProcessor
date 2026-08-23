using System.IO;
using AdvancedControllerProcessor.Helpers;
using AdvancedControllerProcessor.Models;
using AdvancedControllerProcessor.Services;

namespace AdvancedControllerProcessor.ViewModels;

/// <summary>
/// Top-level ViewModel for the application. Owns all child VMs and services.
/// This is the central coordinator between UI, input, processing, and output.
/// </summary>
public sealed class MainViewModel : ViewModelBase, IDisposable
{
    private readonly IControllerService _controllerService;
    private IVirtualControllerService _virtualService;
    private readonly InputProcessingService _processingService;
    private readonly ProfileService _profileService;
    private readonly ConfigurationService _configService;

    private Profile _currentProfile = Profile.Default();
    private bool _isProcessingEnabled;
    private bool _isSafeMode;
    private string _selectedProfileName = "Default";
    private string _statusMessage = "Initializing...";
    private List<string> _availableProfiles = [];
    private VirtualControllerType _virtualType;
    private int _selectedPollingRate = PollingRate.Default;
    private int _measuredPollingRate = PollingRate.Default;

    // Thread dispatcher for UI updates
    private readonly SynchronizationContext? _syncContext;
    private DateTime _lastUiUpdate = DateTime.MinValue;
    private static readonly TimeSpan UiUpdateInterval = TimeSpan.FromMilliseconds(33); // ~30 FPS

    public MainViewModel()
    {
        _syncContext = SynchronizationContext.Current;

        // Initialize services
        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        _configService = new ConfigurationService(appDir);
        _profileService = new ProfileService(Path.Combine(appDir, "Profiles"));

        _processingService = new InputProcessingService();
        _controllerService = new DualSenseControllerService();
        _virtualType = _configService.Settings.VirtualControllerType;
        _virtualService = CreateVirtualService(_virtualType);

        // Restore saved polling rate before the input loop starts
        _selectedPollingRate = PollingRate.Clamp(_configService.Settings.PollingRateHz);
        _measuredPollingRate = _selectedPollingRate;
        _controllerService.PollingRateHz = _selectedPollingRate;

        // Initialize child ViewModels
        Controller = new ControllerViewModel();
        LeftStick = new LeftStickViewModel();
        RightStick = new RightStickViewModel();

        // Wire up auto-sync: when settings change, update the processing pipeline immediately
        LeftStick.OnChanged = OnStickSettingsChanged;
        RightStick.OnChanged = OnStickSettingsChanged;

        // Wire up events
        _controllerService.StateChanged += OnControllerStateChanged;
        _controllerService.ConnectionChanged += OnConnectionChanged;
        _controllerService.MeasuredRateChanged += OnMeasuredRateChanged;
        _virtualService.ControllerCreated += OnVirtualCreated;
        _virtualService.ControllerRemoved += OnVirtualRemoved;

        // Load last profile
        AvailableProfiles = _profileService.ListProfiles();
        string lastProfile = _configService.Settings.LastProfile;
        if (AvailableProfiles.Contains(lastProfile))
            LoadProfile(lastProfile);
        else
            LoadProfile("Default");

        StatusMessage = "Ready. Connect a DualSense controller.";
    }

    // ── Child ViewModels ──────────────────────────────────

    public ControllerViewModel Controller { get; }
    public LeftStickViewModel LeftStick { get; }
    public RightStickViewModel RightStick { get; }

    // ── Profile ───────────────────────────────────────────

    public string SelectedProfileName
    {
        get => _selectedProfileName;
        set
        {
            if (SetProperty(ref _selectedProfileName, value) && !string.IsNullOrEmpty(value))
                LoadProfile(value);
        }
    }

    public List<string> AvailableProfiles
    {
        get => _availableProfiles;
        set => SetProperty(ref _availableProfiles, value);
    }

    // ── Processing State ──────────────────────────────────

    public bool IsProcessingEnabled
    {
        get => _isProcessingEnabled;
        set
        {
            if (SetProperty(ref _isProcessingEnabled, value))
            {
                _processingService.ProcessingEnabled = value;
                _processingService.ResetSmoothing();
                StatusMessage = value ? "Processing: ON" : "Processing: OFF (pass-through)";
            }
        }
    }

    public bool IsSafeMode
    {
        get => _isSafeMode;
        set => SetProperty(ref _isSafeMode, value);
    }

    // ── Virtual Controller Type ───────────────────────────

    /// <summary>Display name of the currently selected virtual controller type.</summary>
    public string VirtualTypeName => _virtualType switch
    {
        VirtualControllerType.DualShock4 => "DualShock 4",
        _ => "Xbox 360"
    };

    /// <summary>
    /// Type of the virtual controller exposed to games.
    /// Switching swaps the service and recreates the virtual pad
    /// immediately if a controller is currently connected.
    /// </summary>
    public VirtualControllerType VirtualType
    {
        get => _virtualType;
        set
        {
            if (!SetProperty(ref _virtualType, value))
                return;

            OnPropertyChanged(nameof(VirtualTypeName));
            _configService.Update(s => s.VirtualControllerType = value);

            bool recreate = _controllerService.IsConnected && _virtualService.IsActive;

            _virtualService.ControllerCreated -= OnVirtualCreated;
            _virtualService.ControllerRemoved -= OnVirtualRemoved;
            _virtualService.Dispose();

            _virtualService = CreateVirtualService(value);
            _virtualService.ControllerCreated += OnVirtualCreated;
            _virtualService.ControllerRemoved += OnVirtualRemoved;

            if (recreate)
                _virtualService.Create();

            Controller.UpdateVirtual(_virtualService.IsActive);
            StatusMessage = $"Virtual controller: {VirtualTypeName}";
            Logging.Info($"[Main] Virtual controller type switched to {value}");
        }
    }

    private static IVirtualControllerService CreateVirtualService(VirtualControllerType type) => type switch
    {
        VirtualControllerType.DualShock4 => new VirtualDualShock4Service(),
        _ => new VirtualXboxControllerService()
    };

    // ── Polling Rate ──────────────────────────────────────

    /// <summary>Preset rates offered in the UI.</summary>
    public int[] PollingRateOptions => PollingRate.Presets;

    /// <summary>
    /// Target virtual-pad submission rate in Hz. Applied live and saved immediately.
    /// </summary>
    public int SelectedPollingRate
    {
        get => _selectedPollingRate;
        set
        {
            int clamped = PollingRate.Clamp(value);
            if (!SetProperty(ref _selectedPollingRate, clamped))
                return;

            _controllerService.PollingRateHz = clamped;
            _configService.Update(s => s.PollingRateHz = clamped);
            OnPropertyChanged(nameof(PollingRateStatus));
            StatusMessage = $"Virtual pad rate: {clamped} Hz";
        }
    }

    /// <summary>Measured virtual-pad update rate, reported ~2x/second.</summary>
    public int MeasuredPollingRate
    {
        get => _measuredPollingRate;
        private set
        {
            if (SetProperty(ref _measuredPollingRate, value))
                OnPropertyChanged(nameof(MeasuredRateText));
        }
    }

    public string MeasuredRateText => $"{MeasuredPollingRate} Hz";

    public string PollingRateStatus => $"Target: {SelectedPollingRate} Hz · Measured: {MeasuredPollingRate} Hz";

    private void OnMeasuredRateChanged(int hz)
    {
        void UpdateUi() => MeasuredPollingRate = hz;

        if (_syncContext is not null)
            _syncContext.Post(_ => UpdateUi(), null);
        else
            UpdateUi();
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    // ── Profile Management ────────────────────────────────

    public void LoadProfile(string profileName)
    {
        var profile = _profileService.Load(profileName);
        _currentProfile = profile;
        _processingService.CurrentProfile = profile;
        _processingService.ResetSmoothing();

        LeftStick.LoadFrom(profile.LeftStick);
        RightStick.LoadFrom(profile.RightStick);

        SelectedProfileName = profile.Name;
        IsSafeMode = false;
        StatusMessage = $"Profile loaded: {profile.Name}";
    }

    public void SaveCurrentProfile()
    {
        _currentProfile.LeftStick = LeftStick.ToSettings();
        _currentProfile.RightStick = RightStick.ToSettings();
        _profileService.Save(_currentProfile);
        StatusMessage = $"Profile saved: {_currentProfile.Name}";
    }

    public void ApplyCurrentSettings()
    {
        _currentProfile.LeftStick = LeftStick.ToSettings();
        _currentProfile.RightStick = RightStick.ToSettings();
        _processingService.CurrentProfile = _currentProfile;
        _processingService.ResetSmoothing();
    }

    public void ResetToSafeMode()
    {
        _currentProfile = Profile.Default();
        _processingService.CurrentProfile = _currentProfile;
        _processingService.ResetSmoothing();

        LeftStick.LoadFrom(_currentProfile.LeftStick);
        RightStick.LoadFrom(_currentProfile.RightStick);

        IsProcessingEnabled = false;
        IsSafeMode = true;
        StatusMessage = "SAFE MODE: All settings reset to defaults";
    }

    public void RefreshProfileList()
    {
        AvailableProfiles = _profileService.ListProfiles();
    }

    // ── Controller Lifecycle ──────────────────────────────

    /// <summary>
    /// Called automatically when any stick setting changes in the UI.
    /// Pushes the new settings into the processing pipeline immediately.
    /// </summary>
    private void OnStickSettingsChanged()
    {
        _currentProfile.LeftStick = LeftStick.ToSettings();
        _currentProfile.RightStick = RightStick.ToSettings();
        _processingService.CurrentProfile = _currentProfile;
    }

    public void StartController()
    {
        _controllerService.Start();
        StatusMessage = "Searching for DualSense controller...";
    }

    public void StopController()
    {
        _controllerService.Stop();
        _virtualService.Remove();
        StatusMessage = "Controller stopped";
    }

    public void ToggleProcessing()
    {
        IsProcessingEnabled = !IsProcessingEnabled;
    }

    public void ToggleSafeMode()
    {
        if (IsSafeMode)
        {
            IsSafeMode = false;
            StatusMessage = "Safe mode deactivated";
        }
        else
        {
            ResetToSafeMode();
        }
    }

    // ── Event Handlers ────────────────────────────────────

    private void OnControllerStateChanged(ControllerState rawState)
    {
        // Process input through pipeline
        ControllerState processedState = _processingService.Process(rawState);

        // Send to virtual controller
        _virtualService.SubmitState(processedState);

        // Update UI at limited rate (~30 FPS)
        var now = DateTime.UtcNow;
        if (now - _lastUiUpdate < UiUpdateInterval)
            return;

        _lastUiUpdate = now;

        // Marshal UI updates to the UI thread
        void UpdateUi()
        {
            LeftStick.UpdateLiveData(
                rawState.LeftStick.X, rawState.LeftStick.Y,
                processedState.LeftStick.X, processedState.LeftStick.Y);

            RightStick.UpdateLiveData(
                rawState.RightStick.X, rawState.RightStick.Y,
                processedState.RightStick.X, processedState.RightStick.Y);
        }

        if (_syncContext is not null)
            _syncContext.Post(_ => UpdateUi(), null);
        else
            UpdateUi();
    }

    private void OnConnectionChanged(bool connected)
    {
        void UpdateUi()
        {
            Controller.UpdateConnection(
                connected,
                connected ? _controllerService.ConnectionType : ConnectionType.Unknown,
                connected ? "DualSense" : "No controller");

            if (connected)
            {
                // Create virtual controller when physical is detected
                bool virtualCreated = _virtualService.Create();
                Controller.UpdateVirtual(virtualCreated);

                if (_configService.Settings.AutoStartProcessing)
                    IsProcessingEnabled = true;

                StatusMessage = $"DualSense connected. Virtual {VirtualTypeName} active.";
            }
            else
            {
                _virtualService.Remove();
                Controller.UpdateVirtual(false);
                StatusMessage = "DualSense disconnected. Waiting for controller...";
            }
        }

        if (_syncContext is not null)
            _syncContext.Post(_ => UpdateUi(), null);
        else
            UpdateUi();
    }

    private void OnVirtualCreated()
    {
        void UpdateUi() => Controller.UpdateVirtual(true);
        if (_syncContext is not null)
            _syncContext.Post(_ => UpdateUi(), null);
    }

    private void OnVirtualRemoved()
    {
        void UpdateUi() => Controller.UpdateVirtual(false);
        if (_syncContext is not null)
            _syncContext.Post(_ => UpdateUi(), null);
    }

    public void Dispose()
    {
        _controllerService.StateChanged -= OnControllerStateChanged;
        _controllerService.ConnectionChanged -= OnConnectionChanged;
        _controllerService.MeasuredRateChanged -= OnMeasuredRateChanged;
        _virtualService.ControllerCreated -= OnVirtualCreated;
        _virtualService.ControllerRemoved -= OnVirtualRemoved;

        _controllerService.Stop();
        _virtualService.Remove();
        _configService.Save();
    }
}
