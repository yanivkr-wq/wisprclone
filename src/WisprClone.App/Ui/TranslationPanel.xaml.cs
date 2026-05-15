using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Serilog;
using WisprClone.App.Tracking;
using WisprClone.App.Translation;

namespace WisprClone.App.Ui;

/// <summary>
/// Modeless popup that shows the original clipboard text + the OpenAI
/// translation side-by-side, with explicit "Use translation" / "Keep original"
/// buttons. The user's clipboard is NEVER modified without them clicking
/// "Use translation" — keeping the source unchanged when they didn't ask
/// for it.
/// </summary>
public partial class TranslationPanel : Window
{
    private readonly string _original;
    private readonly TranslationService _translator;
    private readonly string _targetLanguage;
    private string? _translation;

    public TranslationPanel(string original, TranslationService translator, string targetLanguage)
    {
        InitializeComponent();
        _original = original;
        _translator = translator;
        _targetLanguage = targetLanguage;

        // Preview only — long inputs would otherwise make the window absurd.
        OriginalText.Text = original.Length > 400
            ? original[..400] + "…"
            : original;

        TranslatedText.Text = "Translating…";
        DirectionText.Text = "calling OpenAI…";

        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var result = await _translator.TranslateAsync(_original, _targetLanguage).ConfigureAwait(true);
            _translation = result.Text;
            DirectionText.Text = $"{result.SourceHint} → {result.TargetLanguage}";
            TranslatedText.Text = result.Text;
            UseBtn.IsEnabled = !string.IsNullOrWhiteSpace(result.Text);

            // Bill it to the usage tracker.
            UsageTracker.RecordTranslate(_original, result.Text);

            Log.Information("Translate {From}→{To}: \"{Text}\"", result.SourceHint, result.TargetLanguage, result.Text);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "TranslationPanel: translation failed");
            DirectionText.Text = "(failed)";
            TranslatedText.Text = "Translation failed. See log for details.";
            ShowError(ex.Message);
            UseBtn.IsEnabled = false;
        }
    }

    private void OnUse_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_translation)) return;
        try
        {
            // Replace clipboard with the translation. Original is now gone
            // from the user's clipboard — but they explicitly opted in.
            Clipboard.SetDataObject(_translation, copy: true);
            Log.Information("Translate: user accepted translation, clipboard replaced");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Translate: failed to set clipboard");
        }
        DialogResult = true;
        Close();
    }

    private void OnKeep_Click(object sender, RoutedEventArgs e)
    {
        Log.Information("Translate: user kept original, clipboard untouched");
        DialogResult = false;
        Close();
    }

    private void OnClose_Click(object sender, RoutedEventArgs e) => OnKeep_Click(sender, e);

    private void OnAnyKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            OnKeep_Click(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && UseBtn.IsEnabled)
        {
            OnUse_Click(sender, e);
            e.Handled = true;
        }
    }

    private void ShowError(string message)
    {
        StatusText.Text = message;
        StatusText.Visibility = Visibility.Visible;
    }
}
