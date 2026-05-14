using System;
using System.Runtime.InteropServices;

namespace WisprClone.App.Injection;

/// <summary>
/// Win32 SendInput wrapper for synthesising a Ctrl+V keystroke. Bypasses
/// per-app input queue issues that affect SendKeys / WinForms input simulation.
/// </summary>
internal static class SendInputHelper
{
    private const int INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_V = 0x56;

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUTUNION
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public int type;
        public INPUTUNION u;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    /// <summary>
    /// Sends a synthetic Ctrl+V key chord (down Ctrl, down V, up V, up Ctrl).
    /// </summary>
    public static void SendCtrlV()
    {
        var inputs = new INPUT[]
        {
            Key(VK_CONTROL, isUp: false),
            Key(VK_V,       isUp: false),
            Key(VK_V,       isUp: true),
            Key(VK_CONTROL, isUp: true),
        };

        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        if (sent != inputs.Length)
        {
            int err = Marshal.GetLastWin32Error();
            throw new InvalidOperationException($"SendInput injected only {sent}/{inputs.Length} events (Win32 error {err}).");
        }
    }

    private static INPUT Key(ushort vk, bool isUp) => new INPUT
    {
        type = INPUT_KEYBOARD,
        u = new INPUTUNION
        {
            ki = new KEYBDINPUT
            {
                wVk = vk,
                dwFlags = isUp ? KEYEVENTF_KEYUP : 0
            }
        }
    };
}
