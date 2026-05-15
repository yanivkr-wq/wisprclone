using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using WisprClone.App.Hotkey;

namespace WisprClone.App.Ui;

/// <summary>
/// Modal dialog that asks the user to press their desired hotkey, then
/// commits whatever they pressed as a <see cref="HotkeySpec"/>.
/// </summary>
public partial class HotkeyCaptureDialog : Window
{
    public HotkeySpec? Captured { get; private set; }

    // Track WPF keys currently down (the running chord).
    private readonly HashSet<Key> _down = new();
    // The most-complete chord we observed while at least one key was held.
    private HotkeySpec? _bestSoFar;

    public HotkeyCaptureDialog(HotkeySpec? current = null)
    {
        InitializeComponent();

        if (current != null)
        {
            CapturedText.Text = "Current: " + current.DisplayName + "  —  press a new chord to replace";
        }

        Focusable = true;
        Loaded += (_, _) => Focus();
    }

    private void OnAnyKeyDown(object sender, KeyEventArgs e)
    {
        // Esc with nothing else held: cancel.
        if (e.Key == Key.Escape && _down.Count == 0)
        {
            DialogResult = false;
            Close();
            return;
        }

        var k = NormaliseKey(e);
        _down.Add(k);
        _bestSoFar = BuildSpec();
        CapturedText.Text = _bestSoFar.DisplayName;
        UseBtn.IsEnabled = true;
        e.Handled = true;
    }

    private void OnAnyKeyUp(object sender, KeyEventArgs e)
    {
        var k = NormaliseKey(e);
        _down.Remove(k);
        e.Handled = true;
    }

    private static Key NormaliseKey(KeyEventArgs e) => e.Key == Key.System ? e.SystemKey : e.Key;

    private HotkeySpec BuildSpec()
    {
        bool ctrl  = _down.Contains(Key.LeftCtrl)  || _down.Contains(Key.RightCtrl);
        bool alt   = _down.Contains(Key.LeftAlt)   || _down.Contains(Key.RightAlt);
        bool shift = _down.Contains(Key.LeftShift) || _down.Contains(Key.RightShift);
        bool win   = _down.Contains(Key.LWin)      || _down.Contains(Key.RWin);

        // Find the first non-modifier key currently held (if any).
        int? key = null;
        foreach (var k in _down)
        {
            if (IsModifierKey(k)) continue;
            key = KeyInterop.VirtualKeyFromKey(k);
            break;
        }

        // If only a modifier-side key is held (e.g. user held Right Alt alone),
        // surface it as the Key so the spec is specific.
        if (key == null && _down.Count == 1)
        {
            var only = _down.GetEnumerator();
            only.MoveNext();
            var k = only.Current;
            if (k == Key.RightAlt) key = HotkeySpec.VK_RMENU;
            else if (k == Key.LeftAlt) key = HotkeySpec.VK_LMENU;
        }

        return new HotkeySpec(ctrl, alt, shift, win, key);
    }

    private static bool IsModifierKey(Key k) =>
        k == Key.LeftCtrl  || k == Key.RightCtrl
        || k == Key.LeftAlt  || k == Key.RightAlt
        || k == Key.LeftShift|| k == Key.RightShift
        || k == Key.LWin     || k == Key.RWin
        || k == Key.System;

    private void OnUse_Click(object sender, RoutedEventArgs e)
    {
        if (_bestSoFar == null) return;
        Captured = _bestSoFar;
        DialogResult = true;
        Close();
    }

    private void OnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
