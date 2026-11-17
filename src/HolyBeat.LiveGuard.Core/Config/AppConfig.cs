using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using HolyBeat.LiveGuard.Core.Profiles;

namespace HolyBeat.LiveGuard.Core.Config;

/// <summary>How the app connects to audio on this machine.</summary>
public enum RoutingMode
{
    /// <summary>Only the built-in player is processed (works with zero drivers installed).</summary>
    Player,

    /// <summary>System audio is captured and analysed, the original audio keeps playing untouched.</summary>
    SystemMonitor,

    /// <summary>System audio is captured, processed and re-routed to the selected output device.</summary>
    SystemWide
}

/// <summary>Device and routing preferences.</summary>
public sealed class RoutingSettings
{
    public RoutingMode Mode { get; set; } = RoutingMode.Player;
    public string InputDeviceId { get; set; } = string.Empty;
    public string OutputDeviceId { get; set; } = string.Empty;
    public double MasterVolume { get; set; } = 0.85;
    public bool PreferVirtualCable { get; set; } = true;
}

/// <summary>Interface preferences.</summary>
public sealed class UiSettings
{
    public bool ShowSpectrum { get; set; } = true;
    public bool ShowDetector { get; set; } = true;
    public bool StartMinimizedToTray { get; set; }
    public bool CloseToTray { get; set; } = true;
    public string Theme { get; set; } = "dark";
}

/// <summary>Persisted application configuration.</summary>
public sealed class AppConfig
{
    public int Version { get; set; } = 1;
    public string ActiveProfileId { get; set; } = ProfileLibrary.Safe;
    public bool Bypass { get; set; }
    public bool AutoProfile { get; set; }
    public bool RunAtStartup { get; set; }
    public bool AutoSwitchProfile { get; set; } = true;

    /// <summary>Remembers which profile each track needed, keyed by full file path.</summary>
    public bool RememberTracksPerTitle { get; set; } = true;
    public Dictionary<string, string> TrackProfiles { get; set; } = new Dictionary<string, string>();
    public Dictionary<string, SafetyProfile> Profiles { get; set; } = new Dictionary<string, SafetyProfile>();
    public RoutingSettings Routing { get; set; } = new RoutingSettings();
    public UiSettings Ui { get; set; } = new UiSettings();
}

/// <summary>Loads and saves <see cref="AppConfig"/> as JSON in the user profile.</summary>
public static class ConfigStore
{
    private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HolyBeat");

    public static string DefaultPath => Path.Combine(DefaultDirectory, "config.json");

    public static AppConfig CreateDefault()
    {
        var config = new AppConfig();
        config.Profiles = ProfileLibrary.CreateDefaults();
        return config;
    }

    public static AppConfig Load(string? path = null)
    {
        string file = path ?? DefaultPath;
        try
        {
            if (!File.Exists(file))
                return CreateDefault();

            string json = File.ReadAllText(file);
            var config = JsonSerializer.Deserialize<AppConfig>(json, Options) ?? CreateDefault();

            // Merge missing presets from the factory library and repair damaged values.
            var defaults = ProfileLibrary.CreateDefaults();
            foreach (var preset in defaults)
            {
                if (!config.Profiles.ContainsKey(preset.Key))
                {
                    config.Profiles[preset.Key] = preset.Value;
                    continue;
                }

                config.Profiles[preset.Key].Repair(preset.Value);
            }

            foreach (var profile in config.Profiles.Values)
                profile.WithDefaults();

            if (!config.Profiles.ContainsKey(config.ActiveProfileId))
                config.ActiveProfileId = ProfileLibrary.Safe;

            return config;
        }
        catch (Exception)
        {
            return CreateDefault();
        }
    }

    public static void Save(AppConfig config, string? path = null)
    {
        string file = path ?? DefaultPath;
        string? directory = Path.GetDirectoryName(file);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        string json = JsonSerializer.Serialize(config, Options);
        string temporary = file + ".tmp";
        File.WriteAllText(temporary, json);
        File.Move(temporary, file, overwrite: true);
    }
}
