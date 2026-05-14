using System;
using System.Runtime.InteropServices;
using System.Windows;

namespace WisprClone.App.Ui;

/// <summary>
/// Win32 helpers to find the screen the user's cursor is currently on, so
/// the floating pill appears on the monitor they're actually working on
/// (not always the primary).
/// </summary>
internal static class MonitorHelper
{
    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    private const uint MONITOR_DEFAULTTONEAREST = 2;

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    /// <summary>
    /// Returns the working-area rectangle (in physical pixels) of the monitor
    /// containing the cursor. Falls back to the primary monitor on failure.
    /// </summary>
    public static Rect GetActiveMonitorWorkArea()
    {
        if (!GetCursorPos(out var pt))
        {
            return new Rect(0, 0, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight);
        }

        var hMon = MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(hMon, ref info))
        {
            return new Rect(0, 0, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight);
        }

        var r = info.rcWork;
        return new Rect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
    }
}
