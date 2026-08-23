using System.IO;
using AdvancedControllerProcessor.Helpers;
using AdvancedControllerProcessor.Models;
using Newtonsoft.Json;

namespace AdvancedControllerProcessor.Services;

/// <summary>
/// Manages loading, saving, and switching between controller profiles.
/// Profiles are stored as JSON files in the Profiles directory.
/// </summary>
public sealed class ProfileService
{
    private readonly string _profilesDirectory;

    public ProfileService(string profilesDirectory)
    {
        _profilesDirectory = profilesDirectory;
        Directory.CreateDirectory(_profilesDirectory);
    }

    /// <summary>
    /// Load a profile by name (without .json extension).
    /// Falls back to Default if not found.
    /// </summary>
    public Profile Load(string profileName)
    {
        string path = Path.Combine(_profilesDirectory, $"{profileName}.json");

        if (!File.Exists(path))
        {
            Logging.Warn($"Profile '{profileName}' not found, using Default");
            return Profile.Default();
        }

        try
        {
            string json = File.ReadAllText(path);
            var profile = JsonConvert.DeserializeObject<Profile>(json);
            Logging.Info($"Profile loaded: {profileName}");
            return profile ?? Profile.Default();
        }
        catch (Exception ex)
        {
            Logging.Error(ex, $"Failed to load profile '{profileName}', using Default");
            return Profile.Default();
        }
    }

    /// <summary>
    /// Save a profile to disk.
    /// </summary>
    public void Save(Profile profile)
    {
        string path = Path.Combine(_profilesDirectory, $"{profile.Name}.json");

        try
        {
            var settings = new JsonSerializerSettings
            {
                Formatting = Formatting.Indented,
                DefaultValueHandling = DefaultValueHandling.Include
            };

            string json = JsonConvert.SerializeObject(profile, settings);
            File.WriteAllText(path, json);
            Logging.Info($"Profile saved: {profile.Name}");
        }
        catch (Exception ex)
        {
            Logging.Error(ex, $"Failed to save profile '{profile.Name}'");
        }
    }

    /// <summary>
    /// List all available profile names.
    /// </summary>
    public List<string> ListProfiles()
    {
        try
        {
            return Directory.GetFiles(_profilesDirectory, "*.json")
                .Select(f => Path.GetFileNameWithoutExtension(f))
                .Where(n => !string.IsNullOrEmpty(n))
                .Order()
                .ToList();
        }
        catch (Exception ex)
        {
            Logging.Error(ex, "Failed to list profiles");
            return ["Default"];
        }
    }

    /// <summary>
    /// Delete a profile file.
    /// </summary>
    public bool Delete(string profileName)
    {
        if (profileName.Equals("Default", StringComparison.OrdinalIgnoreCase))
            return false; // Cannot delete Default

        string path = Path.Combine(_profilesDirectory, $"{profileName}.json");

        if (!File.Exists(path))
            return false;

        try
        {
            File.Delete(path);
            Logging.Info($"Profile deleted: {profileName}");
            return true;
        }
        catch (Exception ex)
        {
            Logging.Error(ex, $"Failed to delete profile '{profileName}'");
            return false;
        }
    }

    /// <summary>
    /// Export a profile to a specified file path (for sharing).
    /// </summary>
    public bool ExportProfile(Profile profile, string filePath)
    {
        try
        {
            var settings = new JsonSerializerSettings { Formatting = Formatting.Indented };
            string json = JsonConvert.SerializeObject(profile, settings);
            File.WriteAllText(filePath, json);
            Logging.Info($"Profile exported to: {filePath}");
            return true;
        }
        catch (Exception ex)
        {
            Logging.Error(ex, $"Failed to export profile to '{filePath}'");
            return false;
        }
    }

    /// <summary>
    /// Import a profile from a specified file path.
    /// </summary>
    public Profile? ImportProfile(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
                return null;

            string json = File.ReadAllText(filePath);
            var profile = JsonConvert.DeserializeObject<Profile>(json);
            if (profile is not null)
            {
                Logging.Info($"Profile imported from: {filePath}");
            }
            return profile;
        }
        catch (Exception ex)
        {
            Logging.Error(ex, $"Failed to import profile from '{filePath}'");
            return null;
        }
    }
}
