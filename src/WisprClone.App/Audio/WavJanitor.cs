using System;
using System.IO;
using System.Linq;
using Serilog;

namespace WisprClone.App.Audio;

/// <summary>
/// Startup chore that prunes orphan / stale recordings from the debug folder.
/// Normal flow deletes wavs right after a successful transcription, but a few
/// can pile up: crashes mid-flight, transcription failures, the user toggling
/// KeepWavFiles on then off, etc. The janitor catches them so the folder
/// never grows unboundedly.
/// </summary>
public static class WavJanitor
{
    private const int RetentionDays = 7;

    public static void PruneOlderThanRetention()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WisprClone", "debug");

        if (!Directory.Exists(dir)) return;

        var cutoff = DateTime.UtcNow.AddDays(-RetentionDays);
        int deleted = 0;
        long bytes = 0;

        try
        {
            foreach (var file in Directory.EnumerateFiles(dir, "rec_*.wav"))
            {
                try
                {
                    var info = new FileInfo(file);
                    if (info.LastWriteTimeUtc < cutoff)
                    {
                        bytes += info.Length;
                        File.Delete(file);
                        deleted++;
                    }
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "Could not prune {File}", file);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Janitor sweep of {Dir} failed", dir);
            return;
        }

        if (deleted > 0)
        {
            Log.Information("Janitor: pruned {Count} wav file(s) older than {Days}d ({Mb} MB)",
                deleted, RetentionDays, Math.Round(bytes / 1024.0 / 1024.0, 1));
        }
    }
}
