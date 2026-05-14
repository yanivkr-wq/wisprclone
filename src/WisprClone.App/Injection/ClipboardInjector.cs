using System;
using System.Threading;
using System.Windows;
using Serilog;

namespace WisprClone.App.Injection;

/// <summary>
/// Pastes a text snippet into whatever window currently has focus, via:
///   1. saving the existing clipboard text (best-effort, text only),
///   2. placing our text on the clipboard,
///   3. SendInput Ctrl+V,
///   4. waiting ~100 ms for the paste to land,
///   5. restoring the prior text.
///
/// Limitations (documented for v1):
///   - Only TEXT clipboard content is preserved. Images / files / rich
///     content on the prior clipboard are lost.
///   - Apps that explicitly block synthetic input (some games, secure
///     password fields) won't receive the paste.
///   - Elevated windows are unreachable from non-elevated input.
/// </summary>
public sealed class ClipboardInjector
{
    private const int PasteCompletionWaitMs = 100;
    private const int ClipboardRetries = 5;
    private const int ClipboardRetryDelayMs = 30;

    /// <summary>
    /// Must be invoked on a thread with STA apartment state — typically the
    /// WPF UI dispatcher thread. WPF Clipboard.* requires STA.
    /// </summary>
    public void InjectText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            Log.Information("Inject: empty text, skipping");
            return;
        }

        var savedText = TryGetClipboardText();

        if (!TrySetClipboardText(text))
        {
            Log.Error("Inject: failed to place text on clipboard after {Retries} retries", ClipboardRetries);
            return;
        }

        try
        {
            SendInputHelper.SendCtrlV();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Inject: SendInput failed");
            // Still try to restore — don't leave the clipboard polluted.
        }

        // Give the focused app time to receive WM_PASTE / process Ctrl+V.
        Thread.Sleep(PasteCompletionWaitMs);

        // Restore prior clipboard text. If there was none, just clear ours so
        // a stale transcript doesn't sit there indefinitely.
        if (savedText != null)
        {
            TrySetClipboardText(savedText);
        }
        else
        {
            TryClearClipboard();
        }
    }

    private static string? TryGetClipboardText()
    {
        for (int attempt = 0; attempt < ClipboardRetries; attempt++)
        {
            try
            {
                return Clipboard.ContainsText() ? Clipboard.GetText() : null;
            }
            catch (Exception ex) when (attempt < ClipboardRetries - 1)
            {
                Log.Debug(ex, "Clipboard read attempt {N} failed, retrying", attempt + 1);
                Thread.Sleep(ClipboardRetryDelayMs);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Could not read prior clipboard — will not restore");
                return null;
            }
        }
        return null;
    }

    private static bool TrySetClipboardText(string text)
    {
        for (int attempt = 0; attempt < ClipboardRetries; attempt++)
        {
            try
            {
                // SetDataObject with copy=true detaches the data from our process —
                // so other apps can read it even after our process exits.
                Clipboard.SetDataObject(text, copy: true);
                return true;
            }
            catch (Exception ex) when (attempt < ClipboardRetries - 1)
            {
                Log.Debug(ex, "Clipboard write attempt {N} failed, retrying", attempt + 1);
                Thread.Sleep(ClipboardRetryDelayMs);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Clipboard write final attempt failed");
                return false;
            }
        }
        return false;
    }

    private static void TryClearClipboard()
    {
        try { Clipboard.Clear(); }
        catch (Exception ex) { Log.Debug(ex, "Failed to clear clipboard (non-fatal)"); }
    }
}
