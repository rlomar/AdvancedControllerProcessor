using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace AdvancedControllerProcessor.Services;

/// <summary>Describes an available update published on GitHub Releases.</summary>
public sealed record UpdateInfo(Version Version, string ReleaseUrl, string DownloadUrl);

/// <summary>
/// Server-side policy floor fetched from update-policy.json in the repo.
/// Lets the publisher force-update (or emergency-block) builds regardless of
/// what the newest release tag is.
/// </summary>
public sealed record UpdatePolicy(Version MinimumVersion, string Message);

/// <summary>A mandatory update the running build must install before it can run.</summary>
public sealed record RequiredUpdate(Version RequiredVersion, UpdateInfo Info, string Message);

/// <summary>
/// Checks GitHub Releases for a newer published version of the app.
/// The app does NOT self-install updates; it notifies the user and opens
/// the release page / direct asset download URL.
/// Never throws: failures (offline, API limits) are treated as "no update".
/// </summary>
public static class UpdateChecker
{
    public const string RepoOwner = "rlomar";
    public const string RepoName = "AdvancedControllerProcessor";

    private const string ReleasesApiUrl =
        $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";

    // "HEAD" resolves the default branch — no need to hard-code "main".
    private const string PolicyUrl =
        $"https://raw.githubusercontent.com/{RepoOwner}/{RepoName}/HEAD/update-policy.json";

    public static Version CurrentVersion { get; } =
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"{RepoName}/{CurrentVersion}");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    /// <summary>
    /// Returns update details when the latest GitHub release is newer than the
    /// running build, otherwise null.
    /// </summary>
    public static async Task<UpdateInfo?> CheckForUpdateAsync()
    {
        try
        {
            using var response = await Http.GetAsync(ReleasesApiUrl);
            if (!response.IsSuccessStatusCode)
                return null;

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return ParseRelease(doc.RootElement);
        }
        catch (Exception)
        {
            return null; // Offline or API hiccup — silently ignore
        }
    }

    /// <summary>
    /// Mandatory-update evaluation for the running build. A build is outdated
    /// when it is older than EITHER the newest GitHub release OR the
    /// update-policy.json floor (whichever is higher). Returns null when the
    /// build is current — or when checks fail/offline, which never blocks
    /// startup.
    /// </summary>
    public static async Task<RequiredUpdate?> GetRequiredUpdateAsync(CancellationToken ct = default)
    {
        var latestTask = CheckForUpdateAsync();
        var policyTask = CheckPolicyAsync(ct);
        await Task.WhenAll(latestTask, policyTask);

        UpdateInfo? latest = await latestTask;
        UpdatePolicy? policy = await policyTask;

        Version? required = null;
        string message = string.Empty;

        if (latest is not null)
            required = latest.Version;

        if (policy is not null && (required is null || policy.MinimumVersion > required))
        {
            required = policy.MinimumVersion;
            message = policy.Message;
        }

        if (required is null || required <= CurrentVersion)
            return null;

        // Latest-release info unavailable (e.g. API down but policy file up):
        // fall back to the stable direct-download asset URL pattern so the
        // in-app self-updater still has something valid to fetch.
        var info = latest ?? new UpdateInfo(
            required,
            $"https://github.com/{RepoOwner}/{RepoName}/releases/latest",
            $"https://github.com/{RepoOwner}/{RepoName}/releases/latest/download/AdvancedControllerProcessor.exe");

        return new RequiredUpdate(required, info, message);
    }

    /// <summary>
    /// Fetches the server-side minimum-version floor. Null when absent,
    /// malformed or unreachable — a missing policy never blocks anyone.
    /// </summary>
    public static async Task<UpdatePolicy?> CheckPolicyAsync(CancellationToken ct = default)
    {
        try
        {
            using var response = await Http.GetAsync(PolicyUrl, ct);
            if (!response.IsSuccessStatusCode)
                return null;

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var root = doc.RootElement;

            if (!root.TryGetProperty("minimumVersion", out var minEl))
                return null;
            if (!Version.TryParse(minEl.GetString()?.TrimStart('v', 'V'), out var minimum))
                return null;

            string message = root.TryGetProperty("message", out var msgEl)
                ? msgEl.GetString() ?? string.Empty
                : string.Empty;

            return new UpdatePolicy(minimum, message);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Extracts version + URLs from a GitHub release API object.</summary>
    private static UpdateInfo? ParseRelease(JsonElement root)
    {
        string tag = root.TryGetProperty("tag_name", out var tagEl)
            ? tagEl.GetString()?.TrimStart('v', 'V') ?? string.Empty
            : string.Empty;

        if (!Version.TryParse(tag, out var latest))
            return null;

        if (latest <= CurrentVersion)
            return null;

        string releaseUrl = root.TryGetProperty("html_url", out var urlEl)
            ? urlEl.GetString() ?? $"https://github.com/{RepoOwner}/{RepoName}/releases/latest"
            : $"https://github.com/{RepoOwner}/{RepoName}/releases/latest";

        string downloadUrl = releaseUrl;
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                string name = asset.TryGetProperty("name", out var nameEl)
                    ? nameEl.GetString() ?? string.Empty : string.Empty;

                if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    downloadUrl = asset.TryGetProperty("browser_download_url", out var dlEl)
                        ? dlEl.GetString() ?? downloadUrl : downloadUrl;
                    break;
                }
            }
        }

        return new UpdateInfo(latest, releaseUrl, downloadUrl);
    }
}
