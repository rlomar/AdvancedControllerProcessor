using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace AdvancedControllerProcessor.Services;

/// <summary>Describes an available update published on GitHub Releases.</summary>
public sealed record UpdateInfo(Version Version, string ReleaseUrl, string DownloadUrl);

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
            using var response = await Http.GetAsync(
                $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest");

            if (!response.IsSuccessStatusCode)
                return null;

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = doc.RootElement;

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
        catch (Exception)
        {
            return null; // Offline or API hiccup — silently ignore
        }
    }
}
