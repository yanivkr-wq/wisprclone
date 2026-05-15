using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using Serilog;
using WisprClone.App.Hotkey;
using WisprClone.App.Pipeline;
using WisprClone.App.Refinement;
using WisprClone.App.Settings;
using WisprClone.App.Transcription;
using WisprClone.App.Translation;

namespace WisprClone.App.Ui;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly string _settingsPath;
    private readonly WhisperEngine _whisper;
    private readonly RefinementService _refiner;
    private readonly TranslationService _translator;
    private readonly DictationCoordinator _coordinator;
    private readonly LowLevelKeyboardHook _hook;

    public SettingsWindow(
        AppSettings settings,
        string settingsPath,
        WhisperEngine whisper,
        RefinementService refiner,
        TranslationService translator,
        DictationCoordinator coordinator,
        LowLevelKeyboardHook hook)
    {
        InitializeComponent();

        _settings = settings;
        _settingsPath = settingsPath;
        _whisper = whisper;
        _refiner = refiner;
        _translator = translator;
        _coordinator = coordinator;
        _hook = hook;

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

        _stagedHotkey = HotkeySpec.Parse(_settings.Hotkey);
        HotkeyDisplay.Text = _stagedHotkey.DisplayName;

        OfferRefinementCheck.IsChecked = _settings.OfferRefinement;
        KeepWavsCheck.IsChecked = _settings.KeepWavFiles;

        // Translate hotkey
        _stagedTranslateHotkey = HotkeySpec.Parse(_settings.TranslateHotkey);
        TranslateHotkeyDisplay.Text = _stagedTranslateHotkey.DisplayName;

        // Translate target language
        TranslateTargetCombo.SelectedIndex = 0;  // default: auto
        for (int i = 0; i < TranslateTargetCombo.Items.Count; i++)
        {
            if (TranslateTargetCombo.Items[i] is ComboBoxItem item
                && string.Equals(item.Tag?.ToString(), _settings.TranslateTarget, StringComparison.OrdinalIgnoreCase))
            {
                TranslateTargetCombo.SelectedIndex = i;
                break;
            }
        }
    }

    private HotkeySpec _stagedHotkey = HotkeySpec.Default;
    private HotkeySpec _stagedTranslateHotkey = HotkeySpec.Parse("Alt+Shift+T");

    private void OnChangeHotkey_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new HotkeyCaptureDialog(_stagedHotkey) { Owner = this };
        if (dlg.ShowDialog() == true && dlg.Captured != null)
        {
            _stagedHotkey = dlg.Captured;
            HotkeyDisplay.Text = _stagedHotkey.DisplayName;
        }
    }

    private void OnChangeTranslateHotkey_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new HotkeyCaptureDialog(_stagedTranslateHotkey) { Owner = this };
        if (dlg.ShowDialog() == true && dlg.Captured != null)
        {
            _stagedTranslateHotkey = dlg.Captured;
            TranslateHotkeyDisplay.Text = _stagedTranslateHotkey.DisplayName;
        }
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
        var hotkeyTag = _stagedHotkey.ToStorageString();
        var hotkeyChanged = !string.Equals(_settings.Hotkey, hotkeyTag, StringComparison.OrdinalIgnoreCase);

        var translateTarget = (TranslateTargetCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "auto";
        var translateHotkeyTag = _stagedTranslateHotkey.ToStorageString();

        _settings.OpenAiApiKey = key;
        _settings.HotkeyMode = mode;
        _settings.MaxRecordingSeconds = maxSec;
        _settings.Hotkey = hotkeyTag;
        _settings.OfferRefinement = OfferRefinementCheck.IsChecked == true;
        _settings.KeepWavFiles = KeepWavsCheck.IsChecked == true;
        _settings.TranslateHotkey = translateHotkeyTag;
        _settings.TranslateTarget = translateTarget;

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
            _refiner.UpdateApiKey(key);
            _translator.UpdateApiKey(key);
            _coordinator.UpdateSettings(
                mode, maxSec, _settings.KeepWavFiles, _settings.OfferRefinement);

            // Translate hotkey can be applied live: the hook supports
            // swapping the translate spec at runtime.
            _hook.UpdateTranslateSpec(_stagedTranslateHotkey);

            Log.Information("Settings saved and applied live");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to apply settings live; restart may be needed");
            ShowError("Saved, but couldn't apply some changes live. Restart WisprClone to be safe.");
            return;
        }

        if (hotkeyChanged)
        {
            MessageBox.Show(
                "Hotkey saved. Restart WisprClone for the new hotkey to take effect.",
                "Restart required",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
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
