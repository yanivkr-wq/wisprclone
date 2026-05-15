using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Serilog;
using WisprClone.App.Injection;
using WisprClone.App.Refinement;
using WisprClone.App.Tracking;

namespace WisprClone.App.Ui;

/// <summary>
/// Floating panel that pops up after a successful dictation (when the user has
/// opted in via settings). Shows four refinement buttons; on click it calls
/// OpenAI to rewrite, then sends Shift+Left × N + Ctrl+V to swap the freshly-
/// pasted text in the destination app for the refined version.
/// </summary>
public partial class RefinementPanel : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    private const int AutoDismissMs = 8000;

    private readonly string _originalText;
    private readonly RefinementService _refiner;
    private readonly ClipboardInjector _injector;
    private readonly DispatcherTimer _autoDismiss;
    private bool _busy;

    // The window that had focus when the panel was shown — typically the app
    // the user just dictated into. We restore focus to it before sending
    // Shift+Left × N and Ctrl+V so the keys go where the original paste went,
    // not into our own panel (which gains focus when the user clicks a button).
    private IntPtr _targetHwnd = IntPtr.Zero;

    public RefinementPanel(string originalText, RefinementService refiner, ClipboardInjector injector)
    {
        InitializeComponent();
        _originalText = originalText;
        _refiner = refiner;
        _injector = injector;

        Loaded += OnLoaded;

        _autoDismiss = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(AutoDismissMs)
        };
        _autoDismiss.Tick += (_, _) =>
        {
            _autoDismiss.Stop();
            if (!_busy) Close();
        };
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Ensure window doesn't grab focus and stays out of Alt+Tab.
        var hwnd = new WindowInteropHelper(this).Handle;
        var ex = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);

        // Capture the currently-foreground window — that's the app the user
        // just dictated into. We'll restore focus to it before sending the
        // Shift+Left / Ctrl+V replacement sequence so it lands where the
        // dictation actually went.
        _targetHwnd = GetForegroundWindow();
        // If the foreground at Loaded is ourselves, fall back to whatever
        // came right before. (Shouldn't happen with NoActivate, but defensive.)
        if (_targetHwnd == hwnd) _targetHwnd = IntPtr.Zero;

        // Position at bottom-center of active monitor, like the pill.
        // DIP-converted so it lands correctly on scaled displays.
        UpdateLayout();
        var workArea = MonitorHelper.GetActiveMonitorWorkAreaInDips(this);
        Left = workArea.Left + (workArea.Width - ActualWidth) / 2.0;
        Top = workArea.Bottom - ActualHeight - 60;

        _autoDismiss.Start();
    }

    // ---- Button handlers ----

    private void OnPro_Click(object sender, RoutedEventArgs e)     => _ = HandleRefineAsync(RefinementStyle.Professional);
    private void OnCasual_Click(object sender, RoutedEventArgs e)  => _ = HandleRefineAsync(RefinementStyle.Casual);
    private void OnShorter_Click(object sender, RoutedEventArgs e) => _ = HandleRefineAsync(RefinementStyle.Shorter);
    private void OnLonger_Click(object sender, RoutedEventArgs e)  => _ = HandleRefineAsync(RefinementStyle.Longer);

    private void OnDismiss_Click(object sender, RoutedEventArgs e)
    {
        _autoDismiss.Stop();
        Close();
    }

    private async Task HandleRefineAsync(RefinementStyle style)
    {
        if (_busy) return;
        _busy = true;
        _autoDismiss.Stop();

        SetBusy($"Refining ({style})…");

        string refined;
        try
        {
            refined = await _refiner.RefineAsync(_originalText, style).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Refinement failed");
            SetError("Refinement failed (see log). Press Esc to dismiss.");
            _busy = false;
            return;
        }

        if (string.IsNullOrWhiteSpace(refined))
        {
            SetError("OpenAI returned empty refinement.");
            _busy = false;
            return;
        }

        Log.Information("Refinement {Style}: \"{Refined}\"", style, refined);
        UsageTracker.RecordRefine(_originalText, refined);

        // Replace previously-pasted text using Backspace + Ctrl+V:
        //   1) push the target app back to foreground (clicking our button
        //      moved focus to us; SetForegroundWindow is allowed because the
        //      user just interacted with our process),
        //   2) wait generously for focus to actually transfer,
        //   3) Backspace × N: deletes the N chars before the cursor in LOGICAL
        //      order — works identically for LTR, RTL, mixed scripts. We
        //      previously tried Shift+Left + Ctrl+V but that depends on visual
        //      vs. logical selection conventions that varied per app and was
        //      brittle on focus changes,
        //   4) Ctrl+V the refined text (already on clipboard via InjectText).
        try
        {
            var foregroundBefore = GetForegroundWindow();

            if (_targetHwnd != IntPtr.Zero)
            {
                SetForegroundWindow(_targetHwnd);
                await Task.Delay(180).ConfigureAwait(true);  // generous, prefers reliability over speed
            }

            var foregroundAfter = GetForegroundWindow();
            var originalLen = _originalText.Length;
            Log.Information(
                "Refine replace: target=0x{T:X} foregroundBefore=0x{Before:X} foregroundAfter=0x{After:X} deleting {Len} chars",
                _targetHwnd.ToInt64(), foregroundBefore.ToInt64(), foregroundAfter.ToInt64(), originalLen);

            // Delete the previously-pasted text via Backspace × N.
            SendInputHelper.SendBackspace(originalLen);

            // Wait for the deletes to register before pasting on top.
            await Task.Delay(80).ConfigureAwait(true);

            // Paste refined (single trailing space to match the cleanup convention).
            var refinedWithSpace = refined.TrimEnd() + " ";
            _injector.InjectText(refinedWithSpace);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Couldn't apply refined text to destination");
        }

        Close();
    }

    /// <summary>
    /// True when the text contains ANY Hebrew / Arabic / Syriac character.
    /// We use "contains" rather than "ends with" because RTL sentences often
    /// end with direction-neutral punctuation (e.g. "...אתמול.") and a strict
    /// last-char check would mis-detect those as LTR.
    /// </summary>
    private static bool ContainsRtl(string text)
    {
        foreach (char c in text)
        {
            if ((c >= 0x0590 && c <= 0x05FF)    // Hebrew
             || (c >= 0x0600 && c <= 0x06FF)    // Arabic
             || (c >= 0x0700 && c <= 0x074F))   // Syriac
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Reliable foreground-window switch. SetForegroundWindow alone is
    /// restricted by Windows when the calling thread isn't the foreground
    /// owner; attaching our input queue to both the current foreground and
    /// the target thread lifts that restriction.
    /// </summary>
    private static void ForceForeground(IntPtr hwnd)
    {
        try
        {
            var fore = GetForegroundWindow();
            uint foreThread = GetWindowThreadProcessId(fore, out _);
            uint targetThread = GetWindowThreadProcessId(hwnd, out _);
            uint ourThread = GetCurrentThreadId();

            bool a1 = foreThread != ourThread   && AttachThreadInput(ourThread, foreThread,   true);
            bool a2 = targetThread != ourThread && AttachThreadInput(ourThread, targetThread, true);
            SetForegroundWindow(hwnd);
            if (a2) AttachThreadInput(ourThread, targetThread, false);
            if (a1) AttachThreadInput(ourThread, foreThread,   false);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "ForceForeground failed; falling back to plain SetForegroundWindow");
            SetForegroundWindow(hwnd);
        }
    }

    private void SetBusy(string message)
    {
        ButtonsPanel.Visibility = Visibility.Collapsed;
        StatusText.Text = message;
        StatusText.Foreground = System.Windows.Media.Brushes.White;
        StatusText.Visibility = Visibility.Visible;
    }

    private void SetError(string message)
    {
        ButtonsPanel.Visibility = Visibility.Collapsed;
        StatusText.Text = message;
        StatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0x80, 0x80));
        StatusText.Visibility = Visibility.Visible;

        // Give the user time to read, then auto-dismiss.
        _autoDismiss.Interval = TimeSpan.FromSeconds(4);
        _autoDismiss.Start();
    }
}
