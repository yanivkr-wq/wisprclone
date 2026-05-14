using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;
using Serilog;

namespace WisprClone.App.Ui;

/// <summary>
/// First-run wizard shown when the app starts with no usable OpenAI API key.
/// Modal — caller blocks on ShowDialog(). On success, EnteredKey holds the
/// trimmed key; on cancel, EnteredKey is null and the caller should exit.
/// </summary>
public partial class WelcomeWindow : Window
{
    /// <summary>Trimmed API key if the user clicked Save, otherwise null.</summary>
    public string? EnteredKey { get; private set; }

    public WelcomeWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => ApiKeyBox.Focus();
    }

    private void OnSave_Click(object sender, RoutedEventArgs e)
    {
        var key = ApiKeyBox.Password.Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            ShowError("Please paste your OpenAI API key.");
            ApiKeyBox.Focus();
            return;
        }
        if (!key.StartsWith("sk-", StringComparison.Ordinal))
        {
            ShowError("That doesn't look like an OpenAI key — they always start with \"sk-\".");
            ApiKeyBox.Focus();
            return;
        }
        if (key.Length < 30)
        {
            ShowError("That key looks too short. Double-check that you copied the whole value.");
            ApiKeyBox.Focus();
            return;
        }

        EnteredKey = key;
        DialogResult = true;
        Close();
    }

    private void OnCancel_Click(object sender, RoutedEventArgs e)
    {
        EnteredKey = null;
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
