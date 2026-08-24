using System.Diagnostics;
using System.IO;
using AdvancedControllerProcessor.Helpers;
using AdvancedControllerProcessor.Models;
using HidSharp;

namespace AdvancedControllerProcessor.Services;

/// <summary>
/// Reads input from a PS5 DualSense controller via HID.
///
/// DualSense HID report formats (Report ID 0x01):
///
/// USB (64 bytes total):
///   Byte  0: Report ID (0x01)
///   Byte  1: Left Stick X  (0x00=left, 0x80=center, 0xFF=right)
///   Byte  2: Left Stick Y  (0x00=up,   0x80=center, 0xFF=down)
///   Byte  3: Right Stick X (0x00=left, 0x80=center, 0xFF=right)
///   Byte  4: Right Stick Y (0x00=up,   0x80=center, 0xFF=down)
///   Byte  5: L2 Trigger    (0x00=released, 0xFF=pressed)
///   Byte  6: R2 Trigger    (0x00=released, 0xFF=pressed)
///   Byte  7: Sequence number
///   Byte  8: D-Pad [bits 0-3], Square[4], Cross[5], Circle[6], Triangle[7]
///   Byte  9: L1[0], R1[1], L2btn[2], R2btn[3], Create[4], Options[5], L3[6], R3[7]
///   Byte 10: PS[0], Touchpad[1], Mute[2]
///
/// Bluetooth (78 bytes total):
///   Byte  0: Report ID (0x31)
///   Byte  1: Left Stick X
///   Byte  2: Left Stick Y
///   Byte  3: Right Stick X
///   Byte  4: Right Stick Y
///   Byte  5: D-Pad [bits 0-3], Square[4], Cross[5], Circle[6], Triangle[7]
///   Byte  6: L1[0], R1[1], L2btn[2], R2btn[3], Create[4], Options[5], L3[6], R3[7]
///   Byte  7: PS[0], Touchpad[1], Mute[2]
///   Byte  8: L2 Trigger axis
///   Byte  9: R2 Trigger axis
///   Bytes 10+: motion sensors, touchpad, etc. (not used for basic input)
///
/// Normalization:
///   Stick:  (raw - 128) / 127.0f  -> [-1.0, +1.0] (approx)
///   Trigger: raw / 255.0f         -> [0.0, 1.0]
/// </summary>
public sealed class DualSenseControllerService : IControllerService
{
    // DualSense vendor/product IDs
    private const int VendorIdSony = 0x054C;
    private const int ProductIdDualSense = 0x0CE6;
    private const int ProductIdDualSenseEdge = 0x0DF2;

    // HID report sizes (including Report ID byte)
    private const int UsbReportSize = 64;
    private const int BluetoothReportSize = 78;

    // Byte offsets for USB report (after Report ID)
    private const int UsbLsxOffset = 1;
    private const int UsbLsyOffset = 2;
    private const int UsbRsxOffset = 3;
    private const int UsbRsyOffset = 4;
    private const int UsbL2Offset = 5;
    private const int UsbR2Offset = 6;
    private const int UsbButtons0Offset = 8;
    private const int UsbButtons1Offset = 9;
    private const int UsbButtons2Offset = 10;

    // Byte offsets for Bluetooth report (after Report ID)
    private const int BtLsxOffset = 1;
    private const int BtLsyOffset = 2;
    private const int BtRsxOffset = 3;
    private const int BtRsyOffset = 4;
    private const int BtButtons0Offset = 5;
    private const int BtButtons1Offset = 6;
    private const int BtButtons2Offset = 7;
    private const int BtL2Offset = 8;
    private const int BtR2Offset = 9;

    private HidDevice? _device;
    private HidStream? _stream;
    private CancellationTokenSource? _cts;
    private Thread? _inputThread;
    private bool _isConnected;
    private int _pollingRateHz = PollingRate.Default;

    // Raw hardware arrival-rate tracking (EMA of inter-report gaps in Stopwatch ticks).
    private long _arrivalEmaTicks;
    private long _lastArrivalTicks;
    private bool _hasLastArrival;

    public event Action<ControllerState>? StateChanged;
    public event Action<bool>? ConnectionChanged;
    public event Action<int>? MeasuredRateChanged;
    public event Action<int>? RawRateChanged;

    public bool IsConnected => _isConnected;
    public ConnectionType ConnectionType { get; private set; }

    /// <summary>
    /// Target submission rate to the virtual pad in Hz. Applied live.
    /// Raw HID reads always run at native hardware speed; this only gates
    /// how often parsed states are handed to StateChanged.
    /// </summary>
    public int PollingRateHz
    {
        get => Volatile.Read(ref _pollingRateHz);
        set => Volatile.Write(ref _pollingRateHz, PollingRate.Clamp(value));
    }

    /// <summary>
    /// Find the first connected DualSense controller.
    /// </summary>
    public bool TryConnect()
    {        try
        {
            var devices = DeviceList.Local.GetHidDevices()
                .Where(d => d.VendorID == VendorIdSony &&
                            (d.ProductID == ProductIdDualSense ||
                             d.ProductID == ProductIdDualSenseEdge))
                .ToList();

            if (devices.Count == 0)
            {
                if (_isConnected)
                {
                    _isConnected = false;
                    ConnectionType = ConnectionType.Unknown;
                    ConnectionChanged?.Invoke(false);
                }
                return false;
            }

            _device = devices[0];

            if (!_device.TryOpen(out _stream))
            {
                LogWarn("Failed to open DualSense HID stream");
                return false;
            }

            var previousState = _isConnected;
            _isConnected = true;
            _hasLastArrival = false;
            Volatile.Write(ref _arrivalEmaTicks, 0);

            if (!previousState)
            {
                ConnectionChanged?.Invoke(true);
                LogInfo("DualSense connected");
            }

            return true;
        }
        catch (Exception ex)
        {
            LogError(ex, "Error during DualSense connection");
            return false;
        }
    }

    /// <summary>
    /// Start the input polling loop on a dedicated background thread.
    /// No Thread.Sleep — uses blocking HID read which wakes immediately on new data.
    /// States are submitted to StateChanged at the configured PollingRateHz;
    /// the freshest report is always the one submitted (no stale data).
    /// </summary>
    public void Start()
    {
        if (_inputThread is { IsAlive: true })
            return;

        _cts = new CancellationTokenSource();
        _inputThread = new Thread(InputLoop)
        {
            Name = "DualSenseInput",
            Priority = ThreadPriority.AboveNormal,
            IsBackground = true
        };
        Helpers.InputThreadOptimizer.Prepare();
        _inputThread.Start();
    }

    public void Stop()
    {
        _cts?.Cancel();
        _inputThread?.Join(2000);
        CloseStream();
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }

    private void InputLoop()
    {
        // Pin to performance cores first — never run input on an E-core.
        Helpers.InputThreadOptimizer.ApplyToThisThread();

        var token = _cts?.Token ?? CancellationToken.None;
        var buffer = new byte[BluetoothReportSize]; // max size
        var sw = Stopwatch.StartNew();

        // Submission pacing state (ticks from sw)
        long lastSubmitTicks = 0;
        long windowStartTicks = sw.Elapsed.Ticks;
        int submittedInWindow = 0;
        bool firstReport = true;

        while (!token.IsCancellationRequested)
        {
            try
            {
                if (_stream is null || _device is null)
                {
                    if (!TryConnect())
                    {
                        Thread.Sleep(500); // wait before retrying connection
                        continue;
                    }
                }

                int bytesRead;
                try
                {
                    bytesRead = _stream!.Read(buffer, 0, buffer.Length);
                }
                catch (IOException)
                {
                    // Controller disconnected
                    HandleDisconnect();
                    continue;
                }
                catch (ObjectDisposedException)
                {
                    HandleDisconnect();
                    continue;
                }

                if (bytesRead == 0)
                    continue;

                ConnectionType connType = DetectConnectionType(bytesRead);
                if (connType == ConnectionType.Unknown)
                {
                    // Unrecognized report shape (vendor-specific/capability frames):
                    // parsing it with USB offsets would throw per-report, and each
                    // exception hit the error path (disk log + 15 ms sleep) — a
                    // constant-lag loop. Skip silently instead.
                    continue;
                }
                ControllerState state = ParseReport(buffer, connType);

                long nowTicks = sw.Elapsed.Ticks;
                UpdateArrivalRate(nowTicks);

                bool submit;
                if (firstReport)
                {
                    firstReport = false;
                    lastSubmitTicks = nowTicks;
                    submit = true;
                }
                else
                {
                    long periodTicks = TimeSpan.TicksPerSecond / Math.Max(1, PollingRateHz);
                    if (ShouldPassThrough(periodTicks, Volatile.Read(ref _arrivalEmaTicks)))
                    {
                        // Hardware is no faster than the target: pacing cannot add
                        // freshness, so submit event-driven on every arrival. Keeps
                        // the measured rate pinned to the true hardware rate.
                        lastSubmitTicks = nowTicks;
                        submit = true;
                    }
                    else
                    {
                        submit = ShouldSubmit(ref lastSubmitTicks, nowTicks, periodTicks);
                    }
                }

                if (submit)
                    submittedInWindow++;

                long windowElapsed = nowTicks - windowStartTicks;
                if (windowElapsed >= TimeSpan.TicksPerSecond / 2)
                {
                    MeasuredRateChanged?.Invoke(ComputeMeasuredHz(submittedInWindow, windowElapsed));
                    RawRateChanged?.Invoke(ComputeRawHz(Volatile.Read(ref _arrivalEmaTicks)));
                    submittedInWindow = 0;
                    windowStartTicks = nowTicks;
                }

                if (submit)
                {
                    // Scheduling wait: how long this report was held between
                    // arrival and delivery to the virtual pad (0 when
                    // event-driven). Feeds the dashboard latency monitor.
                    long submitTicks = sw.Elapsed.Ticks;
                    Latency.Wait.Record((submitTicks - nowTicks) / TimeSpan.TicksPerMicrosecond);
                    StateChanged?.Invoke(state);
                }
            }
            catch (ThreadInterruptedException)
            {
                break;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                LogError(ex, "Error in input loop");
                Thread.Sleep(15); // brief backoff; long sleeps stack HID backlog -> stale input bursts
            }
        }
    }

    /// <summary>
    /// Rate gate: decides whether a report at <paramref name="nowTicks"/> should be
    /// submitted given the target period between submissions.
    /// Drift-free: advances the schedule by whole periods; burst-safe: after a stall
    /// the schedule never falls more than one period behind real time.
    /// Pure/static so it can be unit tested without hardware.
    /// </summary>
    /// <returns>True when the caller should submit and lastSubmitTicks was updated.</returns>
    internal static bool ShouldSubmit(ref long lastSubmitTicks, long nowTicks, long periodTicks)
    {
        if (periodTicks <= 0)
        {
            lastSubmitTicks = nowTicks;
            return true;
        }

        long dueTicks = lastSubmitTicks + periodTicks;
        if (nowTicks < dueTicks)
            return false;

        lastSubmitTicks = Math.Max(dueTicks, nowTicks - periodTicks);
        return true;
    }

    /// <summary>
    /// Convert a submission count over a measured window (in Stopwatch ticks) to Hz.
    /// Pure/static so it can be unit tested without hardware.
    /// </summary>
    internal static int ComputeMeasuredHz(int submittedCount, long windowElapsedTicks) =>
        windowElapsedTicks <= 0 ? 0 : (int)((long)submittedCount * TimeSpan.TicksPerSecond / windowElapsedTicks);

    /// <summary>
    /// Convert an EMA inter-arrival gap (in Stopwatch ticks) to a raw hardware rate in Hz.
    /// Pure/static so it can be unit tested without hardware.
    /// </summary>
    internal static int ComputeRawHz(long arrivalEmaTicks) =>
        arrivalEmaTicks <= 0 ? 0 : (int)Math.Round((double)TimeSpan.TicksPerSecond / arrivalEmaTicks);

    /// <summary>
    /// Passthrough decision: when the target period is not longer than the observed
    /// average inter-arrival gap, pacing can never skip a stale report — the freshest
    /// data IS every report, so submit event-driven. Pure/static for unit testing.
    /// </summary>
    internal static bool ShouldPassThrough(long periodTicks, long arrivalEmaTicks) =>
        periodTicks <= arrivalEmaTicks;

    /// <summary>
    /// Maintain the exponential moving average of inter-report gaps.
    /// Alpha = 1/16: smooths Bluetooth burst jitter while reacting within ~16 reports.
    /// Called on the input thread only.
    /// </summary>
    private void UpdateArrivalRate(long nowTicks)
    {
        if (_hasLastArrival)
        {
            long interval = nowTicks - _lastArrivalTicks;
            // Ignore reconnect/hibernation-sized gaps so the EMA recovers quickly
            if (interval > 0 && interval < TimeSpan.TicksPerSecond)
            {
                long ema = Volatile.Read(ref _arrivalEmaTicks);
                ema = ema == 0 ? interval : ema - (ema >> 4) + (interval >> 4);
                Volatile.Write(ref _arrivalEmaTicks, ema);
            }
        }

        _lastArrivalTicks = nowTicks;
        _hasLastArrival = true;
    }

    private void HandleDisconnect()
    {
        CloseStream();
        if (_isConnected)
        {
            _isConnected = false;
            ConnectionType = ConnectionType.Unknown;
            ConnectionChanged?.Invoke(false);
            LogInfo("DualSense disconnected");
        }
    }

    private void CloseStream()
    {
        try { _stream?.Close(); } catch { /* ignore */ }
        _stream = null;
    }

    /// <summary>
    /// Detect connection type from the HID report size.
    /// </summary>
    private static ConnectionType DetectConnectionType(int bytesRead)
    {
        return bytesRead switch
        {
            UsbReportSize => ConnectionType.USB,
            BluetoothReportSize => ConnectionType.Bluetooth,
            _ => ConnectionType.Unknown
        };
    }

    /// <summary>
    /// Parse a raw HID report into a ControllerState.
    /// Callers must guarantee connType is USB or Bluetooth (unknown sizes are
    /// skipped in the input loop, never parsed).
    /// Internal so the hot path can be allocation-tested without hardware.
    /// </summary>
    internal static ControllerState ParseReport(byte[] buffer, ConnectionType connType)
    {
        return connType switch
        {
            ConnectionType.USB => ParseUsbReport(buffer),
            ConnectionType.Bluetooth => ParseBluetoothReport(buffer),
            _ => ParseUsbReport(buffer) // unreachable from InputLoop
        };
    }

    internal static ControllerState ParseUsbReport(byte[] buffer)
    {
        return new ControllerState
        {
            LeftStick = new StickState(
                NormalizeStick(buffer[UsbLsxOffset]),
                NormalizeStick(buffer[UsbLsyOffset])),
            RightStick = new StickState(
                NormalizeStick(buffer[UsbRsxOffset]),
                NormalizeStick(buffer[UsbRsyOffset])),
            L2 = NormalizeTrigger(buffer[UsbL2Offset]),
            R2 = NormalizeTrigger(buffer[UsbR2Offset]),
            Buttons = ParseButtonsUsb(buffer),
            DPad = ParseDPadUsb(buffer),
            Connection = ConnectionType.USB,
            Timestamp = DateTime.UtcNow
        };
    }

    internal static ControllerState ParseBluetoothReport(byte[] buffer)
    {
        return new ControllerState
        {
            LeftStick = new StickState(
                NormalizeStick(buffer[BtLsxOffset]),
                NormalizeStick(buffer[BtLsyOffset])),
            RightStick = new StickState(
                NormalizeStick(buffer[BtRsxOffset]),
                NormalizeStick(buffer[BtRsyOffset])),
            L2 = NormalizeTrigger(buffer[BtL2Offset]),
            R2 = NormalizeTrigger(buffer[BtR2Offset]),
            Buttons = ParseButtonsBluetooth(buffer),
            DPad = ParseDPadBluetooth(buffer),
            Connection = ConnectionType.Bluetooth,
            Timestamp = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Normalize stick byte to [-1.0, +1.0].
    /// Raw: 0x00=left, 0x80=center, 0xFF=right
    /// </summary>
    private static float NormalizeStick(byte raw)
    {
        return (raw - 128f) / 127f;
    }

    /// <summary>
    /// Normalize trigger byte to [0.0, 1.0].
    /// Raw: 0x00=released, 0xFF=pressed
    /// </summary>
    private static float NormalizeTrigger(byte raw)
    {
        return raw / 255f;
    }

    /// <summary>
    /// Parse D-Pad hat switch from USB report byte 8, bits 0-3.
    /// Values: 0=N, 1=NE, 2=E, 3=SE, 4=S, 5=SW, 6=W, 7=NW, 8=Neutral
    /// </summary>
    private static DPDirection ParseDPadUsb(byte[] buffer)
    {
        byte hat = (byte)(buffer[UsbButtons0Offset] & 0x0F);
        return hat switch
        {
            0 => DPDirection.Up,
            1 => DPDirection.UpRight,
            2 => DPDirection.Right,
            3 => DPDirection.DownRight,
            4 => DPDirection.Down,
            5 => DPDirection.DownLeft,
            6 => DPDirection.Left,
            7 => DPDirection.UpLeft,
            _ => DPDirection.Neutral
        };
    }

    /// <summary>
    /// Parse D-Pad from Bluetooth report byte 5, bits 0-3.
    /// </summary>
    private static DPDirection ParseDPadBluetooth(byte[] buffer)
    {
        byte hat = (byte)(buffer[BtButtons0Offset] & 0x0F);
        return hat switch
        {
            0 => DPDirection.Up,
            1 => DPDirection.UpRight,
            2 => DPDirection.Right,
            3 => DPDirection.DownRight,
            4 => DPDirection.Down,
            5 => DPDirection.DownLeft,
            6 => DPDirection.Left,
            7 => DPDirection.UpLeft,
            _ => DPDirection.Neutral
        };
    }

    /// <summary>
    /// Parse buttons from USB report bytes 8-10.
    /// Byte 8 bits 4-7: Square, Cross, Circle, Triangle
    /// Byte 9 bits 0-7: L1, R1, L2btn, R2btn, Create, Options, L3, R3
    /// Byte 10 bits 0-1: PS, Touchpad
    /// </summary>
    private static GamepadButton ParseButtonsUsb(byte[] buffer)
    {
        GamepadButton buttons = GamepadButton.None;

        // Face buttons from byte 8 bits 4-7
        byte b0 = buffer[UsbButtons0Offset];
        if ((b0 & 0x10) != 0) buttons |= GamepadButton.Square;
        if ((b0 & 0x20) != 0) buttons |= GamepadButton.Cross;
        if ((b0 & 0x40) != 0) buttons |= GamepadButton.Circle;
        if ((b0 & 0x80) != 0) buttons |= GamepadButton.Triangle;

        // Shoulder/action buttons from byte 9
        byte b1 = buffer[UsbButtons1Offset];
        if ((b1 & 0x01) != 0) buttons |= GamepadButton.L1;
        if ((b1 & 0x02) != 0) buttons |= GamepadButton.R1;
        if ((b1 & 0x10) != 0) buttons |= GamepadButton.Create;
        if ((b1 & 0x20) != 0) buttons |= GamepadButton.Options;
        if ((b1 & 0x40) != 0) buttons |= GamepadButton.L3;
        if ((b1 & 0x80) != 0) buttons |= GamepadButton.R3;

        // PS and Touchpad from byte 10
        byte b2 = buffer[UsbButtons2Offset];
        if ((b2 & 0x01) != 0) buttons |= GamepadButton.PS;
        if ((b2 & 0x02) != 0) buttons |= GamepadButton.Touchpad;

        return buttons;
    }

    /// <summary>
    /// Parse buttons from Bluetooth report bytes 5-7.
    /// Same bit layout as USB but at different byte offsets.
    /// </summary>
    private static GamepadButton ParseButtonsBluetooth(byte[] buffer)
    {
        GamepadButton buttons = GamepadButton.None;

        // Face buttons from byte 5 bits 4-7
        byte b0 = buffer[BtButtons0Offset];
        if ((b0 & 0x10) != 0) buttons |= GamepadButton.Square;
        if ((b0 & 0x20) != 0) buttons |= GamepadButton.Cross;
        if ((b0 & 0x40) != 0) buttons |= GamepadButton.Circle;
        if ((b0 & 0x80) != 0) buttons |= GamepadButton.Triangle;

        // Shoulder/action buttons from byte 6
        byte b1 = buffer[BtButtons1Offset];
        if ((b1 & 0x01) != 0) buttons |= GamepadButton.L1;
        if ((b1 & 0x02) != 0) buttons |= GamepadButton.R1;
        if ((b1 & 0x10) != 0) buttons |= GamepadButton.Create;
        if ((b1 & 0x20) != 0) buttons |= GamepadButton.Options;
        if ((b1 & 0x40) != 0) buttons |= GamepadButton.L3;
        if ((b1 & 0x80) != 0) buttons |= GamepadButton.R3;

        // PS and Touchpad from byte 7
        byte b2 = buffer[BtButtons2Offset];
        if ((b2 & 0x01) != 0) buttons |= GamepadButton.PS;
        if ((b2 & 0x02) != 0) buttons |= GamepadButton.Touchpad;

        return buttons;
    }

    private static void LogInfo(string msg) => Logging.Info($"[DualSense] {msg}");
    private static void LogWarn(string msg) => Logging.Warn($"[DualSense] {msg}");
    private static void LogError(Exception ex, string msg) => Logging.Error(ex, $"[DualSense] {msg}");
}
