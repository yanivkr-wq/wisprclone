using System;
using System.Diagnostics;
using Microsoft.Win32;
using Serilog;

namespace WisprClone.App.Tray;

/// <summary>
/// Manages whether WisprClone launches at Windows login by writing/removing
/// a value under HKCU\Software\Microsoft\Windows\CurrentVersion\Run.
/// HKCU = current user, no admin required.
/// </summary>
public static class AutostartHelper
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "WisprClone";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            if (key == null) return false;
            var existing = key.GetValue(ValueName) as string;
            return !string.IsNullOrEmpty(existing);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to read autostart registry key");
            return false;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                            ?? Registry.CurrentUser.CreateSubKey(RunKey);
            if (key == null)
            {
                Log.Warning("Could not open Run key for write");
                return;
            }

            if (enabled)
            {
                var exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(exePath))
                {
                    Log.Warning("Could not resolve own EXE path");
                    return;
                }
                // Wrap path in quotes to handle spaces.
                key.SetValue(ValueName, $"\"{exePath}\"");
                Log.Information("Autostart enabled → {Path}", exePath);
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                Log.Information("Autostart disabled");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to update autostart");
        }
    }
}
