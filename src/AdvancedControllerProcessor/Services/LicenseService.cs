using System.Diagnostics;
using System.Net.Http;
using AdvancedControllerProcessor.Helpers;
using AdvancedControllerProcessor.Models;

namespace AdvancedControllerProcessor.Services;

/// <summary>
/// High-level license operations: activate a key on this device and
/// re-validate a previously activated one.
///
/// Threading: ActivateAsync/Validate* run on the UI thread (async I/O);
/// they never touch controller state.
/// </summary>
public sealed class LicenseService
{
    /// <summary>How long transient failures are retried before blocking.
    /// Covers Wi-Fi blips / DNS hiccups without letting a revoked user play.</summary>
    public static readonly TimeSpan DefaultGrace = TimeSpan.FromMinutes(2);

    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromSeconds(3),
        TimeSpan.FromSeconds(7),
        TimeSpan.FromSeconds(15),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromSeconds(45),
        TimeSpan.FromSeconds(60)
    ];

    private readonly SupabaseClient _client;
    private readonly ConfigurationService _config;

    public LicenseService(ConfigurationService config, SupabaseClient? client = null)
    {
        _config = config;
        _client = client ?? SupabaseClient.Default;
    }

    /// <summary>The normalized key saved locally, or null when none.</summary>
    public string? SavedKey
    {
        get
        {
            string saved = _config.Settings.LicenseKey;
            return string.IsNullOrWhiteSpace(saved) ? null : saved;
        }
    }

    /// <summary>
    /// Activate a user-entered key on THIS device. On success the normalized
    /// key is persisted to local settings so future startups skip typing it.
    /// </summary>
    public async Task<LicenseStatus> ActivateAsync(string rawInput, CancellationToken ct = default)
    {
        string normalized = LicenseCrypto.NormalizeKey(rawInput);
        if (!LicenseCrypto.IsValidFormat(normalized))
            return LicenseStatus.InvalidFormat;

        LicenseStatus status = await InvokeAsync("activate_license", normalized, ct)
            .ConfigureAwait(false);

        if (status == LicenseStatus.Ok)
            SaveKey(normalized);

        return status;
    }

    /// <summary>Validate the locally saved key + this device against the server.</summary>
    public async Task<LicenseStatus> ValidateSavedAsync(CancellationToken ct = default)
    {
        string? saved = SavedKey;
        if (saved is null)
            return LicenseStatus.NotFound;

        return await InvokeAsync("validate_license", saved, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Validate with a retry grace window for transient network failures.
    /// Hard failures (revoked, wrong device…) return immediately — no amount
    /// of retrying changes them.
    /// </summary>
    public async Task<LicenseStatus> ValidateWithGraceAsync(
        CancellationToken ct = default, TimeSpan? grace = null, TimeSpan[]? retryDelays = null)
    {
        TimeSpan window = grace ?? DefaultGrace;
        TimeSpan[] delays = retryDelays ?? RetryDelays;
        var sw = Stopwatch.StartNew();

        LicenseStatus status = await ValidateSavedAsync(ct).ConfigureAwait(false);

        for (int attempt = 0; status.IsTransient() && sw.Elapsed < window; attempt++)
        {
            TimeSpan delay = delays[Math.Min(attempt, delays.Length - 1)];
            try { await Task.Delay(delay, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }

            status = await ValidateSavedAsync(ct).ConfigureAwait(false);
        }

        return status;
    }

    private void SaveKey(string normalized) =>
        _config.Update(s => s.LicenseKey = normalized);

    /// <summary>
    /// Call an RPC with hashed inputs and map its text result to a status.
    /// Any transport/protocol failure collapses to NetworkError — callers see
    /// exactly two failure families: definitive (hard) and transient.
    /// </summary>
    private async Task<LicenseStatus> InvokeAsync(
        string functionName, string normalizedKey, CancellationToken ct)
    {
        string keyHash = LicenseCrypto.HashKey(normalizedKey);
        string deviceHash = HardwareId.GetDeviceHash();

        string body;
        try
        {
            body = await _client.InvokeRpcAsync(functionName,
                new { p_key_hash = keyHash, p_device_hash = deviceHash }, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
                                          or System.Text.Json.JsonException)
        {
            Logging.Warn($"[License] {functionName} transport failure: {ex.Message}");
            return LicenseStatus.NetworkError;
        }

        return MapResult(body);
    }

    /// <summary>Pure mapping of a PostgREST scalar-text response to a status.
    /// Internal for unit testing without network.</summary>
    internal static LicenseStatus MapResult(string responseBody)
    {
        string value;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(responseBody ?? "null");
            value = doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.String
                ? doc.RootElement.GetString()?.Trim().ToUpperInvariant() ?? string.Empty
                : string.Empty;
        }
        catch (System.Text.Json.JsonException)
        {
            return LicenseStatus.NetworkError; // malformed response = server-side problem
        }

        return value switch
        {
            "OK" => LicenseStatus.Ok,
            "NOT_FOUND" => LicenseStatus.NotFound,
            "REVOKED" => LicenseStatus.Revoked,
            "DEVICE_LIMIT" => LicenseStatus.DeviceLimit,
            "DEVICE_MISMATCH" => LicenseStatus.DeviceMismatch,
            "INVALID" => LicenseStatus.InvalidFormat,
            _ => LicenseStatus.NetworkError
        };
    }
}
