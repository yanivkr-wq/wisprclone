using System;
using System.Collections.Generic;
using System.Linq;

namespace WisprClone.App.Hotkey;

/// <summary>
/// A general hotkey definition: zero or more modifiers (Ctrl / Alt / Shift / Win)
/// plus an optional non-modifier key. With Key==null it's a modifier-only chord
/// (e.g. Ctrl+Win). With Key set, the user must hold those exact modifiers AND
/// that key (e.g. Ctrl+Shift+F8, or just F8 with no modifiers).
///
/// Modifier matching is EXACT: a "Ctrl+Win" hotkey will NOT fire when Ctrl+Alt+Win
/// is held — that prevents accidental triggers when the user types other chords.
/// </summary>
public sealed record HotkeySpec(bool Ctrl, bool Alt, bool Shift, bool Win, int? Key)
{
    // Virtual-key codes we need.
    public const int VK_LSHIFT   = 0xA0;
    public const int VK_RSHIFT   = 0xA1;
    public const int VK_LCONTROL = 0xA2;
    public const int VK_RCONTROL = 0xA3;
    public const int VK_LMENU    = 0xA4; // Left Alt
    public const int VK_RMENU    = 0xA5; // Right Alt
    public const int VK_LWIN     = 0x5B;
    public const int VK_RWIN     = 0x5C;

    public bool IsChord => Key == null;

    public static readonly HotkeySpec Default = new(Ctrl: true, Alt: false, Shift: false, Win: true, Key: null);

    /// <summary>Returns true if the currently held set of VKs satisfies this spec.</summary>
    public bool Matches(HashSet<int> pressedVks)
    {
        bool ctrl  = pressedVks.Contains(VK_LCONTROL) || pressedVks.Contains(VK_RCONTROL);
        bool alt   = pressedVks.Contains(VK_LMENU)    || pressedVks.Contains(VK_RMENU);
        bool shift = pressedVks.Contains(VK_LSHIFT)   || pressedVks.Contains(VK_RSHIFT);
        bool win   = pressedVks.Contains(VK_LWIN)     || pressedVks.Contains(VK_RWIN);

        // Special case: if the user's Key is itself an alt key, the Alt modifier
        // is implicitly set whenever that key is held — so don't require Alt=false.
        bool keyIsAlt = Key == VK_LMENU || Key == VK_RMENU;
        bool keyIsCtrl = Key == VK_LCONTROL || Key == VK_RCONTROL;
        bool keyIsShift = Key == VK_LSHIFT || Key == VK_RSHIFT;
        bool keyIsWin = Key == VK_LWIN || Key == VK_RWIN;

        if (Ctrl  != ctrl  && !keyIsCtrl ) return false;
        if (Alt   != alt   && !keyIsAlt  ) return false;
        if (Shift != shift && !keyIsShift) return false;
        if (Win   != win   && !keyIsWin  ) return false;

        if (Key.HasValue && !pressedVks.Contains(Key.Value)) return false;
        return true;
    }

    /// <summary>Human-readable label like "Ctrl + Win" or "Ctrl + Shift + F8".</summary>
    public string DisplayName
    {
        get
        {
            var parts = new List<string>();
            if (Ctrl  && !(Key == VK_LCONTROL || Key == VK_RCONTROL)) parts.Add("Ctrl");
            if (Alt   && !(Key == VK_LMENU    || Key == VK_RMENU))     parts.Add("Alt");
            if (Shift && !(Key == VK_LSHIFT   || Key == VK_RSHIFT))    parts.Add("Shift");
            if (Win   && !(Key == VK_LWIN     || Key == VK_RWIN))      parts.Add("Win");
            if (Key.HasValue) parts.Add(VkLabel(Key.Value));
            if (parts.Count == 0) return "(none)";
            return string.Join(" + ", parts);
        }
    }

    // ---------------- Parsing & formatting ----------------

    /// <summary>Parses strings like "Ctrl+Win", "Right Alt", "Ctrl+Shift+F8", "F8".</summary>
    public static HotkeySpec Parse(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return Default;

        bool ctrl = false, alt = false, shift = false, win = false;
        int? key = null;

        foreach (var rawPart in s.Split('+', StringSplitOptions.RemoveEmptyEntries))
        {
            var part = rawPart.Trim();
            switch (part.ToLowerInvariant())
            {
                case "ctrl": case "control":     ctrl = true; break;
                case "alt":                       alt = true; break;
                case "shift":                     shift = true; break;
                case "win": case "windows": case "meta": win = true; break;
                case "right alt": case "ralt":    key = VK_RMENU;    alt = true; break;
                case "left alt":  case "lalt":    key = VK_LMENU;    alt = true; break;
                case "right ctrl": case "rctrl":  key = VK_RCONTROL; ctrl = true; break;
                case "left ctrl":  case "lctrl":  key = VK_LCONTROL; ctrl = true; break;
                default:
                    if (TryParseKey(part, out var vk)) key = vk;
                    break;
            }
        }

        return new HotkeySpec(ctrl, alt, shift, win, key);
    }

    public string ToStorageString() => DisplayName;

    private static bool TryParseKey(string s, out int vk)
    {
        var u = s.ToUpperInvariant();
        // Function keys F1..F24
        if (u.StartsWith("F") && int.TryParse(u[1..], out var n) && n >= 1 && n <= 24)
        {
            vk = 0x6F + n;          // F1 = 0x70, so 0x6F + 1
            return true;
        }
        // Single letter A..Z
        if (u.Length == 1 && u[0] >= 'A' && u[0] <= 'Z')
        {
            vk = u[0];
            return true;
        }
        // Single digit 0..9
        if (u.Length == 1 && u[0] >= '0' && u[0] <= '9')
        {
            vk = u[0];
            return true;
        }
        switch (u)
        {
            case "SPACE":    vk = 0x20; return true;
            case "ENTER": case "RETURN": vk = 0x0D; return true;
            case "TAB":      vk = 0x09; return true;
            case "ESC": case "ESCAPE": vk = 0x1B; return true;
            case "CAPSLOCK": vk = 0x14; return true;
            case "BACKSPACE": vk = 0x08; return true;
            case "INSERT":   vk = 0x2D; return true;
            case "DELETE":   vk = 0x2E; return true;
            case "HOME":     vk = 0x24; return true;
            case "END":      vk = 0x23; return true;
            case "PAGEUP":   vk = 0x21; return true;
            case "PAGEDOWN": vk = 0x22; return true;
            case "LEFT":     vk = 0x25; return true;
            case "UP":       vk = 0x26; return true;
            case "RIGHT":    vk = 0x27; return true;
            case "DOWN":     vk = 0x28; return true;
        }
        vk = 0;
        return false;
    }

    private static string VkLabel(int vk) => vk switch
    {
        VK_LMENU    => "Left Alt",
        VK_RMENU    => "Right Alt",
        VK_LCONTROL => "Left Ctrl",
        VK_RCONTROL => "Right Ctrl",
        VK_LSHIFT   => "Left Shift",
        VK_RSHIFT   => "Right Shift",
        VK_LWIN     => "Left Win",
        VK_RWIN     => "Right Win",
        0x20 => "Space",
        0x0D => "Enter",
        0x09 => "Tab",
        0x1B => "Esc",
        0x14 => "Caps Lock",
        0x08 => "Backspace",
        0x2D => "Insert",
        0x2E => "Delete",
        0x24 => "Home",
        0x23 => "End",
        0x21 => "PageUp",
        0x22 => "PageDown",
        0x25 => "Left",
        0x26 => "Up",
        0x27 => "Right",
        0x28 => "Down",
        _ when vk >= 0x70 && vk <= 0x87 => $"F{vk - 0x6F}",
        _ when vk >= 'A' && vk <= 'Z'    => ((char)vk).ToString(),
        _ when vk >= '0' && vk <= '9'    => ((char)vk).ToString(),
        _ => $"VK{vk:X2}"
    };
}
