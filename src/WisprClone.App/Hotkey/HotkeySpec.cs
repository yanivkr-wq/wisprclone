using System;

namespace WisprClone.App.Hotkey;

/// <summary>
/// A single hotkey shape. Two kinds are currently supported:
///   * a modifier-only chord (e.g. Ctrl+Win with no third key),
///   * a single key (e.g. Right Alt or F8).
/// Future versions can extend this to combinations like Alt+Space.
/// </summary>
public sealed record HotkeySpec(string Name, bool RequireCtrl, bool RequireWin, int? Key)
{
    public bool IsChord => Key == null;

    // VK codes — kept inline so the hook doesn't need a separate constants file.
    public const int VK_LEFT      = 0x25;
    public const int VK_UP        = 0x26;
    public const int VK_RIGHT     = 0x27;
    public const int VK_DOWN      = 0x28;
    public const int VK_D         = 0x44;
    public const int VK_F4        = 0x73;
    public const int VK_F8        = 0x77;
    public const int VK_F9        = 0x78;
    public const int VK_RMENU     = 0xA5; // Right Alt
    public const int VK_SPACE     = 0x20;

    public static readonly HotkeySpec CtrlWin  = new("Ctrl+Win",  RequireCtrl: true,  RequireWin: true,  Key: null);
    public static readonly HotkeySpec RightAlt = new("Right Alt", RequireCtrl: false, RequireWin: false, Key: VK_RMENU);
    public static readonly HotkeySpec F8       = new("F8",        RequireCtrl: false, RequireWin: false, Key: VK_F8);
    public static readonly HotkeySpec F9       = new("F9",        RequireCtrl: false, RequireWin: false, Key: VK_F9);

    public static readonly HotkeySpec[] Available = { CtrlWin, RightAlt, F8, F9 };

    public static HotkeySpec Parse(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return CtrlWin;
        var key = s.Trim().ToLowerInvariant();
        return key switch
        {
            "ctrl+win" or "win+ctrl" or "ctrlwin" => CtrlWin,
            "right alt" or "rightalt" or "ralt"    => RightAlt,
            "f8"                                    => F8,
            "f9"                                    => F9,
            _                                       => CtrlWin // unknown → safe default
        };
    }
}
