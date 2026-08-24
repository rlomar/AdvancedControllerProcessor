using System.Diagnostics;
using System.IO;

namespace AdvancedControllerProcessor.Helpers;

/// <summary>
/// Lightweight file logger. Only logs significant events (connect, disconnect, errors, profile changes).
/// Does NOT log input samples — that would kill performance.
/// Thread-safe via lock.
/// </summary>
public static class Logging
{
    private static readonly object _lock = new();
    private static StreamWriter? _writer;
    private static bool _initialized;

    public static void Initialize(string filePath)
    {
        lock (_lock)
        {
            try
            {
                var directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                _writer = new StreamWriter(filePath, append: true)
                {
                    AutoFlush = true
                };
                _initialized = true;
            }
            catch
            {
                // Logging failure should not crash the app
                _initialized = false;
            }
        }
    }

    public static void Dispose()
    {
        lock (_lock)
        {
            _writer?.Flush();
            _writer?.Dispose();
            _writer = null;
            _initialized = false;
        }
    }

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(Exception ex, string message) =>
        Write("ERROR", $"{message}: {ex.GetType().Name} - {ex.Message}{Environment.NewLine}{ex.StackTrace}");

    public static void Fatal(Exception? ex, string message) =>
        Write("FATAL", ex != null
            ? $"{message}: {ex.GetType().Name} - {ex.Message}{Environment.NewLine}{ex.StackTrace}"
            : message);

    private static void Write(string level, string message)
    {
        if (!_initialized) return;

        lock (_lock)
        {
            try
            {
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                var threadId = Environment.CurrentManagedThreadId;
                _writer?.WriteLine($"[{timestamp}] [{level}] [T{threadId}] {message}");
            }
            catch
            {
                // Swallow — logging should never crash the app
            }
        }
    }
}
