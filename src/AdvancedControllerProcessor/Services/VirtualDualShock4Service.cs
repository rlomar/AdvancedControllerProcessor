using AdvancedControllerProcessor.Helpers;
using AdvancedControllerProcessor.Models;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.DualShock4;

namespace AdvancedControllerProcessor.Services;

/// <summary>
/// Creates and feeds a virtual DualShock 4 controller via ViGEmBus.
///
/// LATENCY NOTE:
/// With AutoSubmitReport enabled (library default), EVERY Set* call submits
/// a full report to the bus driver. A single frame touches ~25 properties,
/// i.e. ~25 bus round-trips per input report (thousands per second over USB),
/// which causes noticeable input lag on the virtual DS4 pad.
///
/// This service therefore runs in BATCH mode:
///   AutoSubmitReport = false  →  set all values  →  SubmitReport() ONCE.
/// One bus submission per frame instead of ~25.
///
/// Mapping from DualSense is 1:1 for every button, including PS and Touchpad
/// which cannot be represented on an Xbox 360 target.
/// Stick Y is NOT inverted: DS4 shares the DualSense Y-down convention
/// (0x00 = up, 0xFF = down).
///
/// Thread-safe: Create/Remove/SubmitState are serialized via a lock so the
/// input thread never touches a target being disposed by the UI thread.
///
/// NOTE: Games that only support XInput will not see this pad.
/// </summary>
public sealed class VirtualDualShock4Service : IVirtualControllerService
{
    private readonly object _sync = new();
    private ViGEmClient? _client;
    private IDualShock4Controller? _target;
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

                _target = _client.CreateDualShock4Controller();
                _target.Connect();

                // Batch mode: accumulate state changes, submit once per frame.
                _target.AutoSubmitReport = false;
                _target.ResetReport();

                _isActive = true;
                ControllerCreated?.Invoke();
                Logging.Info("[Virtual] DualShock 4 virtual controller created (batch mode)");
                return true;
            }
            catch (Exception ex)
            {
                Logging.Error(ex, "[Virtual] Failed to create virtual DualShock 4 controller");
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
                    _target = null;
                }

                _client?.Dispose();
                _client = null;

                if (_isActive)
                {
                    _isActive = false;
                    ControllerRemoved?.Invoke();
                    Logging.Info("[Virtual] DualShock 4 virtual controller removed");
                }
            }
            catch (Exception ex)
            {
                Logging.Error(ex, "[Virtual] Error removing virtual DualShock 4 controller");
            }
        }
    }

    /// <summary>
    /// Submit a processed controller state to the virtual DualShock 4 pad.
    /// All values must be pre-clamped to valid ranges.
    /// The full frame is accumulated and submitted exactly once.
    /// </summary>
    public void SubmitState(ControllerState state)
    {
        lock (_sync)
        {
            var target = _target;
            if (target is null || !_isActive || _client is null)
                return;

            try
            {
                // Start from a neutral report every frame so stale button/axis
                // bits can never leak between frames.
                target.ResetReport();

                // Sticks: float [-1, +1] -> byte [0, 255], center 128
                // No Y inversion: DS4 and DualSense share the same Y-down convention
                target.SetAxisValue(DualShock4Axis.LeftThumbX, FloatToAxisByte(state.LeftStick.X));
                target.SetAxisValue(DualShock4Axis.LeftThumbY, FloatToAxisByte(state.LeftStick.Y));
                target.SetAxisValue(DualShock4Axis.RightThumbX, FloatToAxisByte(state.RightStick.X));
                target.SetAxisValue(DualShock4Axis.RightThumbY, FloatToAxisByte(state.RightStick.Y));

                // Triggers: float [0, 1] -> byte [0, 255]
                target.SetSliderValue(DualShock4Slider.LeftTrigger, FloatToTriggerByte(state.L2));
                target.SetSliderValue(DualShock4Slider.RightTrigger, FloatToTriggerByte(state.R2));

                // D-Pad: hat switch maps directly to DS4 compass directions
                target.SetDPadDirection(ToDPadDirection(state.DPad));

                // Face buttons: identical layout on both pads
                target.SetButtonState(DualShock4Button.Cross, (state.Buttons & GamepadButton.Cross) != 0);
                target.SetButtonState(DualShock4Button.Circle, (state.Buttons & GamepadButton.Circle) != 0);
                target.SetButtonState(DualShock4Button.Square, (state.Buttons & GamepadButton.Square) != 0);
                target.SetButtonState(DualShock4Button.Triangle, (state.Buttons & GamepadButton.Triangle) != 0);

                // Shoulder buttons and stick clicks
                target.SetButtonState(DualShock4Button.ShoulderLeft, (state.Buttons & GamepadButton.L1) != 0);
                target.SetButtonState(DualShock4Button.ShoulderRight, (state.Buttons & GamepadButton.R1) != 0);
                target.SetButtonState(DualShock4Button.ThumbLeft, (state.Buttons & GamepadButton.L3) != 0);
                target.SetButtonState(DualShock4Button.ThumbRight, (state.Buttons & GamepadButton.R3) != 0);

                // System buttons
                target.SetButtonState(DualShock4Button.Options, (state.Buttons & GamepadButton.Options) != 0);
                target.SetButtonState(DualShock4Button.Share, (state.Buttons & GamepadButton.Create) != 0);

                // Special buttons: only possible on a DS4 target (dropped on Xbox target)
                target.SetSpecialButtonsFull((byte)(
                    ((state.Buttons & GamepadButton.PS) != 0 ? 0x01 : 0x00) |
                    ((state.Buttons & GamepadButton.Touchpad) != 0 ? 0x02 : 0x00)));

                // Single bus submission for the whole frame (~25x fewer ioctls
                // than per-property auto-submit — removes the DS4 input delay).
                target.SubmitReport();
            }
            catch (ObjectDisposedException)
            {
                // Service is being removed while the input thread is mid-frame.
                // Safe to ignore; next frame will see _target == null.
            }
            catch (Exception ex)
            {
                Logging.Error(ex, "[Virtual] Error submitting DS4 report");
            }
        }
    }

    public void Dispose()
    {
        Remove();
    }

    private static DualShock4DPadDirection ToDPadDirection(DPDirection dp) => dp switch
    {
        DPDirection.Up => DualShock4DPadDirection.North,
        DPDirection.UpRight => DualShock4DPadDirection.Northeast,
        DPDirection.Right => DualShock4DPadDirection.East,
        DPDirection.DownRight => DualShock4DPadDirection.Southeast,
        DPDirection.Down => DualShock4DPadDirection.South,
        DPDirection.DownLeft => DualShock4DPadDirection.Southwest,
        DPDirection.Left => DualShock4DPadDirection.West,
        DPDirection.UpLeft => DualShock4DPadDirection.Northwest,
        _ => DualShock4DPadDirection.None
    };

    /// <summary>
    /// Convert float [-1, +1] to byte [0, 255] for a DS4 stick axis (center 128).
    /// </summary>
    private static byte FloatToAxisByte(float value)
    {
        float clamped = Math.Clamp(value, -1f, 1f);
        return (byte)Math.Round(128f + clamped * 127f);
    }

    /// <summary>
    /// Convert float [0, 1] to byte [0, 255] for a DS4 trigger.
    /// </summary>
    private static byte FloatToTriggerByte(float value)
    {
        float clamped = Math.Clamp(value, 0f, 1f);
        return (byte)Math.Round(clamped * 255f);
    }
}
