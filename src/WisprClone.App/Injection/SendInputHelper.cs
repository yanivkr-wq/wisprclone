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
    private const ushort VK_SHIFT   = 0x10;
    private const ushort VK_LEFT    = 0x25;
    private const ushort VK_V       = 0x56;
    private const ushort VK_BACK    = 0x08;

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

    /// <summary>
    /// Sends Shift+Left N times. For LTR text: selects the N characters
    /// immediately behind the caret.
    /// </summary>
    public static void SendShiftLeft(int count) => SendShiftArrow(count, VK_LEFT);

    /// <summary>
    /// Sends Shift+Right N times. For RTL text (Hebrew / Arabic), the caret
    /// sits at the visual-left of the just-typed text — pressing Left would
    /// move it into empty space. Shift+Right selects backwards into the text.
    /// </summary>
    public static void SendShiftRight(int count) => SendShiftArrow(count, VK_RIGHT);

    /// <summary>
    /// Sends Backspace N times. Always deletes the character immediately
    /// before the caret in LOGICAL order — works identically for LTR, RTL,
    /// and mixed scripts. Used by the refinement panel as a robust
    /// alternative to Shift-select + paste when focus restoration is shaky.
    /// </summary>
    public static void SendBackspace(int count)
    {
        if (count <= 0) return;

        var inputs = new INPUT[count * 2];
        int i = 0;
        for (int j = 0; j < count; j++)
        {
            inputs[i++] = Key(VK_BACK, isUp: false);
            inputs[i++] = Key(VK_BACK, isUp: true);
        }

        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        if (sent != inputs.Length)
        {
            int err = Marshal.GetLastWin32Error();
            throw new InvalidOperationException($"SendInput injected only {sent}/{inputs.Length} Backspace events (Win32 error {err}).");
        }
    }

    private const ushort VK_RIGHT = 0x27;

    private static void SendShiftArrow(int count, ushort arrowVk)
    {
        if (count <= 0) return;

        // Build: Shift↓ [Arrow↓ Arrow↑]*N Shift↑
        var inputs = new INPUT[2 + count * 2];
        int i = 0;
        inputs[i++] = Key(VK_SHIFT, isUp: false);
        for (int j = 0; j < count; j++)
        {
            inputs[i++] = Key(arrowVk, isUp: false);
            inputs[i++] = Key(arrowVk, isUp: true);
        }
        inputs[i++] = Key(VK_SHIFT, isUp: true);

        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        if (sent != inputs.Length)
        {
            int err = Marshal.GetLastWin32Error();
            throw new InvalidOperationException($"SendInput injected only {sent}/{inputs.Length} Shift+Arrow events (Win32 error {err}).");
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
