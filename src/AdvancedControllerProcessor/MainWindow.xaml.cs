using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using AdvancedControllerProcessor.Helpers;
using AdvancedControllerProcessor.Services;
using AdvancedControllerProcessor.ViewModels;
using Microsoft.Win32;

namespace AdvancedControllerProcessor;

public partial class MainWindow : Window
{
    private MainViewModel _vm = null!;
    private HotkeyService _hotkeys = null!;
    private const int HotkeyToggleId = 1;
    private const int HotkeySafeModeId = 2;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closing += OnClosing;

        // Controls like sliders and combo boxes swallow the mouse wheel for their
        // own behavior, which makes long tabs feel impossible to scroll.
        // Intercept at preview level (fires before ANY control handles the wheel)
        // and redirect it to the enclosing tab ScrollViewer instead. This makes
        // the page scroll everywhere, regardless of what is under the cursor.
        AddHandler(Mouse.PreviewMouseWheelEvent,
            new MouseWheelEventHandler(OnPreviewMouseWheelForScroll), handledEventsToo: true);
    }

    private void OnPreviewMouseWheelForScroll(object sender, MouseWheelEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source)
            return;

        var viewer = FindAncestor<ScrollViewer>(source) ?? FindActiveScrollViewer();
        if (viewer is null || viewer.ScrollableHeight <= 0)
            return;

        viewer.ScrollToVerticalOffset(viewer.VerticalOffset - e.Delta / 3.0);
        e.Handled = true;
    }

    /// <summary>
    /// Fallback for when the cursor is over an area without its own
    /// ScrollViewer (tab header, status bar, card edges): scroll the tab that
    /// currently has the most scrollable content (the active one).
    /// </summary>
    private ScrollViewer? FindActiveScrollViewer()
    {
        ScrollViewer? best = null;
        double bestHeight = 0;
        foreach (var candidate in FindDescendants<ScrollViewer>(ScrollingTabHost))
        {
            if (candidate.ScrollableHeight <= 0 || candidate.ScrollableHeight <= bestHeight)
                continue;
            best = candidate;
            bestHeight = candidate.ScrollableHeight;
        }
        return best;
    }

    private static IEnumerable<T> FindDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        var queue = new Queue<DependencyObject>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(current);
            for (int i = 0; i < count; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(current, i);
                if (child is T match)
                    yield return match;
                queue.Enqueue(child);
            }
        }
    }

    private static T? FindAncestor<T>(DependencyObject start) where T : DependencyObject
    {
        var current = start;
        while (current is not null)
        {
            if (current is T match)
                return match;
            current = GetVisualParent(current);
        }
        return null;
    }

    /// <summary>
    /// Walk the visual/logical parent chain safely. ContentElements (e.g. the
    /// Run inside a TextBlock) are not Visuals, and VisualTreeHelper.GetParent
    /// throws on them — that was spamming the log on every wheel tick.
    /// </summary>
    private static DependencyObject? GetVisualParent(DependencyObject element)
    {
        if (element is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D)
            return System.Windows.Media.VisualTreeHelper.GetParent(element);
        if (element is System.Windows.ContentElement contentElement)
            return System.Windows.ContentOperations.GetParent(contentElement);
        return null;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            // Remove leftovers from previous self-updates before anything else
            Services.SelfUpdater.CleanupLeftovers();

            // Initialize ViewModel
            _vm = new MainViewModel();
            DataContext = _vm;

            // Initialize hotkeys
            _hotkeys = new HotkeyService();
            var helper = new WindowInteropHelper(this);
            _hotkeys.Register(HotkeyToggleId, Key.F8, _vm.ToggleProcessing, helper.Handle);
            _hotkeys.Register(HotkeySafeModeId, Key.F9, _vm.ToggleSafeMode, helper.Handle);

            // Start controller detection
            _vm.StartController();

            // Look for a newer published release (non-blocking, silent on failure)
            _ = CheckForUpdatesAsync();

            // Start UI update timer for stick visualizers
            var uiTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(33) // ~30 FPS
            };
            uiTimer.Tick += OnUiTimerTick;
            uiTimer.Start();

            // Periodically re-check for a mandatory update while the app is
            // open. If the publisher forces a new minimum version mid-session,
            // this instance shuts down so the next launch runs the mandatory
            // update gate and the user self-updates before it can run.
            var forcedUpdateTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(3)
            };
            forcedUpdateTimer.Tick += OnForcedUpdateTimerTick;
            forcedUpdateTimer.Start();
        }
        catch (Exception ex)
        {
            Logging.Error(ex, "Failed to initialize application");
            MessageBox.Show($"Initialization error: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        try
        {
            var helper = new WindowInteropHelper(this);
            _hotkeys?.UnregisterAll(helper.Handle);
            _vm?.Dispose();
            _hotkeys?.Dispose();
        }
        catch (Exception ex)
        {
            Logging.Error(ex, "Error during shutdown");
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // Hook into Windows message loop for hotkey processing
        var helper = new WindowInteropHelper(this);
        var source = HwndSource.FromHwnd(helper.Handle);
        source?.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_hotkeys?.ProcessMessage(msg, wParam) == true)
            handled = true;

        return IntPtr.Zero;
    }

    // ── UI Timer: Update Stick Visualizers ──────────────────

    private void OnUiTimerTick(object? sender, EventArgs e)
    {
        if (_vm is null) return;

        // Latency card (~2 Hz internal throttle)
        _vm.RefreshLatencyIfNeeded();

        // Left Stick visualizer (dashboard)
        LeftStickVisualizer?.UpdatePosition(
            _vm.LeftStick.RawX, _vm.LeftStick.RawY,
            _vm.LeftStick.ProcessedX, _vm.LeftStick.ProcessedY,
            _vm.LeftStick.DeadzoneEnabled ? _vm.LeftStick.Deadzone : 0f);

        // Left Stick visualizer (detail tab)
        LeftStickDetailVisualizer?.UpdatePosition(
            _vm.LeftStick.RawX, _vm.LeftStick.RawY,
            _vm.LeftStick.ProcessedX, _vm.LeftStick.ProcessedY,
            _vm.LeftStick.DeadzoneEnabled ? _vm.LeftStick.Deadzone : 0f);

        // Right Stick visualizers
        RightStickVisualizer?.UpdatePosition(
            _vm.RightStick.RawX, _vm.RightStick.RawY,
            _vm.RightStick.ProcessedX, _vm.RightStick.ProcessedY);

        RightStickDetailVisualizer?.UpdatePosition(
            _vm.RightStick.RawX, _vm.RightStick.RawY,
            _vm.RightStick.ProcessedX, _vm.RightStick.ProcessedY);

        // Update toggle button text
        ToggleProcessingBtn.Content = _vm.IsProcessingEnabled
            ? "Processing: ON" : "Processing: OFF";
    }

    // ── Update Notification ────────────────────────────────

    private UpdateInfo? _pendingUpdate;
    private bool _isUpdating;
    private bool _checkingForcedUpdate;

    /// <summary>
    /// Enforce a mandatory update on live sessions. Runs every few minutes on
    /// the UI thread; shows the blocking update gate when the installed build
    /// fell below the required minimum, then exits so the forced gate re-runs
    /// on the next launch until the user updates.
    /// </summary>
    private async void OnForcedUpdateTimerTick(object? sender, EventArgs e)
    {
        if (_checkingForcedUpdate || _isUpdating)
            return;

        _checkingForcedUpdate = true;
        try
        {
            RequiredUpdate? required = await UpdateChecker.GetRequiredUpdateAsync();
            if (required is null)
                return;

            Logging.Warn(
                $"Live build is outdated — enforcing update to " +
                $"v{required.RequiredVersion.ToString(3)}");

            var gate = new MandatoryUpdateWindow(required);
            gate.ShowDialog();

            Logging.Info("Exiting after live mandatory-update gate");
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            Logging.Warn($"Periodic mandatory-update check failed: {ex.Message}");
        }
        finally
        {
            _checkingForcedUpdate = false;
        }
    }

    private async Task CheckForUpdatesAsync()
    {
        var info = await UpdateChecker.CheckForUpdateAsync();
        if (info is null || UpdateBanner is null)
            return;

        _pendingUpdate = info;
        UpdateBannerText.Text = $"Update available: v{info.Version} — click to update automatically";
        UpdateBanner.Visibility = Visibility.Visible;
    }

    private async void OnUpdateBannerClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_pendingUpdate is null || _isUpdating)
            return;

        var info = _pendingUpdate;
        var choice = MessageBox.Show(
            this,
            $"Version v{info.Version} is available.\n\n" +
            "Install it now? The app will download the update, " +
            "replace itself and restart — no manual steps needed.",
            "Update available",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (choice != MessageBoxResult.Yes)
        {
            OpenDownloadUrl(info);
            return;
        }

        _isUpdating = true;
        try
        {
            await SelfUpdater.ApplyUpdateAsync(
                info,
                progress => Dispatcher.Invoke(() =>
                {
                    if (_vm is not null) _vm.StatusMessage = progress;
                    UpdateBannerText.Text = progress ?? "Updating…";
                }));

            // New version is running; close this (old) instance.
            // Its exe file was renamed aside, so nothing blocks the swap.
            Logging.Info($"Self-updated to v{info.Version} — shutting down old instance");
            Close();
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            Logging.Error(ex, $"Self-update to v{info.Version} failed");
            _isUpdating = false;
            MessageBox.Show(
                this,
                $"Automatic update failed:\n{ex.Message}\n\nOpening the download page instead.",
                "Update failed",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            OpenDownloadUrl(info);
        }
    }

    private void OpenDownloadUrl(UpdateInfo info)
    {
        try
        {
            Process.Start(new ProcessStartInfo(info.DownloadUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Logging.Error(ex, "Failed to open update download URL");
        }
    }

    // ── Button Click Handlers ──────────────────────────────

    private void OnToggleProcessingClick(object sender, RoutedEventArgs e)
    {
        _vm?.ToggleProcessing();
    }

    private void OnSafeModeClick(object sender, RoutedEventArgs e)
    {
        _vm?.ToggleSafeMode();
    }

    private void OnSaveProfileClick(object sender, RoutedEventArgs e)
    {
        _vm?.SaveCurrentProfile();
    }

    private void OnRefreshProfilesClick(object sender, RoutedEventArgs e)
    {
        _vm?.RefreshProfileList();
    }

    private void OnExportProfileClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            DefaultExt = ".json",
            FileName = $"{_vm?.SelectedProfileName ?? "Profile"}.json"
        };

        if (dialog.ShowDialog() == true && _vm is not null)
        {
            var profile = new Models.Profile
            {
                Name = _vm.SelectedProfileName,
                LeftStick = _vm.LeftStick.ToSettings(),
                RightStick = _vm.RightStick.ToSettings()
            };

            var profileService = new ProfileService(
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Profiles"));
            profileService.ExportProfile(profile, dialog.FileName);
            _vm.StatusMessage = $"Profile exported to {dialog.FileName}";
        }
    }

    private void OnImportProfileClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            DefaultExt = ".json"
        };

        if (dialog.ShowDialog() == true && _vm is not null)
        {
            var profileService = new ProfileService(
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Profiles"));
            var profile = profileService.ImportProfile(dialog.FileName);
            if (profile is not null)
            {
                profileService.Save(profile);
                _vm.RefreshProfileList();
                _vm.LoadProfile(profile.Name);
            }
        }
    }

    private void OnResetProfileClick(object sender, RoutedEventArgs e)
    {
        _vm?.ApplyCurrentSettings();
    }

    private void OnRestoreDefaultsClick(object sender, RoutedEventArgs e)
    {
        _vm?.ResetToSafeMode();
    }
}
