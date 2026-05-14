using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog;
using WisprClone.App.Hotkey;

namespace WisprClone.App.Settings;

public sealed class AppSettings
{
    /// <summary>
    /// OpenAI API key (starts with "sk-..."). Required.
    /// Can also be supplied via the OPENAI_API_KEY environment variable —
    /// if both are set, the settings.json value wins.
    /// </summary>
    public string OpenAiApiKey { get; set; } = "";

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public HotkeyMode HotkeyMode { get; set; } = HotkeyMode.PushToTalk;

    public int MaxRecordingSeconds { get; set; } = 120;

    /// <summary>
    /// Optional URL to a Velopack release feed (e.g. a GitHub Releases page or
    /// any HTTP location hosting RELEASES + *.nupkg files). When set AND the
    /// app was installed via Velopack, the app periodically checks for new
    /// versions and applies them on next launch. Empty = auto-update disabled.
    /// </summary>
    public string UpdateFeedUrl { get; set; } = "";

    /// <summary>
    /// Resolves the settings file at %APPDATA%\WisprClone\settings.json. Creates
    /// it with a placeholder API key on first run so the user has something to
    /// edit.
    /// </summary>
    public static AppSettings LoadOrCreate(out string path)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WisprClone");
        Directory.CreateDirectory(dir);

        path = Path.Combine(dir, "settings.json");

        if (!File.Exists(path))
        {
            var defaults = new AppSettings
            {
                OpenAiApiKey = "sk-REPLACE-WITH-YOUR-OPENAI-KEY"
            };
            Save(defaults, path);
            Log.Information("Created default settings file: {Path}", path);
            return defaults;
        }

        try
        {
            var json = File.ReadAllText(path);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions)
                           ?? new AppSettings();
            Log.Information("Loaded settings from {Path}: mode={Mode}, maxSec={Max}, hasKey={HasKey}",
                path, settings.HotkeyMode, settings.MaxRecordingSeconds,
                !string.IsNullOrWhiteSpace(settings.OpenAiApiKey));
            return settings;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to parse {Path} — falling back to defaults", path);
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings, string path)
    {
        var json = JsonSerializer.Serialize(settings, SerializerOptions);
        File.WriteAllText(path, json);
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}
