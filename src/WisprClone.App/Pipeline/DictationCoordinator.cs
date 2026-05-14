using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Serilog;
using WisprClone.App.Audio;
using WisprClone.App.Hotkey;
using WisprClone.App.Injection;
using WisprClone.App.PostProcess;
using WisprClone.App.Transcription;

namespace WisprClone.App.Pipeline;

/// <summary>
/// Orchestrates the full dictation flow:
///   hotkey press   → start mic capture
///   hotkey release → stop capture (PTT)  /  toggle stops on second press
///   recording done → POST WAV to OpenAI Whisper API
///   text returned  → cleanup → inject into focused window (Ctrl+V)
/// Also enforces the max-recording-length safety cap.
/// </summary>
public sealed class DictationCoordinator : IDisposable
{
    private readonly LowLevelKeyboardHook _hook;
    private readonly MicCapture _mic;
    private readonly WhisperEngine? _whisper;
    private readonly ClipboardInjector _injector;
    private HotkeyMode _mode;
    private int _maxRecordingSeconds;
    private readonly Dispatcher _dispatcher;
    private readonly System.Threading.Timer _maxLengthTimer;

    private bool _isRecording;
    private DateTime _recordStartUtc;

    // External listeners (pill UI, history store, etc.).
    public event Action? RecordingStarted;
    public event Action? RecordingStopped;
    /// <summary>Fires after every transcription attempt; text is the cleaned result, or "" on failure / silence.</summary>
    public event Action<string>? TranscriptionCompleted;

    public DictationCoordinator(
        LowLevelKeyboardHook hook,
        MicCapture mic,
        WhisperEngine? whisper,
        HotkeyMode mode,
        int maxRecordingSeconds = 120)
    {
        _hook = hook;
        _mic = mic;
        _whisper = whisper;
        _injector = new ClipboardInjector();
        _mode = mode;
        _maxRecordingSeconds = maxRecordingSeconds;
        _dispatcher = System.Windows.Application.Current?.Dispatcher
            ?? Dispatcher.CurrentDispatcher;
        _maxLengthTimer = new System.Threading.Timer(OnMaxLengthReached, null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start()
    {
        _hook.ChordPressed += OnChordPressed;
        _hook.ChordReleased += OnChordReleased;
        _mic.RecordingComplete += OnRecordingComplete;
    }

    /// <summary>
    /// Lets the settings window change hotkey behaviour at runtime without
    /// rebuilding the whole pipeline. Will not interrupt an in-flight recording.
    /// </summary>
    public void UpdateSettings(HotkeyMode mode, int maxRecordingSeconds)
    {
        _mode = mode;
        _maxRecordingSeconds = maxRecordingSeconds;
        Log.Information("Coordinator settings updated: mode={Mode}, maxSec={Max}", mode, maxRecordingSeconds);
    }

    private void OnChordPressed()
    {
        if (_mode == HotkeyMode.PushToTalk)
        {
            BeginRecording();
        }
        else // Toggle
        {
            if (_isRecording) EndRecording();
            else BeginRecording();
        }
    }

    private void OnChordReleased()
    {
        if (_mode == HotkeyMode.PushToTalk && _isRecording)
        {
            EndRecording();
        }
    }

    private void BeginRecording()
    {
        if (_isRecording) return;

        var path = _mic.StartRecording();
        if (path == null)
        {
            Log.Warning("Failed to start recording");
            return;
        }

        _isRecording = true;
        _recordStartUtc = DateTime.UtcNow;
        _hook.SwallowConflicts = true;
        _maxLengthTimer.Change(TimeSpan.FromSeconds(_maxRecordingSeconds), Timeout.InfiniteTimeSpan);

        Log.Information("Recording started → {Path}", path);
        SafeRaise(RecordingStarted);
    }

    private void EndRecording()
    {
        if (!_isRecording) return;

        _maxLengthTimer.Change(Timeout.Infinite, Timeout.Infinite);
        _mic.StopRecording();
        _hook.SwallowConflicts = false;

        var duration = DateTime.UtcNow - _recordStartUtc;
        _isRecording = false;

        Log.Information("Recording stopped after {Duration:F2}s — transcribing…", duration.TotalSeconds);
        SafeRaise(RecordingStopped);
    }

    private void OnRecordingComplete(string wavPath)
    {
        var recordedFor = DateTime.UtcNow - _recordStartUtc;
        // Run transcription on a thread-pool thread so we don't block the
        // capture thread that just finished closing the file.
        _ = Task.Run(() => TranscribeAsync(wavPath, recordedFor));
    }

    private async Task TranscribeAsync(string wavPath, TimeSpan recordedFor)
    {
        if (_whisper == null)
        {
            Log.Warning("Whisper engine not configured — saved {Path}", wavPath);
            SafeRaise(TranscriptionCompleted, string.Empty);
            return;
        }

        var sw = Stopwatch.StartNew();
        string text;
        try
        {
            text = await _whisper.TranscribeAsync(wavPath).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            sw.Stop();
            Log.Error(ex, "Transcription failed for {Path}", wavPath);
            SafeRaise(TranscriptionCompleted, string.Empty);
            return;
        }

        sw.Stop();

        var cleaned = Cleanup.Apply(text);

        if (string.IsNullOrWhiteSpace(cleaned))
        {
            Log.Information("Transcribed in {LatencyMs}ms (rec {RecMs}ms): (empty / silence) — nothing injected",
                sw.ElapsedMilliseconds, (long)recordedFor.TotalMilliseconds);
            SafeRaise(TranscriptionCompleted, string.Empty);
            return;
        }

        Log.Information("Transcribed in {LatencyMs}ms (rec {RecMs}ms):\n    \"{Text}\"",
            sw.ElapsedMilliseconds, (long)recordedFor.TotalMilliseconds, cleaned.TrimEnd());

        // Clipboard + SendInput must run on a WPF dispatcher (STA) thread.
        try
        {
            await _dispatcher.InvokeAsync(() => _injector.InjectText(cleaned));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to inject text into focused window");
        }

        SafeRaise(TranscriptionCompleted, cleaned);
    }

    private static void SafeRaise(Action? evt)
    {
        try { evt?.Invoke(); }
        catch (Exception ex) { Log.Error(ex, "Event handler threw"); }
    }

    private static void SafeRaise<T>(Action<T>? evt, T arg)
    {
        try { evt?.Invoke(arg); }
        catch (Exception ex) { Log.Error(ex, "Event handler threw"); }
    }

    private void OnMaxLengthReached(object? state)
    {
        _dispatcher.Invoke(() =>
        {
            if (_isRecording)
            {
                Log.Warning("Max recording length ({MaxSeconds}s) reached — auto-stopping",
                    _maxRecordingSeconds);
                EndRecording();
            }
        });
    }

    public void Dispose()
    {
        _maxLengthTimer.Dispose();
        if (_isRecording) EndRecording();
        _hook.ChordPressed -= OnChordPressed;
        _hook.ChordReleased -= OnChordReleased;
        _mic.RecordingComplete -= OnRecordingComplete;
    }
}
