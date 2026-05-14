using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
// Disambiguate against System.Drawing.Color, which the global usings pull in
// when UseWindowsForms is enabled on the project.
using Color = System.Windows.Media.Color;

namespace WisprClone.App.Ui;

/// <summary>
/// The Wispr-style floating overlay near the active monitor's bottom-center.
/// Always-on-top, click-through (mouse passes through to whatever's below),
/// never steals focus. Three states: Hidden, Recording, Transcribing.
/// </summary>
public partial class FloatingPill : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    private static readonly Color RecordingColor = Color.FromRgb(0xFF, 0x44, 0x44);
    private static readonly Color TranscribingColor = Color.FromRgb(0xFF, 0xC0, 0x44);

    public FloatingPill()
    {
        InitializeComponent();
        Visibility = Visibility.Hidden;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Apply click-through + tool-window + no-activate styles to the HWND.
        var hwnd = new WindowInteropHelper(this).Handle;
        var ex = GetWindowLong(hwnd, GWL_EXSTYLE);
        ex |= WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
        SetWindowLong(hwnd, GWL_EXSTYLE, ex);
    }

    /// <summary>
    /// Apply a state change. Safe to call from any thread.
    /// </summary>
    public void SetState(PillState state)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(() => SetState(state)));
            return;
        }

        switch (state)
        {
            case PillState.Hidden:
                Visibility = Visibility.Hidden;
                break;

            case PillState.Recording:
                Indicator.Fill = new SolidColorBrush(RecordingColor);
                StatusText.Text = "Recording";
                ShowAtActiveMonitor();
                break;

            case PillState.Transcribing:
                Indicator.Fill = new SolidColorBrush(TranscribingColor);
                StatusText.Text = "Transcribing…";
                ShowAtActiveMonitor();
                break;
        }
    }

    private void ShowAtActiveMonitor()
    {
        // SizeToContent layout pass: WPF needs the window to be laid out before
        // ActualWidth / ActualHeight are accurate, so force it.
        if (!IsLoaded)
        {
            Show();
        }

        UpdateLayout();

        var workArea = MonitorHelper.GetActiveMonitorWorkArea();
        Left = workArea.Left + (workArea.Width - ActualWidth) / 2.0;
        Top = workArea.Bottom - ActualHeight - 60; // 60 px above bottom

        if (Visibility != Visibility.Visible)
        {
            Show();
        }
    }
}
