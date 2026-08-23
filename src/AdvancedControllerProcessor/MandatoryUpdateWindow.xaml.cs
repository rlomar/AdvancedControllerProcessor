using System.Diagnostics;
using System.Windows;
using AdvancedControllerProcessor.Helpers;
using AdvancedControllerProcessor.Services;

namespace AdvancedControllerProcessor;

/// <summary>
/// Blocking mandatory-update gate. Shown modally before the main window when
/// the running build is older than the required version (newest release or
/// update-policy.json floor). The user can update in place, open the download
/// page, or exit — there is no way into the app on an outdated build.
/// Closing the window (X / Alt+F4) counts as exiting.
/// </summary>
public partial class MandatoryUpdateWindow : Window
{
    private readonly RequiredUpdate _required;
    private bool _isUpdating;

    public MandatoryUpdateWindow(RequiredUpdate required)
    {
        InitializeComponent();
        _required = required;

        InstalledText.Text = $"v{UpdateChecker.CurrentVersion.ToString(3)}";
        RequiredText.Text = $"v{required.RequiredVersion.ToString(3)}";

        if (!string.IsNullOrWhiteSpace(required.Message))
        {
            MessageText.Text = required.Message;
            MessageText.Visibility = Visibility.Visible;
        }
    }

    private async void OnUpdateNowClick(object sender, RoutedEventArgs e)
    {
        if (_isUpdating)
            return;

        _isUpdating = true;
        UpdateNowBtn.IsEnabled = false;
        DownloadPageBtn.IsEnabled = false;
        ExitBtn.IsEnabled = false;
        ProgressPanel.Visibility = Visibility.Visible;

        try
        {
            await SelfUpdater.ApplyUpdateAsync(
                _required.Info,
                progress => Dispatcher.Invoke(() => ProgressText.Text = progress ?? "Updating…"));

            Logging.Info($"Self-updated to v{_required.RequiredVersion.ToString(3)} via mandatory gate");
            Close(); // old instance exits; SelfUpdater already started the new one
        }
        catch (Exception ex)
        {
            Logging.Error(ex, $"Mandatory self-update to v{_required.RequiredVersion} failed");
            _isUpdating = false;

            ProgressPanel.Visibility = Visibility.Collapsed;
            UpdateNowBtn.IsEnabled = true;
            DownloadPageBtn.IsEnabled = true;
            ExitBtn.IsEnabled = true;

            MessageBox.Show(
                this,
                $"Automatic update failed:\n{ex.Message}\n\n" +
                "Use 'Open Download Page' to install the new version manually.",
                "Update failed",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void OnDownloadPageClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(_required.Info.ReleaseUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Logging.Error(ex, "Failed to open mandatory-update download URL");
        }
    }

    private void OnExitClick(object sender, RoutedEventArgs e) => Close();
}
