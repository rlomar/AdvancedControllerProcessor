using System.Net;
using System.Text;
using AdvancedControllerProcessor.Models;
using AdvancedControllerProcessor.Services;
using Xunit;

namespace AdvancedControllerProcessor.Tests;

/// <summary>
/// License activation/validation against the backend — all network traffic is
/// scripted through a fake HttpMessageHandler, so no test touches Supabase.
/// </summary>
public class LicenseServiceTests : IDisposable
{
    private readonly string _configDir;

    public LicenseServiceTests()
    {
        _configDir = Path.Combine(Path.GetTempPath(), "acp_license_tests", Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_configDir, recursive: true); } catch { /* temp cleanup */ }
    }

    // ── Response mapping ──────────────────────────────────

    [Theory]
    [InlineData("\"OK\"", LicenseStatus.Ok)]
    [InlineData("\"NOT_FOUND\"", LicenseStatus.NotFound)]
    [InlineData("\"REVOKED\"", LicenseStatus.Revoked)]
    [InlineData("\"DEVICE_LIMIT\"", LicenseStatus.DeviceLimit)]
    [InlineData("\"DEVICE_MISMATCH\"", LicenseStatus.DeviceMismatch)]
    [InlineData("\"INVALID\"", LicenseStatus.InvalidFormat)]
    [InlineData("\"OK\\n\"", LicenseStatus.Ok)] // trailing newline tolerated
    [InlineData("null", LicenseStatus.NetworkError)]
    [InlineData("<html>oops</html>", LicenseStatus.NetworkError)]
    [InlineData("", LicenseStatus.NetworkError)]
    public void MapResult_CoversEveryServerResponse(string body, LicenseStatus expected)
    {
        Assert.Equal(expected, LicenseService.MapResult(body));
    }

    // ── Activation ────────────────────────────────────────

    [Fact]
    public async Task ActivateAsync_BadFormat_NeverTouchesNetwork()
    {
        var (service, handler) = CreateService();

        var status = await service.ActivateAsync("nope");

        Assert.Equal(LicenseStatus.InvalidFormat, status);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task ActivateAsync_Ok_SavesNormalizedKey()
    {
        var (service, handler) = CreateService("\"OK\"");

        var status = await service.ActivateAsync("abcd-efgh-jkmn-pqrs");

        Assert.Equal(LicenseStatus.Ok, status);
        Assert.Equal(1, handler.CallCount);
        Assert.Equal("ABCDEFGHJKMNPQRS", service.SavedKey);

        // Request must hit activate_license with hashed payloads.
        Assert.Contains("activate_license", handler.LastUrl);
        string body = handler.LastBody!;
        Assert.Contains("p_key_hash", body);
        Assert.Contains("p_device_hash", body);
        // The plaintext key must NEVER appear on the wire.
        Assert.DoesNotContain("ABCDEFGHJKMNPQRS", body);
    }

    [Fact]
    public async Task ActivateAsync_Revoked_ReturnsRevokedWithoutSaving()
    {
        var (service, _) = CreateService("\"REVOKED\"");

        var status = await service.ActivateAsync("ABCDEFGHJKMNPQRS");

        Assert.Equal(LicenseStatus.Revoked, status);
        Assert.Null(service.SavedKey); // failed activation must not persist
    }

    // ── Validation ────────────────────────────────────────

    [Fact]
    public async Task ValidateSaved_WithoutSavedKey_ShortCircuitsAsNotFound()
    {
        var (service, handler) = CreateService();

        var status = await service.ValidateSavedAsync();

        Assert.Equal(LicenseStatus.NotFound, status);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task ValidateSaved_AfterActivation_ChecksSameHashes()
    {
        var (service, handler) = CreateService("\"OK\"");
        await service.ActivateAsync("ABCDEFGHJKMNPQRS");

        handler.ScriptedBody = "\"OK\"";
        var status = await service.ValidateSavedAsync();

        Assert.Equal(LicenseStatus.Ok, status);
        Assert.Equal(2, handler.CallCount);
        Assert.Contains("validate_license", handler.LastUrl);
    }

    [Fact]
    public async Task TransportFailure_MapsToNetworkError()
    {
        var (service, handler) = CreateService();
        await service.ActivateAsync("ABCDEFGHJKMNPQRS"); // seed a saved key

        handler.TransientFailures = 1; // next call simulates an outage

        var status = await service.ValidateSavedAsync();

        Assert.Equal(LicenseStatus.NetworkError, status);
    }

    // ── Grace window ──────────────────────────────────────

    [Fact]
    public async Task GraceWindow_RetriesTransientThenSucceeds()
    {
        var (service, handler) = CreateService();
        await service.ActivateAsync("ABCDEFGHJKMNPQRS"); // seed a saved key

        handler.TransientFailures = 2; // two outages, then success

        var status = await service.ValidateWithGraceAsync(
            grace: TimeSpan.FromSeconds(30),
            retryDelays: [TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(1)]);

        Assert.Equal(LicenseStatus.Ok, status);
        Assert.True(handler.CallCount >= 4, $"expected retries, calls={handler.CallCount}");
    }

    [Fact]
    public async Task GraceWindow_HardFailureReturnsImmediately()
    {
        var (service, handler) = CreateService();

        // Saved key exists → validate → server says REVOKED.
        await service.ActivateAsync("ABCDEFGHJKMNPQRS");
        handler.ScriptedBody = "\"REVOKED\"";
        int before = handler.CallCount;

        var status = await service.ValidateWithGraceAsync(
            grace: TimeSpan.FromMinutes(5),
            retryDelays: [TimeSpan.FromMilliseconds(1)]);

        Assert.Equal(LicenseStatus.Revoked, status);
        Assert.Equal(before + 1, handler.CallCount); // exactly one call, zero retries
    }

    // ── Plumbing ──────────────────────────────────────────

    private (LicenseService Service, ScriptedHandler Handler) CreateService(
        string scriptedBody = "\"OK\"")
    {
        var handler = new ScriptedHandler(scriptedBody);
        var client = new SupabaseClient(
            "https://unit-test.invalid/", "test-key", handler);
        var service = new LicenseService(
            new ConfigurationService(_configDir), client);
        return (service, handler);
    }

    /// <summary>Scriptable HttpMessageHandler with injectable transient outages.</summary>
    private sealed class ScriptedHandler(string body) : HttpMessageHandler
    {
        public int CallCount;
        public string? LastUrl;
        public string? LastBody;
        public string ScriptedBody = body;

        /// <summary>Number of upcoming calls that fail with a 503 before succeeding.</summary>
        public int TransientFailures;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            if (TransientFailures > 0)
            {
                TransientFailures--;
                throw new HttpRequestException("simulated outage",
                    inner: null, statusCode: HttpStatusCode.ServiceUnavailable);
            }

            LastUrl = request.RequestUri?.ToString();
            if (request.Content is not null)
                LastBody = await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(ScriptedBody, Encoding.UTF8, "application/json")
            };
        }
    }
}
