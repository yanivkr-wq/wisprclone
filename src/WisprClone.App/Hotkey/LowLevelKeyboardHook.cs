using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WisprClone.App.Hotkey;

/// <summary>
/// Global low-level keyboard hook (WH_KEYBOARD_LL). Tracks every currently-held
/// virtual key in a HashSet and, on every key change, asks the current
/// <see cref="HotkeySpec"/> whether the chord is satisfied. Fires
/// ChordPressed when it becomes satisfied; ChordReleased when it stops being.
///
/// When the active hotkey is the Ctrl+Win chord, AND <see cref="SwallowConflicts"/>
/// is true, the hook also eats Win+Ctrl+arrow / D / F4 to prevent Windows
/// virtual-desktop shortcuts firing mid-dictation. For other hotkeys there's
/// nothing to swallow.
/// </summary>
public sealed class LowLevelKeyboardHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;

    private readonly LowLevelKeyboardProc _proc;
    private IntPtr _hookId = IntPtr.Zero;

    private readonly HashSet<int> _pressed = new();
    private bool _chordActive;

    private HotkeySpec _spec;

    public event Action? ChordPressed;
    public event Action? ChordReleased;

    /// <summary>
    /// When true and the active hotkey is Ctrl+Win, the hook swallows
    /// Win+Ctrl+arrow / D / F4 to prevent virtual-desktop shortcuts.
    /// Has no effect for other hotkeys.
    /// </summary>
    public bool SwallowConflicts { get; set; }

    public LowLevelKeyboardHook() : this(HotkeySpec.Default) { }

    public LowLevelKeyboardHook(HotkeySpec spec)
    {
        _spec = spec;
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
        bool isUp   = msg == WM_KEYUP   || msg == WM_SYSKEYUP;

        if (isDown) _pressed.Add(vk);
        else if (isUp) _pressed.Remove(vk);

        // Optionally suppress Windows shell shortcuts while a Ctrl+Win chord
        // is active. Only meaningful when the user's hotkey actually IS
        // Ctrl+Win — otherwise their hotkey doesn't conflict with these.
        if (_chordActive && SwallowConflicts && isDown
            && IsCtrlWinChord(_spec)
            && (vk == HotkeySpec.VK_LSHIFT /* placeholder, never matches */ ||
                vk == 0x25 || vk == 0x26 || vk == 0x27 || vk == 0x28 /* arrows */ ||
                vk == 0x44 /* D */ || vk == 0x73 /* F4 */))
        {
            // Mark handled; don't propagate to the shell.
            return new IntPtr(1);
        }

        UpdateChordState();
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private static bool IsCtrlWinChord(HotkeySpec spec) =>
        spec.Ctrl && spec.Win && !spec.Alt && !spec.Shift && spec.Key == null;

    private void UpdateChordState()
    {
        bool active = _spec.Matches(_pressed);
        if (active && !_chordActive)
        {
            _chordActive = true;
            SafeRaise(ChordPressed);
        }
        else if (!active && _chordActive)
        {
            _chordActive = false;
            SafeRaise(ChordReleased);
        }
    }

    /// <summary>
    /// Replace the active hotkey at runtime (e.g. after the user changes it in
    /// Settings). Resets the chord-active state.
    /// </summary>
    public void UpdateSpec(HotkeySpec spec)
    {
        _spec = spec;
        _pressed.Clear();
        if (_chordActive)
        {
            _chordActive = false;
            SafeRaise(ChordReleased);
        }
    }

    public HotkeySpec ActiveSpec => _spec;

    private static void SafeRaise(Action? evt)
    {
        try { evt?.Invoke(); }
        catch (Exception ex) { Serilog.Log.Error(ex, "Hotkey handler threw"); }
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
