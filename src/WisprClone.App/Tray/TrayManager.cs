using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Hardcodet.Wpf.TaskbarNotification;
using Serilog;
using WisprClone.App.Injection;
using WisprClone.App.Ui.Branding;

namespace WisprClone.App.Tray;

/// <summary>
/// Hosts the Windows tray icon and its right-click context menu.
/// Menu structure:
///   Recent ▶  (last N transcriptions — click to re-paste into current focus)
///   ─────
///   Start with Windows  (checkable)
///   Open settings file
///   Open log folder
///   ─────
///   Quit
/// </summary>
public sealed class TrayManager : IDisposable
{
    private readonly HistoryStore _history;
    private readonly ClipboardInjector _injector;
    private readonly Action _openSettings;
    private readonly Action _checkForUpdates;
    private readonly Action _openAbout;
    private readonly TaskbarIcon _icon;
    private readonly MenuItem _recentMenu;
    private readonly MenuItem _autostartMenu;

    public TrayManager(
        HistoryStore history,
        ClipboardInjector injector,
        Action openSettings,
        Action checkForUpdates,
        Action openAbout)
    {
        _history = history;
        _injector = injector;
        _openSettings = openSettings;
        _checkForUpdates = checkForUpdates;
        _openAbout = openAbout;

        _recentMenu = new MenuItem { Header = "Recent dictations" };
        _autostartMenu = new MenuItem
        {
            Header = "Start with Windows",
            IsCheckable = true,
            IsChecked = AutostartHelper.IsEnabled()
        };
        _autostartMenu.Click += OnAutostartToggled;

        var settingsItem = new MenuItem { Header = "Settings…" };
        settingsItem.Click += (_, _) => _openSettings();

        var logsItem = new MenuItem { Header = "Open log folder…" };
        logsItem.Click += (_, _) => OpenFolder(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WisprClone", "logs"));

        var updatesItem = new MenuItem { Header = "Check for updates…" };
        updatesItem.Click += (_, _) => _checkForUpdates();

        var aboutItem = new MenuItem { Header = "About WisprClone…" };
        aboutItem.Click += (_, _) => _openAbout();

        var quitItem = new MenuItem { Header = "Quit" };
        quitItem.Click += (_, _) => System.Windows.Application.Current.Shutdown();

        var menu = new ContextMenu();
        menu.Items.Add(_recentMenu);
        menu.Items.Add(new Separator());
        menu.Items.Add(_autostartMenu);
        menu.Items.Add(settingsItem);
        menu.Items.Add(logsItem);
        menu.Items.Add(updatesItem);
        menu.Items.Add(aboutItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(quitItem);

        // Rebuild the recent submenu lazily, when the menu actually opens —
        // saves us subscribing to history Changed events with dispatcher
        // marshalling.
        menu.Opened += (_, _) => RebuildRecentMenu();

        _icon = new TaskbarIcon
        {
            Icon = BuildTrayIcon(),
            ToolTipText = "WisprClone — hold Ctrl+Win to dictate",
            ContextMenu = menu
        };

        Log.Information("Tray icon ready");
    }

    private void OnAutostartToggled(object sender, RoutedEventArgs e)
    {
        AutostartHelper.SetEnabled(_autostartMenu.IsChecked);
        // Re-sync in case the registry write was rejected.
        _autostartMenu.IsChecked = AutostartHelper.IsEnabled();
    }

    private void RebuildRecentMenu()
    {
        _recentMenu.Items.Clear();
        var entries = _history.Snapshot();
        if (entries.Count == 0)
        {
            var placeholder = new MenuItem { Header = "(no dictations yet)", IsEnabled = false };
            _recentMenu.Items.Add(placeholder);
            return;
        }

        foreach (var entry in entries.Take(20)) // submenu cap, not history cap
        {
            // Trim long entries so the menu stays usable.
            var preview = entry.Text.Length > 60
                ? entry.Text[..57] + "…"
                : entry.Text;

            var item = new MenuItem
            {
                Header = preview,
                ToolTip = entry.Text + "\n\n" + entry.TimestampUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
            };
            // Capture entry by-value for the closure.
            var textCopy = entry.Text;
            item.Click += async (_, _) => await RePasteAsync(textCopy);
            _recentMenu.Items.Add(item);
        }
    }

    private async Task RePasteAsync(string text)
    {
        // Wait for the context menu to fully close (so focus returns to the
        // window the user was in) before injecting.
        await Task.Delay(150);
        try
        {
            _injector.InjectText(text);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Re-paste failed");
        }
    }

    private static void OpenInNotepad(string path)
    {
        try
        {
            // CreateDirectory ensures the file's parent exists; touch the file
            // if it's missing so notepad doesn't pop a "create new?" dialog.
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            if (!File.Exists(path)) File.WriteAllText(path, "{}\n");

            Process.Start(new ProcessStartInfo("notepad.exe", path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to open {Path} in notepad", path);
        }
    }

    private static void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to open folder {Path}", path);
        }
    }

    /// <summary>
    /// Loads the tray icon, preferring the .ico file that ships with the app
    /// (Resources\app.ico, also embedded as the EXE's icon resource). Falls
    /// back to runtime generation via IconFactory if the shipped file is
    /// missing (e.g. dev mode without the copy step), then to a system stock
    /// icon if even that fails.
    /// </summary>
    private static Icon BuildTrayIcon()
    {
        // Path 1: the shipped Resources\app.ico next to the EXE.
        var shippedIconPath = Path.Combine(AppContext.BaseDirectory, "Resources", "app.ico");
        if (File.Exists(shippedIconPath))
        {
            try { return new Icon(shippedIconPath); }
            catch (Exception ex) { Log.Warning(ex, "Failed to load shipped icon {Path}", shippedIconPath); }
        }

        // Path 2: regenerate on-the-fly into LOCALAPPDATA.
        var fallbackDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WisprClone");
        Directory.CreateDirectory(fallbackDir);
        var fallbackIconPath = Path.Combine(fallbackDir, "tray.ico");
        try
        {
            IconFactory.SaveAsIco(fallbackIconPath);
            return new Icon(fallbackIconPath);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to render tray icon; falling back to SystemIcons.Application");
            return SystemIcons.Application;
        }
    }

    /// <summary>
    /// Show a Windows balloon / Action Center notification from the tray icon.
    /// Marshals to the WPF dispatcher because Hardcodet expects that.
    /// </summary>
    public void ShowBalloon(string title, string message)
    {
        try
        {
            _icon.Dispatcher.BeginInvoke(new Action(() =>
            {
                _icon.ShowBalloonTip(title, message, BalloonIcon.Info);
            }));
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "ShowBalloon failed (non-fatal)");
        }
    }

    public void Dispose()
    {
        _icon.Dispose();
    }
}
