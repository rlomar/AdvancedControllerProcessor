using System.IO;
using System.Windows;
using System.Windows.Controls;
using LicenseManager;

namespace LicenseManager;

public partial class MainWindow : Window
{
    private SupabaseAdmin? _admin;
    private string _lastGeneratedPlainKey = "";

    public MainWindow()
    {
        InitializeComponent();

        var config = SupabaseAdmin.LoadConfig();
        if (config is { } cfg)
        {
            UrlInput.Text = cfg.Url;
            Connect(cfg.Url, cfg.Secret);
        }
        else
        {
            // Pre-fill the known project URL — the owner only has to paste the secret.
            UrlInput.Text = "https://okrjquscnfdmanpzsnsk.supabase.co";
            SetStatus("Paste your secret key to connect.", warn: false);
        }
    }

    private void OnConnectClick(object sender, RoutedEventArgs e)
    {
        string url = UrlInput.Text.Trim();
        string secret = SecretInput.Password.Trim();

        if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || secret.Length == 0)
        {
            SetStatus("Enter a valid URL (https://…) and the secret key.", warn: true);
            return;
        }

        try
        {
            SupabaseAdmin.SaveConfig(url, secret);
            Connect(url, secret);
        }
        catch (Exception ex)
        {
            SetStatus($"Could not save config: {ex.Message}", warn: true);
        }
    }

    private async void Connect(string url, string secret)
    {
        _admin = new SupabaseAdmin(url, secret);
        ConnectPanel.Visibility = Visibility.Collapsed;
        RefreshBtn.IsEnabled = true;
        CreateBtn.IsEnabled = true;
        SetStatus("Connected. Loading keys…", warn: false);
        await ReloadAsync();
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await ReloadAsync();

    private async Task ReloadAsync()
    {
        if (_admin is null) return;

        try
        {
            KeysGrid.ItemsSource = await _admin.ListAsync();
            int count = KeysGrid.Items.Count;
            SetStatus($"{count} key{(count == 1 ? "" : "s")} loaded at {DateTime.Now:HH:mm:ss}.", warn: false);
        }
        catch (Exception ex)
        {
            SetStatus($"Load failed: {TrimError(ex.Message)}", warn: true);
        }
    }

    // ── Create flow ───────────────────────────────────────

    private void OnShowCreateClick(object sender, RoutedEventArgs e)
    {
        if (_admin is null) return;
        LabelInput.Clear();
        CreateFormStep.Visibility = Visibility.Visible;
        CreateResultStep.Visibility = Visibility.Collapsed;
        CreateOverlay.Visibility = Visibility.Visible;
        LabelInput.Focus();
    }

    private async void OnGenerateClick(object sender, RoutedEventArgs e)
    {
        if (_admin is null) return;

        string plainKey = LicenseCrypto.GenerateKey();
        string normalized = LicenseCrypto.Normalize(plainKey);
        string hash = LicenseCrypto.HashKey(normalized);

        try
        {
            await _admin.CreateAsync(hash, LabelInput.Text.Trim());

            // Show plaintext exactly once.
            _lastGeneratedPlainKey = LicenseCrypto.FormatGrouped(normalized);
            NewKeyText.Text = _lastGeneratedPlainKey;
            CreateFormStep.Visibility = Visibility.Collapsed;
            CreateResultStep.Visibility = Visibility.Visible;
            SetStatus("Key created. Copy it before closing — it cannot be recovered.", warn: true);
        }
        catch (Exception ex)
        {
            SetStatus($"Create failed: {TrimError(ex.Message)}", warn: true);
        }
    }

    private void OnCopyKeyClick(object sender, RoutedEventArgs e)
    {
        if (_lastGeneratedPlainKey.Length > 0)
        {
            Clipboard.SetText(_lastGeneratedPlainKey);
            CopyKeyBtn.Content = "Copied ✓";
        }
    }

    private void OnCloseOverlayClick(object sender, RoutedEventArgs e)
    {
        CreateOverlay.Visibility = Visibility.Collapsed;
        CopyKeyBtn.Content = "Copy Key";
        _ = ReloadAsync();
    }

    // ── Row actions ───────────────────────────────────────

    private async void OnRevokeClick(object sender, RoutedEventArgs e) =>
        await RowActionAsync((string)((Button)sender).Tag,
            admin => admin.SetRevokedAsync((string)((Button)sender).Tag, true),
            "Key revoked.");

    private async void OnUnrevokeClick(object sender, RoutedEventArgs e) =>
        await RowActionAsync((string)((Button)sender).Tag,
            admin => admin.SetRevokedAsync((string)((Button)sender).Tag, false),
            "Key restored.");

    private async void OnResetDeviceClick(object sender, RoutedEventArgs e)
    {
        string hash = (string)((Button)sender).Tag;
        if (MessageBox.Show(
                "Unbind the device from this key?\nThe same user can then activate on a new PC.",
                "Reset Device", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        await RowActionAsync(hash, admin => admin.ResetDeviceAsync(hash), "Device unbound.");
    }

    private async Task RowActionAsync(string keyHash, Func<SupabaseAdmin, Task> action, string okMessage)
    {
        if (_admin is null) return;
        try
        {
            await action(_admin);
            SetStatus(okMessage, warn: false);
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            SetStatus($"Action failed: {TrimError(ex.Message)}", warn: true);
        }
    }

    // ── Helpers ───────────────────────────────────────────

    private void SetStatus(string message, bool warn)
    {
        StatusBar.Text = message;
        StatusBar.Foreground = warn
            ? FindResource("DangerBrush") as System.Windows.Media.Brush
            : FindResource("TextSecondaryBrush") as System.Windows.Media.Brush;
    }

    private static string TrimError(string message) =>
        message.Length > 220 ? message[..220] + "…" : message;
}
