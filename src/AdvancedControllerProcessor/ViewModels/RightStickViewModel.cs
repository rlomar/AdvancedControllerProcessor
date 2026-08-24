using System.Runtime.CompilerServices;
using AdvancedControllerProcessor.Models;

namespace AdvancedControllerProcessor.ViewModels;

/// <summary>
/// ViewModel for Right Stick settings and live visualization data.
/// Default: ProcessingEnabled = false (pass-through).
///
/// When processing is enabled, the full stick pipeline applies using the
/// nested <see cref="Processing"/> settings (deadzone, curve, speed, ...).
/// </summary>
public sealed class RightStickViewModel : ViewModelBase
{
    private bool _suppressCallbacks;
    private Action? _onChanged;
    private bool _processingEnabled;
    private float _rawX;
    private float _rawY;
    private float _processedX;
    private float _processedY;

    /// <summary>
    /// Full per-stick processing settings applied when <see cref="ProcessingEnabled"/> is true.
    /// Reuses the same editor surface as the left stick.
    /// </summary>
    public LeftStickViewModel Processing { get; } = new();

    public RightStickViewModel()
    {
        // Bubble inner-settings edits to this VM's OnChanged so the main
        // coordinator pushes them into the live pipeline immediately.
        Processing.OnChanged = () => _onChanged?.Invoke();
    }

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

    public bool ProcessingEnabled
    {
        get => _processingEnabled;
        set => SetSetting(ref _processingEnabled, value);
    }

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

    public void LoadFrom(RightStickSettings settings)
    {
        // Suppress our own callback; Processing.LoadFrom suppresses its own.
        _suppressCallbacks = true;
        try
        {
            ProcessingEnabled = settings.ProcessingEnabled;
            Processing.LoadFrom(settings.Settings ?? ProcessingSettings.PassThrough());
        }
        finally
        {
            _suppressCallbacks = false;
        }
    }

    public RightStickSettings ToSettings() => new()
    {
        ProcessingEnabled = ProcessingEnabled,
        Settings = Processing.ToSettings()
    };

    public void UpdateLiveData(float rawX, float rawY, float processedX, float processedY)
    {
        RawX = rawX;
        RawY = rawY;
        ProcessedX = processedX;
        ProcessedY = processedY;
    }
}
