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
    /// When false (default), the captured .wav for each dictation is deleted
    /// after successful transcription, to avoid filling the disk over time.
    /// On transcription failure the wav is preserved (so a power user can
    /// inspect it). Set true to keep ALL wavs (~32 KB/sec of audio).
    /// </summary>
    public bool KeepWavFiles { get; set; } = false;

    /// <summary>
    /// Hotkey identifier. Currently supported: "Ctrl+Win" (default), "Right Alt",
    /// "F8", "F9". Hotkey changes require an app restart to take effect.
    /// </summary>
    public string Hotkey { get; set; } = "Ctrl+Win";

    /// <summary>
    /// When true, after each successful dictation the refinement panel pops up
    /// offering Professional / Casual / Shorter / Longer rewrites via OpenAI.
    /// Each click costs roughly $0.0001 on gpt-4o-mini.
    /// </summary>
    public bool OfferRefinement { get; set; } = false;

    /// <summary>Chat model used for refinement. Defaults to gpt-4o-mini for cost/speed.</summary>
    public string RefinementModel { get; set; } = "gpt-4o-mini";

    /// <summary>
    /// Hotkey that triggers translate-from-clipboard. Read the same way as
    /// <see cref="Hotkey"/> — e.g. "Alt+Shift+T", "Ctrl+Shift+T", "F12".
    /// Default: Alt+Shift+T (4-key chord, unlikely to conflict).
    /// Set to empty string to disable the feature.
    /// </summary>
    public string TranslateHotkey { get; set; } = "Alt+Shift+T";

    /// <summary>
    /// Target language for translation. "auto" flips between Hebrew and
    /// English based on the input. Otherwise an explicit language name —
    /// "Hebrew", "English", "Spanish", "Arabic", "French", "German", …
    /// </summary>
    public string TranslateTarget { get; set; } = "auto";

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
