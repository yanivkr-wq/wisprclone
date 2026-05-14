using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using Serilog;
using WisprClone.App.Hotkey;
using WisprClone.App.Pipeline;
using WisprClone.App.Settings;
using WisprClone.App.Transcription;

namespace WisprClone.App.Ui;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly string _settingsPath;
    private readonly WhisperEngine _whisper;
    private readonly DictationCoordinator _coordinator;

    public SettingsWindow(
        AppSettings settings,
        string settingsPath,
        WhisperEngine whisper,
        DictationCoordinator coordinator)
    {
        InitializeComponent();

        _settings = settings;
        _settingsPath = settingsPath;
        _whisper = whisper;
        _coordinator = coordinator;

        PopulateFromSettings();
    }

    private void PopulateFromSettings()
    {
        // Don't surface the literal placeholder back to the user — show blank so
        // they paste a real key, not "sk-REPLACE-WITH..."
        var key = _settings.OpenAiApiKey ?? "";
        if (key.Contains("REPLACE-WITH-YOUR-OPENAI-KEY", StringComparison.OrdinalIgnoreCase))
        {
            key = "";
        }
        ApiKeyBox.Password = key;

        HotkeyCombo.SelectedIndex = _settings.HotkeyMode == HotkeyMode.PushToTalk ? 0 : 1;
        MaxSlider.Value = Math.Clamp(_settings.MaxRecordingSeconds, (int)MaxSlider.Minimum, (int)MaxSlider.Maximum);
    }

    private void OnSave_Click(object sender, RoutedEventArgs e)
    {
        var key = ApiKeyBox.Password.Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            ShowError("API key cannot be empty. Get one at platform.openai.com/api-keys.");
            return;
        }
        if (!key.StartsWith("sk-", StringComparison.Ordinal))
        {
            ShowError("That doesn't look like an OpenAI key — they start with \"sk-\".");
            return;
        }

        var selectedTag = (HotkeyCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "PushToTalk";
        var mode = selectedTag == "Toggle" ? HotkeyMode.Toggle : HotkeyMode.PushToTalk;
        var maxSec = (int)MaxSlider.Value;

        _settings.OpenAiApiKey = key;
        _settings.HotkeyMode = mode;
        _settings.MaxRecordingSeconds = maxSec;

        try
        {
            AppSettings.Save(_settings, _settingsPath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to save settings");
            ShowError("Couldn't write settings.json. See logs for details.");
            return;
        }

        // Apply live without restart.
        try
        {
            _whisper.UpdateApiKey(key);
            _coordinator.UpdateSettings(mode, maxSec);
            Log.Information("Settings saved and applied live");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to apply settings live; restart may be needed");
            ShowError("Saved, but couldn't apply some changes live. Restart WisprClone to be safe.");
            return;
        }

        DialogResult = true;
        Close();
    }

    private void OnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OnHyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to open URL {Url}", e.Uri);
        }
        e.Handled = true;
    }

    private void ShowError(string message)
    {
        StatusText.Text = message;
        StatusText.Visibility = Visibility.Visible;
    }
}
