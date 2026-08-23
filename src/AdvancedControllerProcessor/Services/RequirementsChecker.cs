using Microsoft.Win32;

namespace AdvancedControllerProcessor.Services;

/// <summary>
/// Status of a single runtime requirement (driver, service, etc.).
/// </summary>
public sealed record RequirementStatus
{
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public bool Installed { get; init; }
    public bool Mandatory { get; init; } = true;
    public string DownloadUrl { get; init; } = "";
    public string InstallHint { get; init; } = "";
}

/// <summary>
/// Checks whether mandatory runtime dependencies are installed on this machine.
///
/// ViGEmBus  — kernel driver that provides virtual Xbox 360 / DualShock 4 controllers.
///             Mandatory: the core feature of this app cannot work without it.
/// HidHide   — optional driver used to hide the physical controller from games
///             when the virtual one takes over.
///
/// Detection is done via the HKLM\SYSTEM\CurrentControlSet\Services registry key:
/// every installed Windows kernel driver has a sub-key there regardless of
/// whether the service is currently running.
/// </summary>
public static class RequirementsChecker
{
    public const string VigemBusDownloadUrl = "https://vigem.org/downloads/";
    public const string HidHideDownloadUrl = "https://github.com/ViGEm/HidHide/releases/latest";

    /// <summary>Checks all known requirements.</summary>
    public static List<RequirementStatus> CheckAll()
    {
        return new List<RequirementStatus>
        {
            new()
            {
                Name = "ViGEmBus Driver",
                Description = "Required to create the virtual Xbox 360 / DualShock 4 controller.",
                Installed = IsDriverServiceInstalled("ViGEmBus"),
                Mandatory = true,
                DownloadUrl = VigemBusDownloadUrl,
                InstallHint = "Download 'ViGEmBus_Setup', run it, press Next until finished, then re-check."
            },
            new()
            {
                Name = "HidHide Driver",
                Description = "Optional: hides your real controller so games only see the virtual one.",
                Installed = IsDriverServiceInstalled("HidHide"),
                Mandatory = false,
                DownloadUrl = HidHideDownloadUrl,
                InstallHint = "Only needed if you enable controller hiding in settings."
            }
        };
    }

    /// <summary>Returns true only if every MANDATORY requirement is satisfied.</summary>
    public static bool AreMandatorySatisfied() =>
        CheckAll().Where(r => r.Mandatory).All(r => r.Installed);

    /// <summary>
    /// True when a driver/service with the given name exists under
    /// HKLM\SYSTEM\CurrentControlSet\Services.
    /// </summary>
    public static bool IsDriverServiceInstalled(string serviceName)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                $@"SYSTEM\CurrentControlSet\Services\{serviceName}");
            return key is not null;
        }
        catch
        {
            return false;
        }
    }
}
