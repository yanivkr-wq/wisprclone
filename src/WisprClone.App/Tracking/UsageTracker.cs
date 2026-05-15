using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Serilog;

namespace WisprClone.App.Tracking;

/// <summary>
/// Static, file-backed counter for OpenAI API usage. Tracks transcription
/// seconds + refinement / translation calls (with rough token estimates)
/// per day, persists to %APPDATA%\WisprClone\usage.json, and estimates spend
/// using the published per-unit pricing for the models we call.
///
/// Self-tracked (not pulled from OpenAI's billing API), so the dollar figure
/// is an estimate within a few %, not authoritative.
/// </summary>
public static class UsageTracker
{
    // Published pricing as of mid-2026 — adjust if OpenAI changes them.
    private const double WhisperUsdPerSec    = 0.006 / 60.0;            // $0.006 per minute
    private const double Gpt4oMiniInUsdPerTk = 0.15 / 1_000_000.0;       // $0.15 per 1M input tokens
    private const double Gpt4oMiniOutUsdPerTk= 0.60 / 1_000_000.0;       // $0.60 per 1M output tokens

    private static readonly object _lock = new();
    private static UsageStore _store = new();
    private static string? _path;
    private static bool _initialized;

    public static void Initialize()
    {
        if (_initialized) return;
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WisprClone");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "usage.json");
        _store = Load(_path);
        _initialized = true;
        Log.Information("UsageTracker initialized ({Records} day records loaded)", _store.Records.Count);
    }

    public static void RecordWhisper(double audioSeconds)
    {
        if (audioSeconds <= 0) return;
        lock (_lock)
        {
            var rec = GetOrCreateTodayRecord();
            rec.WhisperCount++;
            rec.WhisperSeconds += audioSeconds;
            Save();
        }
    }

    public static void RecordRefine(string input, string output)
    {
        lock (_lock)
        {
            var rec = GetOrCreateTodayRecord();
            rec.RefineCount++;
            rec.RefineInTokens += EstimateTokens(input);
            rec.RefineOutTokens += EstimateTokens(output);
            Save();
        }
    }

    public static void RecordTranslate(string input, string output)
    {
        lock (_lock)
        {
            var rec = GetOrCreateTodayRecord();
            rec.TranslateCount++;
            rec.TranslateInTokens += EstimateTokens(input);
            rec.TranslateOutTokens += EstimateTokens(output);
            Save();
        }
    }

    public static UsageSummary GetSummary()
    {
        lock (_lock)
        {
            var today = TodayKey();
            var monthPrefix = DateTime.UtcNow.ToString("yyyy-MM");

            var todayRec = _store.Records.FirstOrDefault(r => r.Date == today) ?? new UsageRecord { Date = today };
            var monthRecs = _store.Records.Where(r => r.Date.StartsWith(monthPrefix)).ToList();
            var allRecs = _store.Records;

            return new UsageSummary
            {
                TodayTranscriptions = todayRec.WhisperCount,
                TodayAudioMin       = todayRec.WhisperSeconds / 60.0,
                TodayRefines        = todayRec.RefineCount,
                TodayTranslates     = todayRec.TranslateCount,
                TodayCostUsd        = CostOf(todayRec),

                MonthTranscriptions = monthRecs.Sum(r => r.WhisperCount),
                MonthAudioMin       = monthRecs.Sum(r => r.WhisperSeconds) / 60.0,
                MonthRefines        = monthRecs.Sum(r => r.RefineCount),
                MonthTranslates     = monthRecs.Sum(r => r.TranslateCount),
                MonthCostUsd        = monthRecs.Sum(CostOf),

                LifetimeTranscriptions = allRecs.Sum(r => r.WhisperCount),
                LifetimeAudioMin       = allRecs.Sum(r => r.WhisperSeconds) / 60.0,
                LifetimeRefines        = allRecs.Sum(r => r.RefineCount),
                LifetimeTranslates     = allRecs.Sum(r => r.TranslateCount),
                LifetimeCostUsd        = allRecs.Sum(CostOf),
            };
        }
    }

    private static double CostOf(UsageRecord r) =>
        r.WhisperSeconds * WhisperUsdPerSec
        + r.RefineInTokens   * Gpt4oMiniInUsdPerTk
        + r.RefineOutTokens  * Gpt4oMiniOutUsdPerTk
        + r.TranslateInTokens  * Gpt4oMiniInUsdPerTk
        + r.TranslateOutTokens * Gpt4oMiniOutUsdPerTk;

    /// <summary>
    /// Crude token-count estimate. Real tokenisation is BPE and varies by
    /// language; ~3 chars/token is a reasonable average across English and
    /// Hebrew for short sentences. Within ~30% of actual.
    /// </summary>
    private static int EstimateTokens(string text) =>
        string.IsNullOrEmpty(text) ? 0 : Math.Max(1, text.Length / 3);

    private static string TodayKey() => DateTime.UtcNow.ToString("yyyy-MM-dd");

    private static UsageRecord GetOrCreateTodayRecord()
    {
        var today = TodayKey();
        var rec = _store.Records.FirstOrDefault(r => r.Date == today);
        if (rec == null)
        {
            rec = new UsageRecord { Date = today };
            _store.Records.Add(rec);
        }
        return rec;
    }

    private static void Save()
    {
        if (_path == null) return;
        try
        {
            var json = JsonSerializer.Serialize(_store, new JsonSerializerOptions { WriteIndented = false });
            File.WriteAllText(_path, json);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "UsageTracker save failed (non-fatal)");
        }
    }

    private static UsageStore Load(string path)
    {
        if (!File.Exists(path)) return new UsageStore();
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<UsageStore>(json) ?? new UsageStore();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "UsageTracker load failed — starting fresh");
            return new UsageStore();
        }
    }
}

public sealed class UsageStore
{
    public List<UsageRecord> Records { get; set; } = new();
}

public sealed class UsageRecord
{
    public string Date { get; set; } = "";       // yyyy-MM-dd UTC
    public int WhisperCount { get; set; }
    public double WhisperSeconds { get; set; }
    public int RefineCount { get; set; }
    public int RefineInTokens { get; set; }
    public int RefineOutTokens { get; set; }
    public int TranslateCount { get; set; }
    public int TranslateInTokens { get; set; }
    public int TranslateOutTokens { get; set; }
}

public sealed class UsageSummary
{
    public int    TodayTranscriptions { get; set; }
    public double TodayAudioMin       { get; set; }
    public int    TodayRefines        { get; set; }
    public int    TodayTranslates     { get; set; }
    public double TodayCostUsd        { get; set; }

    public int    MonthTranscriptions { get; set; }
    public double MonthAudioMin       { get; set; }
    public int    MonthRefines        { get; set; }
    public int    MonthTranslates     { get; set; }
    public double MonthCostUsd        { get; set; }

    public int    LifetimeTranscriptions { get; set; }
    public double LifetimeAudioMin       { get; set; }
    public int    LifetimeRefines        { get; set; }
    public int    LifetimeTranslates     { get; set; }
    public double LifetimeCostUsd        { get; set; }
}
