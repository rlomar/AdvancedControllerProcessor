using AdvancedControllerProcessor.Models;
using AdvancedControllerProcessor.Processing;

namespace AdvancedControllerProcessor.Services;

/// <summary>
/// Orchestrates the full stick processing pipeline:
///
///   Raw Input -> Deadzone -> Response Curve -> Speed -> DirectionalSpeed -> Smoothing -> Clamp
///
/// For Left Stick: full pipeline based on ProcessingSettings.
/// For Right Stick: pass-through by default, optional full pipeline.
/// Buttons/Triggers/DPad: pass-through, except turbo controls (see
/// <see cref="ApplyTurbo"/> and <see cref="ProcessTrigger"/>), which are
/// oscillated at the configured rate.
///
/// Thread-safe: only Process() and ResetSmoothing() access mutable state,
/// and they are expected to be called from a single input thread.
/// </summary>
public sealed class InputProcessingService : IInputProcessingService
{
    private readonly SmoothingProcessor _leftSmoothing = new();
    private readonly SmoothingProcessor _rightSmoothing = new();

    // Turbo state. Index-aligned with _turboButtons. Accessed only from the
    // single input thread that calls Process(), so no locking is needed.
    private static readonly GamepadButton[] TurboButtons =
    {
        GamepadButton.A, GamepadButton.B, GamepadButton.X, GamepadButton.Y,
        GamepadButton.LeftShoulder, GamepadButton.RightShoulder
    };

    private readonly bool[] _turboPhase = new bool[TurboButtons.Length];
    private readonly long[] _turboLastTick = new long[TurboButtons.Length];

    // Trigger turbo state (index 0 = L2, 1 = R2). Same square-wave oscillator
    // as the buttons, but flips the analog value (held pull ↔ 0) instead of a bit.
    private readonly bool[] _triggerPhase = new bool[2];
    private readonly long[] _triggerLastTick = new long[2];

    // Per-stick curve cache. The hot path runs hundreds of times per second;
    // resolving a CustomCurve there would allocate a list-backed object every
    // frame and cause GC churn that shows up as stickiness/stutter.
    private string _leftCurveName = string.Empty;
    private List<CurvePoint>? _leftCurvePoints;
    private IResponseCurve _leftCurve = LinearCurve.Instance;
    private string _rightCurveName = string.Empty;
    private List<CurvePoint>? _rightCurvePoints;
    private IResponseCurve _rightCurve = LinearCurve.Instance;

    public bool ProcessingEnabled { get; set; }
    public Profile CurrentProfile { get; set; } = Profile.Default();

    /// <summary>
    /// Process raw controller state through the full pipeline.
    /// When ProcessingEnabled is false, raw input passes through unmodified.
    /// </summary>
    public ControllerState Process(ControllerState rawInput)
    {
        // Defensive: neutralize any non-finite value before it enters the
        // pipeline. NaN would otherwise propagate through smoothing state and
        // make MathF.Sign throw ArithmeticException on every frame.
        var left = Sanitize(rawInput.LeftStick);
        var right = Sanitize(rawInput.RightStick);

        if (!ProcessingEnabled)
        {
            // Pass-through: sanitized input unmodified otherwise
            return new ControllerState
            {
                LeftStick = left,
                RightStick = right,
                L2 = Sanitize01(rawInput.L2),
                R2 = Sanitize01(rawInput.R2),
                Buttons = rawInput.Buttons,
                DPad = rawInput.DPad,
                Connection = rawInput.Connection,
                Timestamp = rawInput.Timestamp
            };
        }

        var leftStick = ProcessLeftStick(left);
        var rightStick = ProcessRightStick(right);

        var turbo = CurrentProfile.Turbo;

        return new ControllerState
        {
            LeftStick = leftStick,
            RightStick = rightStick,
            L2 = ProcessTrigger(Sanitize01(rawInput.L2), 0, turbo),
            R2 = ProcessTrigger(Sanitize01(rawInput.R2), 1, turbo),
            Buttons = ApplyTurbo(rawInput.Buttons, turbo),
            DPad = rawInput.DPad,
            Connection = rawInput.Connection,
            Timestamp = rawInput.Timestamp
        };
    }

    private static StickState Sanitize(StickState s) =>
        new(Sanitize(s.X), Sanitize(s.Y));

    private static float Sanitize(float v) => float.IsFinite(v) ? v : 0f;

    private static float Sanitize01(float v) =>
        float.IsFinite(v) ? Math.Clamp(v, 0f, 1f) : 0f;

    /// <summary>
    /// Apply the turbo oscillator to the raw button bitset when turbo is
    /// configured and the profile has it enabled. For each turbo-assigned
    /// button still held down, the virtual output toggles between pressed and
    /// released with a flip interval of <see cref="ButtonTurboSettings.TurboIntervalMs"/>
    /// milliseconds (floor 1 ms = the virtual pad's real report ceiling).
    ///
    /// A button that was just pressed starts in the "pressed" phase so the
    /// first press is immediate — no initial dead half-cycle.
    ///
    /// Allocation-free (fixed-size arrays + Environment.TickCount64).
    /// </summary>
    private GamepadButton ApplyTurbo(GamepadButton raw, ButtonTurboSettings? turbo)
    {
        if (turbo is null || !turbo.TurboEnabled)
            return raw;

        // Milliseconds between flips. 0.01 is accepted as input, but the
        // oscillator can only flip as fast as the virtual pad reports
        // (~1 ms at the 250 Hz XInput max) — floor it here.
        long flipMs = Math.Max((long)Math.Clamp(turbo.TurboIntervalMs, 0.01, 500), 1L);
        long now = Environment.TickCount64;
        GamepadButton output = GamepadButton.None;

        for (int i = 0; i < TurboButtons.Length; i++)
        {
            var flag = TurboButtons[i];

            if ((raw & flag) != 0 && turbo.IsButtonTurbo(flag))
            {
                long last = _turboLastTick[i];
                if (last == 0) // just pressed — bite immediately
                {
                    _turboLastTick[i] = now;
                    _turboPhase[i] = true;
                    output |= flag;
                }
                else if (now - last >= flipMs)
                {
                    if (now < last) // TickCount64 wrapped — resync, keep phase
                        _turboLastTick[i] = now;
                    else
                    {
                        _turboLastTick[i] = now;
                        _turboPhase[i] = !_turboPhase[i];
                    }
                    if (_turboPhase[i])
                        output |= flag;
                }
                else if (_turboPhase[i])
                {
                    output |= flag; // still inside the current "on" half-cycle
                }
            }
            else
            {
                _turboLastTick[i] = 0; // released or not assigned — reset
                _turboPhase[i] = false;
                if ((raw & flag) != 0)
                    output |= flag; // held but not turbo-assigned — pass through unchanged
            }
        }

        return output;
    }

    /// <summary>
    /// Apply the turbo oscillator to an L2/R2 analog trigger value.
    /// While an assigned trigger is held (pull &gt; 0), the virtual output toggles
    /// between the user's real pull and 0 with the same flip interval as the
    /// buttons. The first pull answers immediately. Mirror-image of the button
    /// oscillator in <see cref="ApplyTurbo"/>, allocation-free.
    /// </summary>
    private float ProcessTrigger(float raw, int index, ButtonTurboSettings? turbo)
    {
        if (turbo is null || !turbo.TurboEnabled)
        {
            _triggerLastTick[index] = 0;
            _triggerPhase[index] = false;
            return raw;
        }

        long flipMs = Math.Max((long)Math.Clamp(turbo.TurboIntervalMs, 0.01, 500), 1L);
        long now = Environment.TickCount64;

        if (!turbo.IsTriggerTurbo(index) || raw <= 0f)
        {
            // Released or not assigned — reset and pass through unchanged.
            _triggerLastTick[index] = 0;
            _triggerPhase[index] = false;
            return raw;
        }

        long last = _triggerLastTick[index];
        if (last == 0) // just pressed — bite immediately with the user's pull
        {
            _triggerLastTick[index] = now;
            _triggerPhase[index] = true;
            return raw;
        }

        if (now - last >= flipMs)
        {
            if (now < last) // TickCount64 wrapped — resync, keep phase
                _triggerLastTick[index] = now;
            else
            {
                _triggerLastTick[index] = now;
                _triggerPhase[index] = !_triggerPhase[index];
            }
        }

        return _triggerPhase[index] ? raw : 0f;
    }

    /// <summary>
    /// Reset smoothing state for both sticks and the turbo oscillators.
    /// Call when switching profiles, entering safe mode, or toggling processing.
    /// </summary>
    public void ResetSmoothing()
    {
        _leftSmoothing.Reset();
        _rightSmoothing.Reset();

        Array.Clear(_turboPhase);
        Array.Clear(_turboLastTick);
        Array.Clear(_triggerPhase);
        Array.Clear(_triggerLastTick);
    }

    /// <summary>
    /// Process left stick through the full pipeline.
    ///
    /// Pipeline order:
    ///   1. Deadzone (Radial or Axial)
    ///   2. Response Curve (Linear/Soft/Aggressive/Custom)
    ///   3. Speed Multiplier (X/Y independent)
    ///   4. Directional Speed (optional Forward/Back/Left/Right)
    ///   5. Smoothing (optional EMA)
    ///   6. Clamp [-1, +1]
    /// </summary>
    private StickState ProcessLeftStick(StickState raw)
    {
        var settings = CurrentProfile.LeftStick;

        // 1. Deadzone
        StickState step1 = settings.DeadzoneEnabled
            ? (settings.DeadzoneType == "Axial"
                ? DeadzoneProcessor.ProcessAxial(raw, settings.Deadzone, settings.Deadzone)
                : DeadzoneProcessor.ProcessRadial(raw, settings.Deadzone))
            : raw;

        // 2. Response Curve
        var curve = GetCachedCurve(
            ref _leftCurveName, ref _leftCurvePoints, ref _leftCurve,
            settings.ResponseCurve, settings.CustomCurvePoints);
        StickState step2 = new StickState(
            ApplyCurveWithSign(step1.X, curve),
            ApplyCurveWithSign(step1.Y, curve));

        // 3. Speed Multiplier
        StickState step3 = new StickState(
            SpeedMultiplierProcessor.ProcessX(step2.X, settings),
            SpeedMultiplierProcessor.ProcessY(step2.Y, settings));

        // 4. Directional Speed
        StickState step4 = DirectionalSpeedProcessor.Process(step3, settings);

        // 5. Smoothing
        StickState step5 = _leftSmoothing.Process(step4, settings);

        // 6. Clamp
        return ClampProcessor.ClampStick(step5);
    }

    /// <summary>
    /// Process right stick. By default this is pass-through.
    /// Only applies full pipeline if RightStick.ProcessingEnabled is true.
    /// </summary>
    private StickState ProcessRightStick(StickState raw)
    {
        var rightSettings = CurrentProfile.RightStick;

        if (!rightSettings.ProcessingEnabled || rightSettings.Settings is null)
            return raw; // Pass-through

        var settings = rightSettings.Settings;

        // Same pipeline as left stick
        StickState step1 = settings.DeadzoneEnabled
            ? (settings.DeadzoneType == "Axial"
                ? DeadzoneProcessor.ProcessAxial(raw, settings.Deadzone, settings.Deadzone)
                : DeadzoneProcessor.ProcessRadial(raw, settings.Deadzone))
            : raw;

        var curve = GetCachedCurve(
            ref _rightCurveName, ref _rightCurvePoints, ref _rightCurve,
            settings.ResponseCurve, settings.CustomCurvePoints);
        StickState step2 = new StickState(
            ApplyCurveWithSign(step1.X, curve),
            ApplyCurveWithSign(step1.Y, curve));

        StickState step3 = new StickState(
            SpeedMultiplierProcessor.ProcessX(step2.X, settings),
            SpeedMultiplierProcessor.ProcessY(step2.Y, settings));

        StickState step4 = DirectionalSpeedProcessor.Process(step3, settings);

        StickState step5 = _rightSmoothing.Process(step4, settings);

        return ClampProcessor.ClampStick(step5);
    }

    /// <summary>
    /// Resolve a response curve from its name and custom points.
    /// </summary>
    private static IResponseCurve ResolveCurve(string curveName, List<CurvePoint>? customPoints)
    {
        return curveName switch
        {
            "Soft" => SoftCurve.Instance,
            "Aggressive" => AggressiveCurve.Instance,
            "Custom" => new CustomCurve(customPoints ?? []),
            _ => LinearCurve.Instance // "Linear" or unknown
        };
    }

    /// <summary>
    /// Cached variant of <see cref="ResolveCurve"/> for the hot path.
    /// Rebuilds only when the curve name or the point-list instance changes.
    /// The UI pushes brand-new settings objects on every edit, so reference
    /// equality is a precise change signal: cache hits for every frame between
    /// user edits, one rebuild per edit, zero allocations in steady state.
    /// </summary>
    private static IResponseCurve GetCachedCurve(
        ref string cachedName, ref List<CurvePoint>? cachedPoints, ref IResponseCurve cachedCurve,
        string curveName, List<CurvePoint>? customPoints)
    {
        if (cachedName == curveName && ReferenceEquals(cachedPoints, customPoints))
            return cachedCurve;

        cachedCurve = ResolveCurve(curveName, customPoints);
        cachedName = curveName;
        cachedPoints = customPoints;
        return cachedCurve;
    }

    /// <summary>
    /// Apply a response curve to a signed value.
    /// The curve operates on absolute values, sign is preserved.
    /// This ensures the curve doesn't flip the stick direction.
    /// </summary>
    private static float ApplyCurveWithSign(float signedValue, IResponseCurve curve)
    {
        float abs = MathF.Abs(signedValue);
        float curved = curve.Evaluate(abs);
        // MathF.Sign throws ArithmeticException on NaN — use a branch instead.
        float sign = signedValue < 0f ? -1f : signedValue > 0f ? 1f : 0f;
        return sign * curved;
    }
}
