using System.IO;
using AdvancedControllerProcessor.Helpers;
using AdvancedControllerProcessor.Models;
using Newtonsoft.Json;

namespace AdvancedControllerProcessor.Services;

/// <summary>
/// Manages application-level settings (separate from profiles).
/// Stored in AppSettings.json next to the executable.
/// </summary>
public sealed class ConfigurationService
{
    private readonly string _settingsPath;
    private AppSettings _settings;

    public ConfigurationService(string settingsDirectory)
    {
        _settingsPath = Path.Combine(settingsDirectory, "AppSettings.json");
        _settings = Load();
    }

    public AppSettings Settings => _settings;

    /// <summary>
    /// Load settings from disk. Returns defaults if file doesn't exist or is corrupted.
    /// </summary>
    public AppSettings Load()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                string json = File.ReadAllText(_settingsPath);
                var loaded = JsonConvert.DeserializeObject<AppSettings>(json);
                if (loaded is not null)
                {
                    _settings = loaded;
                    Logging.Info("AppSettings loaded");
                    return _settings;
                }
            }
        }
        catch (Exception ex)
        {
            Logging.Warn($"Failed to load AppSettings, using defaults: {ex.Message}");
        }

        _settings = AppSettings.Default();
        return _settings;
    }

    /// <summary>
    /// Save current settings to disk.
    /// </summary>
    public void Save()
    {
        try
        {
            var jsonSettings = new JsonSerializerSettings
            {
                Formatting = Formatting.Indented
            };

            string json = JsonConvert.SerializeObject(_settings, jsonSettings);
            File.WriteAllText(_settingsPath, json);
            Logging.Info("AppSettings saved");
        }
        catch (Exception ex)
        {
            Logging.Error(ex, "Failed to save AppSettings");
        }
    }

    /// <summary>
    /// Update a setting and save immediately.
    /// </summary>
    public void Update(Action<AppSettings> updater)
    {
        updater(_settings);
        Save();
    }
}
