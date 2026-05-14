using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Serilog;

namespace WisprClone.App.Tray;

public sealed record HistoryEntry(string Text, DateTime TimestampUtc);

/// <summary>
/// Keeps the last ~50 transcriptions in memory and persists them to
/// %APPDATA%\WisprClone\history.json on every change. FIFO eviction.
/// </summary>
public sealed class HistoryStore
{
    private const int MaxEntries = 50;

    private readonly string _path;
    private readonly List<HistoryEntry> _entries = new();
    private readonly object _lock = new();

    public event Action? Changed;

    public HistoryStore()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WisprClone");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "history.json");

        Load();
    }

    public IReadOnlyList<HistoryEntry> Snapshot()
    {
        lock (_lock)
        {
            return _entries.ToList();
        }
    }

    public void Add(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        lock (_lock)
        {
            _entries.Insert(0, new HistoryEntry(text, DateTime.UtcNow));
            while (_entries.Count > MaxEntries)
            {
                _entries.RemoveAt(_entries.Count - 1);
            }
        }

        TrySave();
        Changed?.Invoke();
    }

    private void Load()
    {
        if (!File.Exists(_path)) return;

        try
        {
            var json = File.ReadAllText(_path);
            var loaded = JsonSerializer.Deserialize<List<HistoryEntry>>(json, SerializerOptions);
            if (loaded != null)
            {
                lock (_lock)
                {
                    _entries.Clear();
                    _entries.AddRange(loaded.Take(MaxEntries));
                }
                Log.Information("Loaded {Count} history entries from {Path}", _entries.Count, _path);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load history from {Path} — starting fresh", _path);
        }
    }

    private void TrySave()
    {
        try
        {
            List<HistoryEntry> snapshot;
            lock (_lock) { snapshot = _entries.ToList(); }
            var json = JsonSerializer.Serialize(snapshot, SerializerOptions);
            File.WriteAllText(_path, json);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to persist history to {Path}", _path);
        }
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}
