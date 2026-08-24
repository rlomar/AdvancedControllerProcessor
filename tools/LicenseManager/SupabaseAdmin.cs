using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace LicenseManager;

/// <summary>
/// Administrative Supabase access using the SECRET key. This key bypasses
/// row-level security, so the tool must never leave the owner's machine and
/// its config (which stores the secret) lives under %APPDATA%.
/// </summary>
public sealed class SupabaseAdmin
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly string _url;
    private readonly string _secret;

    public SupabaseAdmin(string url, string secret)
    {
        _url = url.TrimEnd('/');
        _secret = secret;
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("ACP-LicenseManager/1.5");
    }

    // ── Config persistence ────────────────────────────────

    public static (string Url, string Secret)? LoadConfig()
    {
        try
        {
            string path = ConfigPath();
            if (!File.Exists(path)) return null;

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            string url = doc.RootElement.GetProperty("url").GetString() ?? "";
            string secret = doc.RootElement.GetProperty("secret").GetString() ?? "";
            return url.Length > 0 && secret.Length > 0 ? (url, secret) : null;
        }
        catch
        {
            return null;
        }
    }

    public static void SaveConfig(string url, string secret)
    {
        string path = ConfigPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path,
            JsonSerializer.Serialize(new { url, secret }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string ConfigPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "LicenseManager", "config.json");

    // ── Operations ────────────────────────────────────────

    /// <summary>All licenses with their (zero or one) device activation embedded.</summary>
    public async Task<List<LicenseRow>> ListAsync()
    {
        string uri = $"{_url}/rest/v1/licenses" +
                     "?select=key_hash,label,created_at,revoked,activations(device_hash,activated_at)" +
                     "&order=created_at.desc";

        using var request = NewRequest(HttpMethod.Get, uri);
        string body = await SendAsync(request);

        var rows = JsonSerializer.Deserialize<List<RawRow>>(body, JsonOpts) ?? [];
        return rows.Select(r => new LicenseRow
        {
            KeyHash = r.KeyHash ?? "",
            Label = r.Label ?? "",
            CreatedAt = ParseDate(r.CreatedAt),
            Revoked = r.Revoked,
            DeviceShort = ShortDevice(r.Activations?.DeviceHash),
            ActivatedAt = r.Activations?.ActivatedAt is { } a ? ParseDate(a) : null
        }).ToList();
    }

    public async Task CreateAsync(string keyHash, string label)
    {
        var payload = new { key_hash = keyHash, label, revoked = false };
        using var request = NewRequest(HttpMethod.Post, $"{_url}/rest/v1/licenses");
        request.Content = new StringContent(
            JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        request.Headers.Add("Prefer", "return=minimal");
        await SendAsync(request);
    }

    public Task SetRevokedAsync(string keyHash, bool revoked) =>
        PatchLicense(keyHash, new { revoked });

    public async Task ResetDeviceAsync(string keyHash)
    {
        using var request = NewRequest(HttpMethod.Delete,
            $"{_url}/rest/v1/activations?key_hash=eq.{keyHash}");
        await SendAsync(request);
    }

    private Task PatchLicense(string keyHash, object body)
    {
        var request = NewRequest(HttpMethod.Patch, $"{_url}/rest/v1/licenses?key_hash=eq.{keyHash}");
        request.Content = new StringContent(
            JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        request.Headers.Add("Prefer", "return=minimal");
        return SendAsync(request);
    }

    // ── Plumbing ──────────────────────────────────────────

    private HttpRequestMessage NewRequest(HttpMethod method, string uri)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Add("apikey", _secret);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _secret);
        return request;
    }

    private async Task<string> SendAsync(HttpRequestMessage request)
    {
        using var response = await _http.SendAsync(request);
        string body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"{(int)response.StatusCode} {response.ReasonPhrase}: {body}");
        return body;
    }

    private static DateTime? ParseDate(string? iso) =>
        DateTime.TryParse(iso, out var dt) ? dt.ToLocalTime() : null;

    private static string ShortDevice(string? hash) => hash is { Length: >= 8 }
        ? $"{hash[..4].ToUpperInvariant()}-{hash[4..8].ToUpperInvariant()}"
        : "—";

    // ── DTOs ──────────────────────────────────────────────

    private sealed class RawRow
    {
        public string? KeyHash { get; set; }
        public string? Label { get; set; }
        public string? CreatedAt { get; set; }
        public bool Revoked { get; set; }
        public RawActivation? Activations { get; set; }
    }

    private sealed class RawActivation
    {
        public string? DeviceHash { get; set; }
        public string? ActivatedAt { get; set; }
    }
}

/// <summary>Display-ready license record.</summary>
public sealed class LicenseRow
{
    public string KeyHash { get; init; } = "";
    public string Label { get; init; } = "";
    public DateTime? CreatedAt { get; init; }
    public bool Revoked { get; set; }
    public string DeviceShort { get; init; } = "—";
    public DateTime? ActivatedAt { get; init; }

    public string HashShort => KeyHash.Length >= 10 ? KeyHash[..10] + "…" : KeyHash;
    public string StatusText => Revoked ? "REVOKED" : DeviceShort == "—" ? "UNUSED" : "ACTIVE";
    public string CreatedText => CreatedAt?.ToString("yyyy-MM-dd HH:mm") ?? "—";
    public string ActivatedText => ActivatedAt?.ToString("yyyy-MM-dd HH:mm") ?? "—";
}
