using System.Runtime.CompilerServices;
using AdvancedControllerProcessor.Models;

namespace AdvancedControllerProcessor.ViewModels;

/// <summary>
/// ViewModel for Left Stick settings and live visualization data.
/// Binds to the Left Stick tab in the UI.
/// </summary>
public sealed class LeftStickViewModel : ViewModelBase
{
    private bool _suppressCallbacks;
    private Action? _onChanged;

    private bool _deadzoneEnabled;
    private float _deadzone;
    private string _deadzoneType = "Radial";
    private string _responseCurve = "Linear";

    private float _xSpeed = 1.0f;
    private float _ySpeed = 1.0f;

    private bool _directionalSpeedEnabled;
    private float _forwardSpeed = 1.0f;
    private float _backwardSpeed = 1.0f;
    private float _leftSpeed = 1.0f;
    private float _rightSpeed = 1.0f;

    private bool _smoothingEnabled;
    private float _smoothingAmount;
    // Live stick data (for visualizer)
    private float _rawX;
    private float _rawY;
    private float _processedX;
    private float _processedY;

    /// <summary>
    /// Callback invoked when any processing setting changes.
    /// Used by MainViewModel to auto-sync to the processing pipeline.
    /// </summary>
    public Action? OnChanged
    {
        get => _onChanged;
        set => _onChanged = value;
    }

    private bool SetSetting<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (_suppressCallbacks)
            return SetProperty(ref field, value, name);

        bool changed = SetProperty(ref field, value, name);
        if (changed)
            _onChanged?.Invoke();
        return changed;
    }

    // ── Deadzone ──────────────────────────────────────────

    public bool DeadzoneEnabled
    {
        get => _deadzoneEnabled;
        set => SetSetting(ref _deadzoneEnabled, value);
    }

    public float Deadzone
    {
        get => _deadzone;
        set => SetSetting(ref _deadzone, Math.Clamp(value, 0f, 0.5f));
    }

    public string DeadzoneType
    {
        get => _deadzoneType;
        set => SetSetting(ref _deadzoneType, value);
    }

    // ── Response Curve ────────────────────────────────────

    public string ResponseCurve
    {
        get => _responseCurve;
        set => SetSetting(ref _responseCurve, value);
    }

    // ── Speed ─────────────────────────────────────────────

    public float XSpeed
    {
        get => _xSpeed;
        set => SetSetting(ref _xSpeed, Math.Clamp(value, 0.1f, 3.0f));
    }

    public float YSpeed
    {
        get => _ySpeed;
        set => SetSetting(ref _ySpeed, Math.Clamp(value, 0.1f, 3.0f));
    }

    // ── Directional Speed ─────────────────────────────────

    public bool DirectionalSpeedEnabled
    {
        get => _directionalSpeedEnabled;
        set => SetSetting(ref _directionalSpeedEnabled, value);
    }

    public float ForwardSpeed
    {
        get => _forwardSpeed;
        set => SetSetting(ref _forwardSpeed, Math.Clamp(value, 0.1f, 3.0f));
    }

    public float BackwardSpeed
    {
        get => _backwardSpeed;
        set => SetSetting(ref _backwardSpeed, Math.Clamp(value, 0.1f, 3.0f));
    }

    public float LeftSpeed
    {
        get => _leftSpeed;
        set => SetSetting(ref _leftSpeed, Math.Clamp(value, 0.1f, 3.0f));
    }

    public float RightSpeed
    {
        get => _rightSpeed;
        set => SetSetting(ref _rightSpeed, Math.Clamp(value, 0.1f, 3.0f));
    }

    // ── Smoothing ─────────────────────────────────────────

    public bool SmoothingEnabled
    {
        get => _smoothingEnabled;
        set => SetSetting(ref _smoothingEnabled, value);
    }

    public float SmoothingAmount
    {
        get => _smoothingAmount;
        set => SetSetting(ref _smoothingAmount, Math.Clamp(value, 0f, 0.95f));
    }

    // ── Live Stick Data ───────────────────────────────────

    public float RawX
    {
        get => _rawX;
        set => SetProperty(ref _rawX, value);
    }

    public float RawY
    {
        get => _rawY;
        set => SetProperty(ref _rawY, value);
    }

    public float ProcessedX
    {
        get => _processedX;
        set => SetProperty(ref _processedX, value);
    }

    public float ProcessedY
    {
        get => _processedY;
        set => SetProperty(ref _processedY, value);
    }

    // ── Methods ───────────────────────────────────────────

    /// <summary>
    /// Load settings from a ProcessingSettings model.
    /// </summary>
    public void LoadFrom(ProcessingSettings settings)
    {
        _suppressCallbacks = true;
        try
        {
            DeadzoneEnabled = settings.DeadzoneEnabled;
            Deadzone = settings.Deadzone;
            DeadzoneType = settings.DeadzoneType;
            ResponseCurve = settings.ResponseCurve;
            XSpeed = settings.XSpeedMultiplier;
            YSpeed = settings.YSpeedMultiplier;
            DirectionalSpeedEnabled = settings.DirectionalSpeedEnabled;
            ForwardSpeed = settings.ForwardSpeed;
            BackwardSpeed = settings.BackwardSpeed;
            LeftSpeed = settings.LeftSpeed;
            RightSpeed = settings.RightSpeed;
            SmoothingEnabled = settings.SmoothingEnabled;
            SmoothingAmount = settings.SmoothingAmount;
        }
        finally
        {
            _suppressCallbacks = false;
        }
    }

    /// <summary>
    /// Save current UI state to a ProcessingSettings model.
    /// </summary>
    public ProcessingSettings ToSettings() => new()
    {
        DeadzoneEnabled = DeadzoneEnabled,
        Deadzone = Deadzone,
        DeadzoneType = DeadzoneType,
        ResponseCurve = ResponseCurve,
        XSpeedMultiplier = XSpeed,
        YSpeedMultiplier = YSpeed,
        DirectionalSpeedEnabled = DirectionalSpeedEnabled,
        ForwardSpeed = ForwardSpeed,
        BackwardSpeed = BackwardSpeed,
        LeftSpeed = LeftSpeed,
        RightSpeed = RightSpeed,
        SmoothingEnabled = SmoothingEnabled,
        SmoothingAmount = SmoothingAmount
    };

    /// <summary>
    /// Update live stick visualization data.
    /// </summary>
    public void UpdateLiveData(float rawX, float rawY, float processedX, float processedY)
    {
        RawX = rawX;
        RawY = rawY;
        ProcessedX = processedX;
        ProcessedY = processedY;
    }
}
