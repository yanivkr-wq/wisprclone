using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WisprClone.App.Hotkey;

/// <summary>
/// Global low-level keyboard hook (WH_KEYBOARD_LL). Generalised to support
/// either a modifier-only chord (Ctrl+Win) or a single key (Right Alt, F8…)
/// as defined by the active <see cref="HotkeySpec"/>.
///
/// While the chord is active AND <see cref="SwallowConflicts"/> is true, AND
/// the hotkey happens to be Ctrl+Win, the hook eats Windows-shell shortcuts
/// (Win+Ctrl+arrow / D / F4) so virtual-desktop switching doesn't trigger
/// mid-dictation. For non-Ctrl+Win hotkeys there's nothing to suppress.
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

    private readonly LowLevelKeyboardProc _proc;
    private IntPtr _hookId = IntPtr.Zero;

    private bool _ctrlDown;
    private bool _winDown;
    private bool _singleKeyDown;
    private bool _chordActive;

    private readonly HotkeySpec _spec;

    public event Action? ChordPressed;
    public event Action? ChordReleased;

    /// <summary>
    /// When true and the chord is currently held, this hook eats
    /// arrow/D/F4 key presses to prevent virtual-desktop shortcuts.
    /// Only meaningful for the Ctrl+Win chord.
    /// </summary>
    public bool SwallowConflicts { get; set; }

    public LowLevelKeyboardHook() : this(HotkeySpec.CtrlWin) { }

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
        bool isUp = msg == WM_KEYUP || msg == WM_SYSKEYUP;

        if (_spec.IsChord)
        {
            // Modifier-only chord (e.g. Ctrl+Win).
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
            else if (_chordActive && SwallowConflicts && isDown
                     && _spec.RequireCtrl && _spec.RequireWin
                     && (vk == HotkeySpec.VK_LEFT  || vk == HotkeySpec.VK_RIGHT
                         || vk == HotkeySpec.VK_UP || vk == HotkeySpec.VK_DOWN
                         || vk == HotkeySpec.VK_D  || vk == HotkeySpec.VK_F4))
            {
                // Swallow virtual-desktop shortcuts while Ctrl+Win is held.
                return new IntPtr(1);
            }
        }
        else
        {
            // Single-key hotkey (e.g. Right Alt, F8).
            if (vk == _spec.Key!.Value)
            {
                if (isDown && !_singleKeyDown)
                {
                    _singleKeyDown = true;
                    _chordActive = true;
                    SafeRaise(ChordPressed);
                }
                else if (isUp && _singleKeyDown)
                {
                    _singleKeyDown = false;
                    _chordActive = false;
                    SafeRaise(ChordReleased);
                }
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private void UpdateChordState()
    {
        // All required modifiers must be held. Extra modifiers being held
        // (e.g. user pressing Ctrl while their hotkey is Win-only) don't
        // matter — the chord is still considered active.
        bool active = (!_spec.RequireCtrl || _ctrlDown)
                      && (!_spec.RequireWin || _winDown);

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
