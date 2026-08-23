using System.Runtime.InteropServices;
using System.Windows.Input;
using AdvancedControllerProcessor.Helpers;

namespace AdvancedControllerProcessor.Services;

/// <summary>
/// Manages global system-wide hotkeys for the application.
/// Uses Win32 RegisterHotKey/UnregisterHotKey API.
///
/// Default hotkeys:
///   F8 — Toggle processing ON/OFF
///   F9 — Safe mode reset
/// </summary>
public sealed class HotkeyService : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private readonly Dictionary<int, Action> _registeredHotkeys = [];
    private bool _disposed;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    /// <summary>
    /// Register a global hotkey with the system.
    /// </summary>
    /// <param name="hotkeyId">Unique ID for this hotkey (0-10000).</param>
    /// <param name="key">The key to register (e.g., Key.F8).</param>
    /// <param name="callback">Action to invoke when hotkey is pressed.</param>
    /// <param name="windowHandle">Handle to the window for message processing.</param>
    public bool Register(int hotkeyId, Key key, Action callback, IntPtr windowHandle)
    {
        try
        {
            uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);
            if (!RegisterHotKey(windowHandle, hotkeyId, 0, vk))
            {
                Logging.Warn($"Failed to register hotkey ID={hotkeyId} key={key} (may be in use)");
                return false;
            }

            _registeredHotkeys[hotkeyId] = callback;
            Logging.Info($"Hotkey registered: {key} (ID={hotkeyId})");
            return true;
        }
        catch (Exception ex)
        {
            Logging.Error(ex, $"Failed to register hotkey {key}");
            return false;
        }
    }

    /// <summary>
    /// Process a Windows message. Call from WndProc.
    /// Returns true if the message was a hotkey.
    /// </summary>
    public bool ProcessMessage(int message, IntPtr wParam)
    {
        if (message != WM_HOTKEY)
            return false;

        int hotkeyId = wParam.ToInt32();

        if (_registeredHotkeys.TryGetValue(hotkeyId, out var callback))
        {
            try
            {
                callback();
            }
            catch (Exception ex)
            {
                Logging.Error(ex, $"Error in hotkey callback ID={hotkeyId}");
            }
            return true;
        }

        return false;
    }

    /// <summary>
    /// Unregister all hotkeys and clean up.
    /// </summary>
    public void UnregisterAll(IntPtr windowHandle)
    {
        foreach (var id in _registeredHotkeys.Keys)
        {
            try
            {
                UnregisterHotKey(windowHandle, id);
            }
            catch { /* ignore */ }
        }
        _registeredHotkeys.Clear();
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
        }
    }
}
