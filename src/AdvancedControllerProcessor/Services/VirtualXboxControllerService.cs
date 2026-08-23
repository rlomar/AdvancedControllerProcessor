using AdvancedControllerProcessor.Helpers;
using AdvancedControllerProcessor.Models;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;

namespace AdvancedControllerProcessor.Services;

/// <summary>
/// Creates and feeds a virtual Xbox 360 controller via ViGEmBus.
///
/// LATENCY NOTE (batch mode):
/// With AutoSubmitReport enabled (library default), EVERY Set* call submits
/// a full report to the bus driver. A single frame touches ~25 properties,
/// i.e. ~25 bus round-trips per input report (thousands per second over USB),
/// which floods ViGEmBus and causes noticeable input lag on the virtual pad.
///
/// This service therefore runs in BATCH mode:
///   AutoSubmitReport = false  →  set all values  →  SubmitReport() ONCE.
/// One bus submission per frame instead of ~25.
///
/// ViGEm API (v1.21.256) uses property-based state setting:
///   SetAxisValue(Xbox360Axis, short)   — sticks: [-32768, +32767]
///   SetSliderValue(Xbox360Slider, byte) — triggers: [0, 255]
///   SetButtonState(Xbox360Button, bool) — individual button press/release
///
/// Mapping from DualSense:
///   Sticks: float [-1, +1] -> short [-32768, +32767]
///   Triggers: float [0, 1] -> byte [0, 255]
///   D-Pad: individual Up/Down/Left/Right button states
///   Face buttons: A=Cross, B=Circle, X=Square, Y=Triangle
///
/// Thread-safe: Create/Remove/SubmitState are serialized via a lock so the
/// input thread never touches a target being disposed by the UI thread.
/// </summary>
public sealed class VirtualXboxControllerService : IVirtualControllerService
{
    private readonly object _sync = new();
    private ViGEmClient? _client;
    private IXbox360Controller? _target;
    private bool _isActive;

    public event Action? ControllerCreated;
    public event Action? ControllerRemoved;

    public bool IsActive => _isActive;

    public bool Create()
    {
        lock (_sync)
        {
            try
            {
                _client?.Dispose();
                _client = new ViGEmClient();

                _target = _client.CreateXbox360Controller();
                _target.FeedbackReceived += OnFeedbackReceived;
                _target.Connect();

                // Batch mode: accumulate state changes, submit once per frame.
                _target.AutoSubmitReport = false;
                _target.ResetReport();

                _isActive = true;
                ControllerCreated?.Invoke();
                Logging.Info("[Virtual] Xbox 360 virtual controller created (batch mode)");
                return true;
            }
            catch (Exception ex)
            {
                Logging.Error(ex, "[Virtual] Failed to create virtual controller");
                _isActive = false;
                return false;
            }
        }
    }

    public void Remove()
    {
        lock (_sync)
        {
            try
            {
                if (_target is not null)
                {
                    _target.Disconnect();
                    _target.FeedbackReceived -= OnFeedbackReceived;
                    _target = null;
                }

                _client?.Dispose();
                _client = null;

                if (_isActive)
                {
                    _isActive = false;
                    ControllerRemoved?.Invoke();
                    Logging.Info("[Virtual] Virtual controller removed");
                }
            }
            catch (Exception ex)
            {
                Logging.Error(ex, "[Virtual] Error removing virtual controller");
            }
        }
    }

    /// <summary>
    /// Submit a processed controller state to the virtual Xbox 360 pad.
    /// All values must be pre-clamped to valid ranges.
    /// The full frame is accumulated and submitted exactly once.
    /// </summary>
    public void SubmitState(ControllerState state)
    {
        lock (_sync)
        {
            var target = _target;
            if (target is null || !_isActive)
                return;

            try
            {
                // Start from a neutral report every frame so stale button/axis
                // bits can never leak between frames.
                target.ResetReport();

                // Sticks: float [-1, +1] -> short [-32768, +32767]
                // Y axis is inverted: DualSense Y-down positive, XInput Y-up positive
                target.SetAxisValue(Xbox360Axis.LeftThumbX, FloatToShort(state.LeftStick.X));
                target.SetAxisValue(Xbox360Axis.LeftThumbY, FloatToShort(-state.LeftStick.Y));
                target.SetAxisValue(Xbox360Axis.RightThumbX, FloatToShort(state.RightStick.X));
                target.SetAxisValue(Xbox360Axis.RightThumbY, FloatToShort(-state.RightStick.Y));

                // Triggers: float [0, 1] -> byte [0, 255]
                target.SetSliderValue(Xbox360Slider.LeftTrigger, FloatToByte(state.L2));
                target.SetSliderValue(Xbox360Slider.RightTrigger, FloatToByte(state.R2));

                // D-Pad: map hat switch to individual directional buttons
                target.SetButtonState(Xbox360Button.Up,
                    state.DPad == DPDirection.Up ||
                    state.DPad == DPDirection.UpLeft ||
                    state.DPad == DPDirection.UpRight);
                target.SetButtonState(Xbox360Button.Down,
                    state.DPad == DPDirection.Down ||
                    state.DPad == DPDirection.DownLeft ||
                    state.DPad == DPDirection.DownRight);
                target.SetButtonState(Xbox360Button.Left,
                    state.DPad == DPDirection.Left ||
                    state.DPad == DPDirection.UpLeft ||
                    state.DPad == DPDirection.DownLeft);
                target.SetButtonState(Xbox360Button.Right,
                    state.DPad == DPDirection.Right ||
                    state.DPad == DPDirection.UpRight ||
                    state.DPad == DPDirection.DownRight);

                // Face buttons: DualSense -> Xbox mapping
                target.SetButtonState(Xbox360Button.A, (state.Buttons & GamepadButton.Cross) != 0);
                target.SetButtonState(Xbox360Button.B, (state.Buttons & GamepadButton.Circle) != 0);
                target.SetButtonState(Xbox360Button.X, (state.Buttons & GamepadButton.Square) != 0);
                target.SetButtonState(Xbox360Button.Y, (state.Buttons & GamepadButton.Triangle) != 0);

                // Shoulder buttons
                target.SetButtonState(Xbox360Button.LeftShoulder, (state.Buttons & GamepadButton.L1) != 0);
                target.SetButtonState(Xbox360Button.RightShoulder, (state.Buttons & GamepadButton.R1) != 0);

                // Stick clicks
                target.SetButtonState(Xbox360Button.LeftThumb, (state.Buttons & GamepadButton.L3) != 0);
                target.SetButtonState(Xbox360Button.RightThumb, (state.Buttons & GamepadButton.R3) != 0);

                // System buttons
                target.SetButtonState(Xbox360Button.Start, (state.Buttons & GamepadButton.Options) != 0);
                target.SetButtonState(Xbox360Button.Back, (state.Buttons & GamepadButton.Create) != 0);

                // Single bus submission for the whole frame (~25x fewer ioctls
                // than per-property auto-submit — removes the input delay).
                target.SubmitReport();
            }
            catch (ObjectDisposedException)
            {
                // Service is being removed while the input thread is mid-frame.
                // Safe to ignore; next frame will see _target == null.
            }
            catch (Exception ex)
            {
                Logging.Error(ex, "[Virtual] Error submitting report");
            }
        }
    }

    public void Dispose()
    {
        Remove();
    }

    private void OnFeedbackReceived(object? sender, Xbox360FeedbackReceivedEventArgs e)
    {
        // Could handle rumble/LED feedback here in the future
    }

    /// <summary>
    /// Convert float [-1, +1] to short [-32768, +32767] for Xbox 360 stick axis.
    /// </summary>
    private static short FloatToShort(float value)
    {
        float clamped = Math.Clamp(value, -1f, 1f);
        return (short)Math.Round(clamped * 32767f);
    }

    /// <summary>
    /// Convert float [0, 1] to byte [0, 255] for Xbox 360 trigger.
    /// </summary>
    private static byte FloatToByte(float value)
    {
        float clamped = Math.Clamp(value, 0f, 1f);
        return (byte)(clamped * 255f);
    }
}
