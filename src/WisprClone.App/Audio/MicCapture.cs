using System;
using System.IO;
using NAudio.Wave;

namespace WisprClone.App.Audio;

/// <summary>
/// Records microphone audio to a 16 kHz / 16-bit / mono WAV file.
/// Uses NAudio's WaveInEvent (WinMM) for broad device compatibility;
/// can be swapped for WasapiCapture later if WinMM proves problematic.
/// </summary>
public sealed class MicCapture : IDisposable
{
    private WaveInEvent? _waveIn;
    private WaveFileWriter? _writer;
    private string? _currentPath;
    private readonly object _lock = new();

    /// <summary>
    /// Fires once the WAV file is fully flushed and closed and is safe to read.
    /// Argument is the absolute path to the saved file.
    /// </summary>
    public event Action<string>? RecordingComplete;

    /// <summary>
    /// Begins a new recording. Returns the destination file path,
    /// or null if a recording is already in progress.
    /// </summary>
    public string? StartRecording()
    {
        lock (_lock)
        {
            if (_waveIn != null) return null;

            var debugDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WisprClone", "debug");
            Directory.CreateDirectory(debugDir);

            _currentPath = Path.Combine(debugDir, $"rec_{DateTime.Now:yyyyMMdd_HHmmss_fff}.wav");

            _waveIn = new WaveInEvent
            {
                WaveFormat = new WaveFormat(16000, 16, 1),
                BufferMilliseconds = 50
            };

            _writer = new WaveFileWriter(_currentPath, _waveIn.WaveFormat);
            _waveIn.DataAvailable += OnDataAvailable;
            _waveIn.RecordingStopped += OnRecordingStopped;

            _waveIn.StartRecording();
            return _currentPath;
        }
    }

    /// <summary>
    /// Stops the current recording. Returns the saved file path, or null if not recording.
    /// </summary>
    public string? StopRecording()
    {
        WaveInEvent? captureToStop;
        string? path;

        lock (_lock)
        {
            if (_waveIn == null) return null;
            captureToStop = _waveIn;
            path = _currentPath;
        }

        // StopRecording is async — the writer is finalised in OnRecordingStopped.
        captureToStop.StopRecording();
        return path;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        // Capture thread — guard against late frames after stop.
        var writer = _writer;
        if (writer != null && e.BytesRecorded > 0)
        {
            writer.Write(e.Buffer, 0, e.BytesRecorded);
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        string? completedPath;

        lock (_lock)
        {
            try
            {
                _writer?.Flush();
                _writer?.Dispose();
            }
            finally
            {
                _writer = null;
            }

            try
            {
                _waveIn?.Dispose();
            }
            finally
            {
                _waveIn = null;
            }

            completedPath = _currentPath;
            _currentPath = null;
        }

        if (e.Exception != null)
        {
            Serilog.Log.Error(e.Exception, "WaveIn recording stopped with exception");
            return;
        }

        if (completedPath != null)
        {
            RecordingComplete?.Invoke(completedPath);
        }
    }

    public void Dispose()
    {
        StopRecording();
    }
}
