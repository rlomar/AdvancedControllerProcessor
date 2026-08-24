using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using AdvancedControllerProcessor.Helpers;
using AdvancedControllerProcessor.Models;
using AdvancedControllerProcessor.Services;

namespace AdvancedControllerProcessor;

/// <summary>
/// Blocking activation dialog. Shown by the startup gate when no valid
/// license exists, and re-shown mid-session when a key is revoked.
/// Closes with DialogResult=true only after a successful activation.
/// </summary>
public partial class ActivationWindow : Window
{
    private readonly LicenseService _licenseService;
    private bool _activating;

    public ActivationWindow(LicenseService licenseService, string? presetMessage = null)
    {
        InitializeComponent();
        _licenseService = licenseService;

        DeviceIdText.Text = HardwareId.GetShortDeviceId();

        // Prefill the saved key so a transient failure at startup just needs
        // one click on Activate instead of retyping everything.
        if (_licenseService.SavedKey is { } saved)
            KeyInput.Text = LicenseCrypto.FormatKeyGrouped(saved);

        if (!string.IsNullOrEmpty(presetMessage))
            ShowStatus(presetMessage, isWarning: true);
    }

    /// <summary>User-friendly text for every terminal status.</summary>
    public static string Describe(LicenseStatus status) => status switch
    {
        LicenseStatus.Ok => "Activated successfully.",
        LicenseStatus.NotFound => "This key was not recognized. Check it and try again.",
        LicenseStatus.Revoked => "This key has been revoked and can no longer be used.",
        LicenseStatus.DeviceLimit =>
            "This key is already bound to another device (1 device per key). " +
            "Contact Blank RL to move it to this PC.",
        LicenseStatus.DeviceMismatch =>
            "This key belongs to a different device. Contact Blank RL to reset its device binding.",
        LicenseStatus.InvalidFormat =>
            "The key format looks wrong — it should look like XXXX-XXXX-XXXX-XXXX.",
        LicenseStatus.NetworkError =>
            "Could not reach the activation server. Check your internet connection and try again.",
        _ => "Unexpected response. Please try again."
    };

    private void OnActivateClick(object sender, RoutedEventArgs e) => _ = ActivateAsync();

    private void OnKeyDownHandler(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && ActivateBtn.IsEnabled && !_activating)
            _ = ActivateAsync();
    }

    private void OnKeyTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) =>
        ActivateBtn.IsEnabled = !string.IsNullOrWhiteSpace(KeyInput.Text) && !_activating;

    private async Task ActivateAsync()
    {
        if (_activating)
            return;

        _activating = true;
        ActivateBtn.IsEnabled = false;
        ExitBtn.IsEnabled = false;
        ProgressPanel.Visibility = Visibility.Visible;
        StatusText.Visibility = Visibility.Collapsed;

        LicenseStatus status;
        try
        {
            status = await _licenseService.ActivateAsync(KeyInput.Text);
        }
        catch (Exception ex)
        {
            Logging.Error(ex, "[Activation] Unexpected failure during activation");
            status = LicenseStatus.NetworkError;
        }

        ProgressPanel.Visibility = Visibility.Collapsed;
        ExitBtn.IsEnabled = true;
        _activating = false;
        OnKeyTextChanged(this, null!); // re-evaluate button state

        if (status == LicenseStatus.Ok)
        {
            ShowStatus(Describe(status), isWarning: false, success: true);
            await Task.Delay(450); // let the user see the success flash
            DialogResult = true;
            Close();
            return;
        }

        bool transient = status.IsTransient();
        ShowStatus(Describe(status), isWarning: !transient);
        KeyInput.SelectAll();
        KeyInput.Focus();
    }

    private void OnCopyDeviceClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(HardwareId.GetShortDeviceId());
            CopyDeviceBtn.Content = "Copied ✓";
            CopyDeviceBtn.Foreground = new SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter
                    .ConvertFromString("#10B981"));
        }
        catch (Exception ex)
        {
            Logging.Warn($"[Activation] Clipboard copy failed: {ex.Message}");
        }
    }

    private void OnExitClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void ShowStatus(string message, bool isWarning, bool success = false)
    {
        StatusText.Text = message;
        StatusText.Foreground = success
            ? FindResource("SuccessBrush") as Brush
            : isWarning
                ? FindResource("DangerBrush") as Brush
                : FindResource("WarningBrush") as Brush;
        StatusText.Visibility = Visibility.Visible;
    }
}
