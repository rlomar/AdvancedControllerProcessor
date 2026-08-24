using System.Net.Http;
using System.Text;

namespace AdvancedControllerProcessor.Services;

/// <summary>
/// Endpoint configuration for the license backend (Supabase project).
///
/// The publishable/anon key is safe to embed: row-level security denies all
/// direct table access, and the only callable RPCs are activate_license and
/// validate_license. Administrative operations use the secret key which is
/// NEVER distributed — it lives only in the owner's License Manager tool.
/// </summary>
internal static class LicenseBackend
{
    public const string SupabaseUrl = "https://okrjquscnfdmanpzsnsk.supabase.co";
    public const string PublishableKey = "sb_publishable_DjWESb5yZ8kn1DZOV-6bsw_jb5EJNFH";
}

/// <summary>
/// Minimal PostgREST client for invoking remote procedure calls on the
/// Supabase backend. Uses System.Text.Json like <see cref="UpdateChecker"/>
/// and a single shared HttpClient to avoid socket exhaustion.
/// </summary>
public sealed class SupabaseClient
{
    private static readonly HttpClient SharedHttp = CreateClient();

    private readonly HttpClient _http;
    private readonly string _url;
    private readonly string _apiKey;

    /// <param name="httpHandler">
    /// Optional custom handler — used by unit tests to script responses.
    /// Production callers omit it and share one static client.
    /// </param>
    public SupabaseClient(string url, string apiKey, HttpMessageHandler? httpHandler = null)
    {
        _url = url.TrimEnd('/');
        _apiKey = apiKey;
        _http = httpHandler is null
            ? SharedHttp
            : new HttpClient(httpHandler) { Timeout = TimeSpan.FromSeconds(10) };
    }

    public static SupabaseClient Default { get; } =
        new(LicenseBackend.SupabaseUrl, LicenseBackend.PublishableKey);

    /// <summary>
    /// Invoke a Postgres RPC function with a JSON payload.
    /// Returns the raw response body (JSON-encoded scalar for text functions).
    /// Throws HttpRequestException on non-success status codes.
    /// </summary>
    public async Task<string> InvokeRpcAsync(
        string functionName, object payload, CancellationToken ct = default)
    {
        string requestUri = $"{_url}/rest/v1/rpc/{functionName}";

        using var content = new StringContent(
            System.Text.Json.JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");

        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = content
        };
        request.Headers.Add("apikey", _apiKey);
        request.Headers.Authorization = new System.Net.Http.Headers
            .AuthenticationHeaderValue("Bearer", _apiKey);

        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);

        string body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"RPC {functionName} failed: {(int)response.StatusCode} {response.ReasonPhrase}");

        return body;
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("AdvancedControllerProcessor/1.5");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        return client;
    }
}
