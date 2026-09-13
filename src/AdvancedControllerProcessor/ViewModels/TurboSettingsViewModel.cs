using System.Runtime.CompilerServices;
using AdvancedControllerProcessor.Models;

namespace AdvancedControllerProcessor.ViewModels;

/// <summary>
/// ViewModel for per-button turbo (rapid-fire) settings.
/// Mirrors the stick VMs: every edit invokes <see cref="OnChanged"/> so
/// MainViewModel pushes the value into the live pipeline immediately.
/// </summary>
public sealed class TurboSettingsViewModel : ViewModelBase
{
    private bool _suppressCallbacks;
    private Action? _onChanged;

    private bool _turboEnabled;
    private double _turboIntervalMs = 8.0;
    private bool _turboA;
    private bool _turboB;
    private bool _turboX;
    private bool _turboY;
    private bool _turboLB;
    private bool _turboRB;
    private bool _turboL2;
    private bool _turboR2;

    /// <summary>
    /// Callback invoked when any turbo setting changes.
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

    public bool TurboEnabled
    {
        get => _turboEnabled;
        set
        {
            if (SetSetting(ref _turboEnabled, value))
                OnPropertyChanged(nameof(EnabledText));
        }
    }

    /// <summary>Plain-language status for chips/hero display.</summary>
    public string EnabledText => TurboEnabled ? "ON" : "OFF";

    /// <summary>
    /// Milliseconds between state flips while a turbo button is held.
    /// Minimum 0.01 (the engine floors at the virtual pad's real report rate).
    /// </summary>
    public double TurboIntervalMs
    {
        get => _turboIntervalMs;
        set
        {
            if (SetSetting(ref _turboIntervalMs, Math.Clamp(value, 0.01, 500)))
                OnPropertyChanged(nameof(IntervalText));
        }
    }

    /// <summary>Friendly label: gap in ms + equivalent presses per second.</summary>
    public string IntervalText => $"0.01–500 ms · ≈{(int)Math.Round(1000.0 / Math.Max(_turboIntervalMs, 0.01))} presses/s";

    public bool TurboA
    {
        get => _turboA;
        set => SetSetting(ref _turboA, value);
    }

    public bool TurboB
    {
        get => _turboB;
        set => SetSetting(ref _turboB, value);
    }

    public bool TurboX
    {
        get => _turboX;
        set => SetSetting(ref _turboX, value);
    }

    public bool TurboY
    {
        get => _turboY;
        set => SetSetting(ref _turboY, value);
    }

    public bool TurboLB
    {
        get => _turboLB;
        set => SetSetting(ref _turboLB, value);
    }

    public bool TurboRB
    {
        get => _turboRB;
        set => SetSetting(ref _turboRB, value);
    }

    public bool TurboL2
    {
        get => _turboL2;
        set => SetSetting(ref _turboL2, value);
    }

    public bool TurboR2
    {
        get => _turboR2;
        set => SetSetting(ref _turboR2, value);
    }

    public bool AnyTurboAssigned => TurboA || TurboB || TurboX || TurboY || TurboLB || TurboRB || TurboL2 || TurboR2;

    public void LoadFrom(ButtonTurboSettings settings)
    {
        _suppressCallbacks = true;
        try
        {
            TurboEnabled = settings.TurboEnabled;
            TurboIntervalMs = Math.Clamp(settings.TurboIntervalMs, 0.01, 500);
            TurboA = settings.TurboA;
            TurboB = settings.TurboB;
            TurboX = settings.TurboX;
            TurboY = settings.TurboY;
            TurboLB = settings.TurboLB;
            TurboRB = settings.TurboRB;
            TurboL2 = settings.TurboL2;
            TurboR2 = settings.TurboR2;
        }
        finally
        {
            _suppressCallbacks = false;
        }
    }

    public ButtonTurboSettings ToSettings() => new()
    {
        TurboEnabled = TurboEnabled,
        TurboIntervalMs = TurboIntervalMs,
        TurboA = TurboA,
        TurboB = TurboB,
        TurboX = TurboX,
        TurboY = TurboY,
        TurboLB = TurboLB,
        TurboRB = TurboRB,
        TurboL2 = TurboL2,
        TurboR2 = TurboR2
    };
}