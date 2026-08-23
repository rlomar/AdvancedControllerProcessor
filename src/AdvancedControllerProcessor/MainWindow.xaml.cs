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

        // Sliders swallow the mouse wheel to adjust their own value while the
        // cursor rests on them, making long tabs feel impossible to scroll.
        // Intercept at preview level (before sliders handle it) and redirect to
        // the enclosing tab ScrollViewer instead.
        AddHandler(Mouse.PreviewMouseWheelEvent,
            new MouseWheelEventHandler(OnPreviewMouseWheelForScroll), handledEventsToo: true);
    }

    private void OnPreviewMouseWheelForScroll(object sender, MouseWheelEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source)
            return;

        if (FindAncestor<System.Windows.Controls.Slider>(source) is null)
            return; // normal elements already bubble the wheel to the ScrollViewer

        var viewer = FindAncestor<ScrollViewer>(source);
        if (viewer is null || viewer.ScrollableHeight <= 0)
            return;

        viewer.ScrollToVerticalOffset(viewer.VerticalOffset - e.Delta / 3.0);
        e.Handled = true;
    }

    private static T? FindAncestor<T>(DependencyObject start) where T : DependencyObject
    {
        var current = start;
        while (current is not null)
        {
            if (current is T match)
                return match;
            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
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

    private async Task CheckForUpdatesAsync()
    {
        var info = await UpdateChecker.CheckForUpdateAsync();
        if (info is null || UpdateBanner is null)
            return;

        _pendingUpdate = info;
        UpdateBannerText.Text = $"Update available: v{info.Version} — click to download";
        UpdateBanner.Visibility = Visibility.Visible;
    }

    private void OnUpdateBannerClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_pendingUpdate is null)
            return;

        try
        {
            Process.Start(new ProcessStartInfo(_pendingUpdate.DownloadUrl) { UseShellExecute = true });
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
