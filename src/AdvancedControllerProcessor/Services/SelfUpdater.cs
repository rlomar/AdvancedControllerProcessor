using System.Diagnostics;
using System.IO;
using System.Net.Http;

namespace AdvancedControllerProcessor.Services;

/// <summary>
/// In-place self-updater for the portable single-file exe.
///
/// Flow (no admin needed, no duplicate copies):
///   1. Download the new exe next to the current one as "&lt;exe&gt;.downloading"
///   2. Rename the RUNNING exe to "&lt;exe&gt;.old_&lt;ticks&gt;" (Windows allows renaming a running exe)
///   3. Rename the downloaded file into place
///   4. Start the new version and exit
///   5. On next startup, CleanupLeftovers() deletes stale .old/.downloading files
///
/// Any failure rolls back and throws; the caller falls back to opening the browser.
/// </summary>
public static class SelfUpdater
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };

    /// <summary>Delete leftover partial/old exes from previous updates. Safe at startup.</summary>
    public static void CleanupLeftovers()
    {
        string? dir = Path.GetDirectoryName(Environment.ProcessPath);
        if (dir is null || !Directory.Exists(dir))
            return;

        foreach (string pattern in new[] { "*.exe.old_*", "*.exe.downloading" })
        {
            foreach (string file in Directory.EnumerateFiles(dir, pattern))
                TryDeleteWithRetry(file);
        }
    }

    /// <summary>
    /// Download <paramref name="info"/>'s exe, swap it with the running file,
    /// launch the new build, then return true. Caller must shut down the app.
    /// </summary>
    public static async Task<bool> ApplyUpdateAsync(
        UpdateInfo info, Action<string>? progress = null, CancellationToken ct = default)
    {
        string currentExe = Environment.ProcessPath
            ?? throw new InvalidOperationException("Cannot determine running exe path");

        string dir = Path.GetDirectoryName(currentExe)!;
        string fileName = Path.GetFileName(currentExe);
        string downloading = Path.Combine(dir, fileName + ".downloading");
        string oldFile = Path.Combine(dir, $"{fileName}.old_{DateTime.Now.Ticks}");
        string newExe = Path.Combine(dir, fileName);

        progress?.Invoke($"Downloading update v{info.Version}…");
        long totalBytes = await DownloadToFileAsync(info.DownloadUrl, downloading, progress, ct);

        // Sanity check: a real build is tens of MB; reject truncated/HTML responses.
        if (new FileInfo(downloading).Length < 10_000_000)
        {
            TryDelete(downloading);
            throw new IOException("Downloaded update is incomplete");
        }

        try
        {
            progress?.Invoke("Installing update…");

            // 1) Park the running exe aside. Renaming a running exe is legal on Windows.
            if (File.Exists(oldFile))
                File.Delete(oldFile);
            File.Move(currentExe, oldFile);

            try
            {
                // 2) Promote the download into place (same volume → instant rename).
                File.Move(downloading, newExe);
            }
            catch
            {
                // Roll back so the app stays runnable.
                TryMoveWithRetry(oldFile, currentExe);
                throw;
            }
        }
        catch
        {
            TryDelete(downloading);
            throw;
        }

        progress?.Invoke($"Restarting as v{info.Version}…");
        using (var _ = Process.Start(new ProcessStartInfo(newExe) { UseShellExecute = true }))
        {
        }

        _ = totalBytes;
        return true;
    }

    private static async Task<long> DownloadToFileAsync(
        string url, string targetPath, Action<string>? progress, CancellationToken ct)
    {
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        long? total = response.Content.Headers.ContentLength;
        var buffer = new byte[81920];
        long written = 0;
        int lastReportedPct = -1;

        await using var httpStream = await response.Content.ReadAsStreamAsync(ct);
        await using var file = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None);
        int read;
        while ((read = await httpStream.ReadAsync(buffer, ct)) > 0)
        {
            await file.WriteAsync(buffer.AsMemory(0, read), ct);
            written += read;

            if (total is > 0 && progress is not null)
            {
                int pct = (int)(written * 100 / total.Value);
                if (pct != lastReportedPct)
                {
                    lastReportedPct = pct;
                    progress?.Invoke($"Downloading update… {pct}%");
                }
            }
        }

        return written;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
    }

    private static void TryMoveWithRetry(string from, string to, int attempts = 5)
    {
        for (int i = 0; i < attempts; i++)
        {
            try
            {
                if (!File.Exists(from)) return;
                File.Move(from, to, overwrite: true);
                return;
            }
            catch when (i < attempts - 1)
            {
                Thread.Sleep(300); // AV scanners briefly lock fresh files
            }
        }
    }

    private static void TryDeleteWithRetry(string path, int attempts = 3)
    {
        for (int i = 0; i < attempts; i++)
        {
            try
            {
                if (!File.Exists(path)) return;
                File.Delete(path);
                return;
            }
            catch when (i < attempts - 1)
            {
                Thread.Sleep(300);
            }
            catch
            {
                // Locked by something else — skip silently, retried next startup.
            }
        }
    }
}
