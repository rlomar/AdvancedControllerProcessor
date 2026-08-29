using System.Diagnostics;
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
    private int _measuredPollingRate = 0;

    // License re-validation (mid-session revocation enforcement)
    private readonly LicenseService? _licenseService;
    private System.Windows.Threading.DispatcherTimer? _licenseTimer;
    private bool _licenseChecking;
    private bool _processingBeforeLicensePause;

    // Latency card refresh throttle
    private DateTime _lastLatencyRefresh = DateTime.MinValue;

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

        // Reuse the license service created by the startup gate so the saved
        // key and validation state stay consistent across components.
        _licenseService = App.CurrentLicenseService;

        _processingService = new InputProcessingService();
        _controllerService = new DualSenseControllerService();
        _virtualType = _configService.Settings.VirtualControllerType;
        _virtualService = CreateVirtualService(_virtualType);

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

        StartLicenseWatch();
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

    // ── Performance (opt-in system boosts) ────────────────

    /// <summary>
    /// Opt-in: switch Windows to the High performance power plan while the
    /// app runs. The original plan is restored on exit.
    /// </summary>
    public bool EnableHighPerformancePowerPlan
    {
        get => _configService.Settings.EnableHighPerformancePowerPlan;
        set
        {
            _configService.Update(s => s.EnableHighPerformancePowerPlan = value);
            OnPropertyChanged(nameof(EnableHighPerformancePowerPlan));

            SystemPerformanceBoost.Apply(_configService.Settings);
            StatusMessage = value
                ? "High performance power plan active (original restored on exit)."
                : "Default Windows power plan restored.";
            Logging.Info($"[Perf] High performance power plan set to {value}");
        }
    }

    /// <summary>
    /// Opt-in: force 1 ms global timer resolution while the app runs.
    /// Restored on exit.
    /// </summary>
    public bool EnableHighResolutionTimer
    {
        get => _configService.Settings.EnableHighResolutionTimer;
        set
        {
            _configService.Update(s => s.EnableHighResolutionTimer = value);
            OnPropertyChanged(nameof(EnableHighResolutionTimer));

            SystemPerformanceBoost.Apply(_configService.Settings);
            StatusMessage = value
                ? "1 ms timer resolution active (restored on exit)."
                : "Timer resolution restored to Windows default.";
            Logging.Info($"[Perf] 1 ms timer resolution set to {value}");
        }
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

    // ── Input Rate (observed) ─────────────────────────────

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

    // ── License Re-validation ─────────────────────────────

    /// <summary>
    /// Periodically re-validates the saved license (every 15 minutes) so a
    /// revoked key stops working mid-session. Transient network failures are
    /// ignored — the next cycle retries; definitive failures pause the
    /// controller and re-open the activation window.
    /// </summary>
    private void StartLicenseWatch()
    {
        if (_licenseService is null || _licenseTimer is not null)
            return;

        _licenseTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(15)
        };
        _licenseTimer.Tick += async (_, _) => await LicenseCheckTickAsync();
        _licenseTimer.Start();
    }

    private async Task LicenseCheckTickAsync()
    {
        if (_licenseChecking || _licenseService is null)
            return;

        _licenseChecking = true;
        try
        {
            LicenseStatus status = await _licenseService.ValidateSavedAsync();

            if (status == LicenseStatus.Ok || status.IsTransient())
                return; // valid, or just a network hiccup — next cycle retries

            Logging.Warn($"[License] Mid-session validation failed: {status} — pausing controller");

            _processingBeforeLicensePause = IsProcessingEnabled;
            StopController();
            IsProcessingEnabled = false;
            StatusMessage = "License no longer valid — processing paused";

            var window = new ActivationWindow(_licenseService, ActivationWindow.Describe(status))
            {
                Owner = System.Windows.Application.Current?.MainWindow
            };
            bool reactivated = window.ShowDialog() == true;

            StartController();
            if (_processingBeforeLicensePause)
                IsProcessingEnabled = true;

            StatusMessage = reactivated
                ? "License re-activated — controller resumed"
                : "Running without a valid license — processing off";
        }
        catch (Exception ex)
        {
            Logging.Error(ex, "[License] Mid-session check crashed");
        }
        finally
        {
            _licenseChecking = false;
        }
    }

    // ── Latency Monitor ───────────────────────────────────

    /// <summary>
    /// Refreshes latency display strings at ~2 Hz. Called from the UI timer;
    /// cheap by design (snapshot of pre-aggregated statistics).
    /// </summary>
    public void RefreshLatencyIfNeeded()
    {
        DateTime now = DateTime.UtcNow;
        if (now - _lastLatencyRefresh < TimeSpan.FromMilliseconds(500))
            return;

        _lastLatencyRefresh = now;

        var pipeline = Latency.Pipeline.Snapshot();
        var wait = Latency.Wait.Snapshot();

        PipelineAvgText = pipeline.Count == 0 ? "—" : $"{pipeline.Average:F0} µs";
        PipelineMaxText = pipeline.Count == 0 ? "—" : $"{pipeline.Max:F0} µs";
        PipelineP95Text = pipeline.Count == 0 ? "— " : $"{pipeline.P95:F0} µs";

        WaitAvgText = wait.Count == 0 ? "—" : $"{wait.Average / 1000.0:F2} ms";
        WaitMaxText = wait.Count == 0 ? "—" : $"{wait.Max / 1000.0:F2} ms";
        WaitP95Text = wait.Count == 0 ? "— " : $"{wait.P95 / 1000.0:F2} ms";
        WaitModeText = wait.Count == 0
            ? string.Empty
            : wait.Average < 100
                ? "event-driven (no added delay)"
                : "paced submissions";

        // Health: driven by the worse of the two components.
        double worstUs = Math.Max(pipeline.P95, wait.P95);
        LatencyHealthText = pipeline.Count == 0 && wait.Count == 0
            ? "idle"
            : worstUs <= 500 ? "excellent"
            : worstUs <= 2000 ? "good"
            : worstUs <= 8000 ? "elevated"
            : "high";

        LatencyHealthBrush = ResolveBrush(LatencyHealthText switch
        {
            "excellent" or "idle" => "SuccessBrush",
            "good" => "WarningBrush",
            _ => "DangerBrush"
        });
    }

    private static System.Windows.Media.Brush ResolveBrush(string resourceKey) =>
        System.Windows.Application.Current?.TryFindResource(resourceKey)
            as System.Windows.Media.Brush
        ?? System.Windows.Media.Brushes.Gray;

    private string _pipelineAvgText = "—";
    private string _pipelineMaxText = "—";
    private string _pipelineP95Text = "— ";
    private string _waitAvgText = "—";
    private string _waitMaxText = "—";
    private string _waitP95Text = "— ";
    private string _waitModeText = string.Empty;
    private string _latencyHealthText = "idle";
    private System.Windows.Media.Brush _latencyHealthBrush = System.Windows.Media.Brushes.Gray;

    public string PipelineAvgText { get => _pipelineAvgText; private set => SetProperty(ref _pipelineAvgText, value); }
    public string PipelineMaxText { get => _pipelineMaxText; private set => SetProperty(ref _pipelineMaxText, value); }
    public string PipelineP95Text { get => _pipelineP95Text; private set => SetProperty(ref _pipelineP95Text, value); }
    public string WaitAvgText { get => _waitAvgText; private set => SetProperty(ref _waitAvgText, value); }
    public string WaitMaxText { get => _waitMaxText; private set => SetProperty(ref _waitMaxText, value); }
    public string WaitP95Text { get => _waitP95Text; private set => SetProperty(ref _waitP95Text, value); }
    public string WaitModeText { get => _waitModeText; private set => SetProperty(ref _waitModeText, value); }
    public string LatencyHealthText { get => _latencyHealthText; private set => SetProperty(ref _latencyHealthText, value); }

    public System.Windows.Media.Brush LatencyHealthBrush
    {
        get => _latencyHealthBrush;
        private set => SetProperty(ref _latencyHealthBrush, value);
    }

    // ── Event Handlers ────────────────────────────────────

    private void OnControllerStateChanged(ControllerState rawState)
    {
        long pipelineStart = Stopwatch.GetTimestamp();

        // Process input through pipeline
        ControllerState processedState = _processingService.Process(rawState);

        // Send to virtual controller
        _virtualService.SubmitState(processedState);

        // Record end-to-end software latency (process + bus submission)
        Latency.Pipeline.Record((long)((Stopwatch.GetTimestamp() - pipelineStart) * 1_000_000
                                       / (double)Stopwatch.Frequency));

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
        _licenseTimer?.Stop();

        // Persist the live UI state into the active profile so tuning survives
        // restarts. Without this, every edit is lost on exit and the next
        // launch reloads the stale on-disk profile (feels like "reset to
        // default"). Manual Save still works exactly as before.
        try
        {
            _currentProfile.LeftStick = LeftStick.ToSettings();
            _currentProfile.RightStick = RightStick.ToSettings();
            _profileService.Save(_currentProfile);
        }
        catch (Exception ex)
        {
            Logging.Error(ex, "Failed to auto-save profile on exit");
        }

        _configService.Save();
    }
}
