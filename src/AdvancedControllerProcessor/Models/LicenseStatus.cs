namespace AdvancedControllerProcessor.Models;

/// <summary>
/// Outcome of a license activation or validation attempt against the
/// license server. Values map 1:1 to the strings returned by the
/// activate_license / validate_license Postgres functions, plus client-side
/// outcomes (format rejection, network failure).
/// </summary>
public enum LicenseStatus
{
    /// <summary>Key valid and bound to this device.</summary>
    Ok,

    /// <summary>Key does not exist in the database.</summary>
    NotFound,

    /// <summary>Key existed but was revoked by the owner.</summary>
    Revoked,

    /// <summary>Key already bound to a different device (1-device limit).</summary>
    DeviceLimit,

    /// <summary>Key is valid but registered to another device.</summary>
    DeviceMismatch,

    /// <summary>Client-side format rejection — request never left the machine.</summary>
    InvalidFormat,

    /// <summary>Server could not be reached (offline, DNS, timeout, 5xx).</summary>
    NetworkError
}

/// <summary>Helpers for classifying license statuses.</summary>
public static class LicenseStatusExtensions
{
    /// <summary>
    /// True when the server gave a definitive answer that the key/device pair
    /// is not allowed. These block immediately — retrying cannot change them.
    /// </summary>
    public static bool IsHardFailure(this LicenseStatus status) => status is
        LicenseStatus.NotFound or
        LicenseStatus.Revoked or
        LicenseStatus.DeviceLimit or
        LicenseStatus.DeviceMismatch;

    /// <summary>
    /// True when the failure is transient (no connectivity / server hiccup).
    /// Worth retrying within a grace window before blocking the user.
    /// </summary>
    public static bool IsTransient(this LicenseStatus status) =>
        status == LicenseStatus.NetworkError;
}
