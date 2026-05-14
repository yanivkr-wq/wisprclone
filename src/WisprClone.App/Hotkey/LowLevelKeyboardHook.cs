using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WisprClone.App.Hotkey;

/// <summary>
/// Global low-level keyboard hook (WH_KEYBOARD_LL).
/// Detects Ctrl + Win chord press/release events. While the chord is active
/// AND <see cref="SwallowConflicts"/> is true, it eats Windows-shell shortcuts
/// that would otherwise fire (Win+Ctrl+Arrow, Win+Ctrl+D, Win+Ctrl+F4)
/// so virtual-desktop switching doesn't trigger mid-dictation.
/// </summary>
public sealed class LowLevelKeyboardHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;

    private const int VK_LCONTROL = 0xA2;
    private const int VK_RCONTROL = 0xA3;
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;

    private const int VK_LEFT = 0x25;
    private const int VK_UP = 0x26;
    private const int VK_RIGHT = 0x27;
    private const int VK_DOWN = 0x28;
    private const int VK_D = 0x44;
    private const int VK_F4 = 0x73;

    private readonly LowLevelKeyboardProc _proc;
    private IntPtr _hookId = IntPtr.Zero;

    private bool _ctrlDown;
    private bool _winDown;
    private bool _chordActive;

    public event Action? ChordPressed;
    public event Action? ChordReleased;

    /// <summary>
    /// When true and the chord is currently held, this hook eats
    /// arrow/D/F4 key presses to prevent virtual-desktop shortcuts.
    /// </summary>
    public bool SwallowConflicts { get; set; }

    public LowLevelKeyboardHook()
    {
        _proc = HookCallback;
        _hookId = SetHook(_proc);
        if (_hookId == IntPtr.Zero)
        {
            int err = Marshal.GetLastWin32Error();
            throw new InvalidOperationException($"Failed to install keyboard hook (Win32 error {err}).");
        }
    }

    private static IntPtr SetHook(LowLevelKeyboardProc proc)
    {
        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule!;
        return SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(curModule.ModuleName), 0);
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0)
            return CallNextHookEx(_hookId, nCode, wParam, lParam);

        var kb = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
        int msg = wParam.ToInt32();
        int vk = (int)kb.vkCode;

        bool isDown = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
        bool isUp = msg == WM_KEYUP || msg == WM_SYSKEYUP;

        if (vk == VK_LCONTROL || vk == VK_RCONTROL)
        {
            if (isDown) _ctrlDown = true;
            else if (isUp) _ctrlDown = false;
            UpdateChordState();
        }
        else if (vk == VK_LWIN || vk == VK_RWIN)
        {
            if (isDown) _winDown = true;
            else if (isUp) _winDown = false;
            UpdateChordState();
        }
        else if (_chordActive && SwallowConflicts && isDown)
        {
            if (vk == VK_LEFT || vk == VK_RIGHT || vk == VK_UP || vk == VK_DOWN ||
                vk == VK_D || vk == VK_F4)
            {
                // Mark as handled — do not pass to Windows shell.
                return new IntPtr(1);
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private void UpdateChordState()
    {
        bool both = _ctrlDown && _winDown;
        if (both && !_chordActive)
        {
            _chordActive = true;
            try { ChordPressed?.Invoke(); }
            catch (Exception ex) { Serilog.Log.Error(ex, "ChordPressed handler threw"); }
        }
        else if (!both && _chordActive)
        {
            _chordActive = false;
            try { ChordReleased?.Invoke(); }
            catch (Exception ex) { Serilog.Log.Error(ex, "ChordReleased handler threw"); }
        }
    }

    public void Dispose()
    {
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);
}
