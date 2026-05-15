using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using Serilog;

namespace WisprClone.App.Ui;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();

        // Stamp the version from the running assembly so it stays in sync with
        // whatever's actually deployed.
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.0";
        VersionText.Text = $"v{version}";

        // Show the shipped app icon, if present.
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Resources", "app.ico");
        if (File.Exists(iconPath))
        {
            try
            {
                AppIcon.Source = new BitmapImage(new Uri(iconPath, UriKind.Absolute));
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "About: couldn't load app icon");
            }
        }
    }

    private void OnClose_Click(object sender, RoutedEventArgs e) => Close();

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
}
