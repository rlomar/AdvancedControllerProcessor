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
    private int _selectedPollingRate = PollingRate.Default;
    private int _measuredPollingRate = PollingRate.Default;
    private int _measuredRawRate;

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
        _controllerService.RawRateChanged += OnRawRateChanged;
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
            OnPropertyChanged(nameof(PollingRateStatus));
            OnPropertyChanged(nameof(RateHint));
        }
    }

    public string MeasuredRateText => $"{MeasuredPollingRate} Hz";

    /// <summary>Raw hardware HID report rate (DualSense USB native ≈ 250 Hz).</summary>
    public int MeasuredRawRate
    {
        get => _measuredRawRate;
        private set
        {
            if (SetProperty(ref _measuredRawRate, value))
                OnPropertyChanged(nameof(HardwareRateText));
            OnPropertyChanged(nameof(PollingRateStatus));
            OnPropertyChanged(nameof(RateHint));
        }
    }

    public string HardwareRateText => MeasuredRawRate > 0
        ? $"{MeasuredRawRate} Hz"
        : "—";

    public string PollingRateStatus => $"Target: {SelectedPollingRate} Hz · Pad: {MeasuredPollingRate} Hz · Hardware: {HardwareRateText}";

    /// <summary>
    /// Explains any gap between target and achievable rate. The DualSense sends
    /// ~250 reports/sec over USB natively; higher targets engage automatically
    /// when the host USB polling interval is overclocked (e.g. hidusbf).
    /// </summary>
    public string RateHint
    {
        get
        {
            if (MeasuredRawRate == 0)
                return string.Empty;

            if (MeasuredRawRate + 5 < SelectedPollingRate)
                return $"DualSense hardware sends ~{MeasuredRawRate} reports/sec — every one reaches the pad at full speed. " +
                       $"Higher target engages automatically if the USB interval is overclocked (hidusbf).";
            return "Running event-driven at full hardware speed.";
        }
    }

    private void OnMeasuredRateChanged(int hz)
    {
        void UpdateUi() => MeasuredPollingRate = hz;

        if (_syncContext is not null)
            _syncContext.Post(_ => UpdateUi(), null);
        else
            UpdateUi();
    }

    private void OnRawRateChanged(int hz)
    {
        void UpdateUi() => MeasuredRawRate = hz;

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

    public string PipelineAvgText { get; private set; } = "—";
    public string PipelineMaxText { get; private set; } = "—";
    public string PipelineP95Text { get; private set; } = "— ";
    public string WaitAvgText { get; private set; } = "—";
    public string WaitMaxText { get; private set; } = "—";
    public string WaitP95Text { get; private set; } = "— ";
    public string WaitModeText { get; private set; } = string.Empty;
    public string LatencyHealthText { get; private set; } = "idle";

    public System.Windows.Media.Brush LatencyHealthBrush { get; private set; } =
        System.Windows.Media.Brushes.Gray;

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
        _controllerService.RawRateChanged -= OnRawRateChanged;
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
