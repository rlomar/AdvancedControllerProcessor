using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using AdvancedControllerProcessor.Services;

namespace AdvancedControllerProcessor;

/// <summary>
/// Blocking requirements gate. Shown instead of the main window when a
/// mandatory runtime component (ViGEmBus) is missing. The user must install
/// it — closing this window without satisfying the requirement aborts the app.
///
/// Returns DialogResult=true only when every mandatory requirement passes.
/// </summary>
public partial class RequirementsWindow : Window
{
    private List<RequirementStatus> _requirements = new();
    private bool _checkedOnce;
    private bool _finishing;

    public RequirementsWindow()
    {
        InitializeComponent();
        Activated += OnWindowActivated;
    }

    public RequirementsWindow(List<RequirementStatus> requirements) : this()
    {
        _requirements = requirements;
        Loaded += (_, _) => RenderRequirements();
    }

    // ── Rendering ────────────────────────────────────────────

    private void RenderRequirements()
    {
        RequirementsList.ItemsSource = null;
        RequirementsList.ItemsSource = _requirements;

        int missingMandatory = _requirements.Count(r => r.Mandatory && !r.Installed);
        int missingOptional = _requirements.Count(r => !r.Mandatory && !r.Installed);

        if (missingMandatory > 0)
        {
            StatusText.Text = missingMandatory == 1
                ? "1 required component is missing — download and install it to continue."
                : $"{missingMandatory} required components are missing — install them to continue.";
            StatusText.Foreground = FindResource("WarningBrush") as System.Windows.Media.Brush
                ?? System.Windows.Media.Brushes.Orange;
        }

        if (!_checkedOnce)
            _checkedOnce = true;

        if (missingMandatory == 0)
            BeginFinish(missingOptional);
    }

    /// <summary>Silent re-check when the user returns from the installer.</summary>
    private void OnWindowActivated(object? sender, EventArgs e)
    {
        if (_finishing || !_checkedOnce)
            return;

        var fresh = RequirementsChecker.CheckAll();
        bool changed = fresh.Zip(_requirements, (a, b) => a.Installed != b.Installed).Any(x => x);
        _requirements = fresh;

        if (changed)
            RenderRequirements();
    }

    private void BeginFinish(int missingOptional)
    {
        if (_finishing) return;
        _finishing = true;

        StatusText.Text = missingOptional > 0
            ? "All required components installed! Starting…"
            : "Everything is ready — starting…";
        StatusText.Foreground = FindResource("SuccessBrush") as System.Windows.Media.Brush
            ?? System.Windows.Media.Brushes.Green;

        RecheckBtn.IsEnabled = false;
        ExitBtn.IsEnabled = false;

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            DialogResult = true;
            Close();
        };
        timer.Start();
    }

    // ── Actions ──────────────────────────────────────────────

    private void OnDownloadClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: string url } || string.IsNullOrEmpty(url))
            return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                $"Could not open the browser:\n{ex.Message}\n\nOpen this link manually:\n{url}",
                "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnRecheckClick(object sender, RoutedEventArgs e)
    {
        _requirements = RequirementsChecker.CheckAll();
        RenderRequirements();
    }

    private void OnExitClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
