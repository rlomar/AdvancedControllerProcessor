using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using AdvancedControllerProcessor.Helpers;
using AdvancedControllerProcessor.Models;

namespace AdvancedControllerProcessor;

public partial class App : Application
{
    private static readonly string LogDirectory =
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");

    private static readonly string ProfilesDirectory =
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Profiles");

    public static string LogFilePath { get; } =
        Path.Combine(LogDirectory, "app.log");

    public static string ProfilesPath { get; } = ProfilesDirectory;

    /// <summary>
    /// Shared license/config services created by the startup gate and reused
    /// by MainViewModel so every component reads/writes one consistent state.
    /// </summary>
    internal static Services.LicenseService? CurrentLicenseService { get; private set; }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        EnsureDirectories();
        Logging.Initialize(LogFilePath);
        Logging.Info("Application starting");
        Logging.Info($"Advanced Controller Processor v{GetAppVersion()} — Blank RL");

        // Keep the input thread responsive during long gaming sessions: without
        // this, Windows progressively deprioritizes the process under load and
        // input delay creeps back in.
        TryElevateProcessPriority();

        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        ExtractDefaultProfileIfMissing();

        // ── Mandatory requirements gate ────────────────────────
        var requirements = Services.RequirementsChecker.CheckAll();
        bool missing = requirements.Any(r => r.Mandatory && !r.Installed);

        if (missing)
        {
            Logging.Warn("Mandatory runtime requirements are missing — blocking startup");
            var reqWindow = new RequirementsWindow(requirements);
            bool satisfied = reqWindow.ShowDialog() == true;

            if (!satisfied)
            {
                Logging.Info("Startup aborted: mandatory requirements were not installed");
                Shutdown(1);
                return;
            }

            Logging.Info("Mandatory requirements installed — continuing startup");
        }

        // ── Mandatory update gate ───────────────────────────────
        // Any build older than the newest release (or below the
        // update-policy.json floor) cannot run: it must self-update or exit.
        // Offline / failed checks never block startup.
        try
        {
            var requiredUpdate = await Services.UpdateChecker.GetRequiredUpdateAsync();
            if (requiredUpdate is not null)
            {
                Logging.Warn(
                    $"Build v{GetAppVersion()} is outdated — update to " +
                    $"v{requiredUpdate.RequiredVersion.ToString(3)} enforced");
                var gate = new MandatoryUpdateWindow(requiredUpdate);
                gate.ShowDialog();
                Logging.Info("Exiting after mandatory-update gate");
                Shutdown(1);
                return;
            }
        }
        catch (Exception ex)
        {
            Logging.Error(ex, "Mandatory-update check failed — continuing startup");
        }

        // ── License activation gate ─────────────────────────────
        // Strictly online: a valid key bound to this device is required on
        // every startup. Transient network failures get a grace window of
        // retries; definitive failures (revoked / wrong device) block at once.
        var configService = new Services.ConfigurationService(AppDomain.CurrentDomain.BaseDirectory);
        var licenseService = new Services.LicenseService(configService);
        CurrentLicenseService = licenseService;

        try
        {
            if (licenseService.SavedKey is null)
            {
                Logging.Info("[License] No saved key — showing activation window");
                var activation = new ActivationWindow(licenseService);
                if (activation.ShowDialog() != true)
                {
                    Logging.Info("Startup aborted: program was not activated");
                    Shutdown(1);
                    return;
                }
                Logging.Info("[License] Program activated successfully");
            }
            else
            {
                LicenseStatus status = await licenseService.ValidateWithGraceAsync();
                if (status != LicenseStatus.Ok)
                {
                    Logging.Warn($"[License] Startup validation failed: {status}");
                    var reactivation = new ActivationWindow(
                        licenseService, ActivationWindow.Describe(status));
                    if (reactivation.ShowDialog() != true)
                    {
                        Logging.Info("Startup aborted: license validation failed");
                        Shutdown(1);
                        return;
                    }
                    Logging.Info("[License] Re-activated successfully");
                }
                else
                {
                    Logging.Info("[License] Startup validation OK");
                }
            }
        }
        catch (Exception ex)
        {
            Logging.Error(ex, "[License] Activation gate crashed — blocking startup");
            MessageBox.Show(
                "Activation check failed unexpectedly. Please restart the program.",
                "Activation Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        var main = new MainWindow();
        MainWindow = main;
        main.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Logging.Info("Application shutting down");
        Logging.Dispose();
        base.OnExit(e);
    }

    /// <summary>
    /// Writes the embedded Default.json profile next to the executable when missing.
    /// Needed for single-file portable builds where content files are not deployed.
    /// </summary>
    private static void ExtractDefaultProfileIfMissing()
    {
        try
        {
            string target = Path.Combine(ProfilesDirectory, "Default.json");
            if (File.Exists(target))
                return;

            var assembly = Assembly.GetExecutingAssembly();
            string resourceName = $"{assembly.GetName().Name}.Profiles.Default.json";

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is null)
            {
                Logging.Warn($"Embedded default profile not found: {resourceName}");
                return;
            }

            using var file = File.Create(target);
            stream.CopyTo(file);
            Logging.Info("Extracted embedded Default.json profile");
        }
        catch (Exception ex)
        {
            Logging.Error(ex, "Failed to extract default profile");
        }
    }

    private static string GetAppVersion()
    {
        try
        {
            return Assembly.GetExecutingAssembly()
                .GetName().Version?.ToString(3) ?? "1.0.0";
        }
        catch
        {
            return "1.0.0";
        }
    }

    private static void EnsureDirectories()
    {
        Directory.CreateDirectory(LogDirectory);
        Directory.CreateDirectory(ProfilesDirectory);
    }

    private static void TryElevateProcessPriority()
    {
        try
        {
            using var process = System.Diagnostics.Process.GetCurrentProcess();
            process.PriorityClass = System.Diagnostics.ProcessPriorityClass.High;
            process.PriorityBoostEnabled = true;
            Logging.Info("Process priority set to High (priority boost enabled)");
        }
        catch (Exception ex)
        {
            Logging.Warn($"Could not raise process priority: {ex.Message}");
        }
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var ex = e.ExceptionObject as Exception;
        Logging.Fatal(ex, "Unhandled exception");
        MessageBox.Show(
            $"An unexpected error occurred:\n{ex?.Message}",
            "Error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Logging.Error(e.Exception, "Dispatcher unhandled exception");
        e.Handled = true;
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Logging.Error(e.Exception, "Unobserved task exception");
        e.SetObserved();
    }
}
