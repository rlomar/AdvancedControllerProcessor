using AdvancedControllerProcessor.Models;

namespace AdvancedControllerProcessor.Processing;

/// <summary>
/// Exponential Moving Average (EMA) smoothing for stick values.
///
/// Smoothing reduces jitter but adds latency.
/// The smoothing amount controls how much the previous value influences the current output.
///
/// Formula:
///   output = previousOutput * amount + input * (1 - amount)
///
/// At amount=0.0: no smoothing (output = input)
/// At amount=0.5: half previous, half current (moderate smoothing)
/// At amount=0.9: mostly previous value (heavy smoothing, noticeable latency)
///
/// WARNING: Smoothing introduces input latency proportional to the amount.
/// For competitive gaming, keep this OFF (default) or use very low values (0.1-0.2).
///
/// This processor maintains state between calls (the previous output value).
/// </summary>
public sealed class SmoothingProcessor
{
    private float _previousX;
    private float _previousY;

    /// <summary>
    /// Process the full stick state with smoothing.
    /// Maintains internal state for the EMA filter.
    /// </summary>
    public StickState Process(StickState input, ProcessingSettings settings)
    {
        if (!settings.SmoothingEnabled)
        {
            // Reset state when smoothing is disabled to avoid stale values
            _previousX = input.X;
            _previousY = input.Y;
            return input;
        }

        float amount = Math.Clamp(settings.SmoothingAmount, 0f, 0.95f);
        float oneMinusAmount = 1f - amount;

        float x = _previousX * amount + input.X * oneMinusAmount;
        float y = _previousY * amount + input.Y * oneMinusAmount;

        _previousX = x;
        _previousY = y;

        return new StickState(x, y);
    }

    /// <summary>
    /// Reset internal state. Call when switching profiles or resetting.
    /// </summary>
    public void Reset()
    {
        _previousX = 0f;
        _previousY = 0f;
    }
}
