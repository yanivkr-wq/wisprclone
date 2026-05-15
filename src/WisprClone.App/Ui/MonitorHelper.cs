using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace WisprClone.App.Ui;

/// <summary>
/// Win32 helpers to find the screen the user's cursor is currently on, so
/// the floating pill appears on the monitor they're actually working on
/// (not always the primary). Returns physical-pixel rects from Win32 plus
/// a DPI-aware conversion so WPF (which positions in DIPs) places windows
/// correctly on high-DPI / scaled displays.
/// </summary>
internal static class MonitorHelper
{
    [DllImport("user32.dll")]
    private static extern int GetDpiForWindow(IntPtr hwnd);

    /// <summary>
    /// Returns the active monitor's work area converted from physical pixels
    /// (Win32) to DIPs (WPF) using the DPI of the supplied window. Use this
    /// when assigning to <see cref="Window.Left"/> / <see cref="Window.Top"/>.
    /// </summary>
    public static Rect GetActiveMonitorWorkAreaInDips(Window window)
    {
        var physical = GetActiveMonitorWorkArea();
        var hwnd = new WindowInteropHelper(window).Handle;
        int dpi = hwnd != IntPtr.Zero ? GetDpiForWindow(hwnd) : 96;
        if (dpi <= 0) dpi = 96;
        double scale = dpi / 96.0;
        return new Rect(
            physical.X / scale,
            physical.Y / scale,
            physical.Width / scale,
            physical.Height / scale);
    }

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
