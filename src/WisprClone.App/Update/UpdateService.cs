using System;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using Velopack;

namespace WisprClone.App.Update;

/// <summary>
/// Background update checker driven by Velopack. Auto-checks 30 s after launch
/// and every 4 h thereafter, downloads any new release in the background, and
/// queues it to apply on the next app launch (so we never interrupt the user
/// mid-dictation).
///
/// No-ops gracefully when:
///   * the feed URL is empty (auto-update disabled),
///   * the app wasn't installed via Velopack (e.g. running from `dotnet run`
///     or installed via Inno Setup) — Velopack's UpdateManager.IsInstalled
///     returns false in those cases.
/// </summary>
public sealed class UpdateService : IDisposable
{
    private readonly string _feedUrl;
    private readonly System.Threading.Timer _timer;

    /// <summary>Fires when a new version has been downloaded and is queued for apply.</summary>
    public event Action<string>? UpdateDownloaded;

    public UpdateService(string feedUrl)
    {
        _feedUrl = feedUrl ?? string.Empty;

        // Wait 30 s after startup to let the app settle, then every 4 hours.
        _timer = new System.Threading.Timer(
            _ => _ = CheckAsync(silent: true),
            null,
            TimeSpan.FromSeconds(30),
            TimeSpan.FromHours(4));
    }

    /// <summary>User-triggered check (e.g. from the tray menu). Logs verbosely.</summary>
    public Task<UpdateCheckResult> CheckNowAsync() => CheckAsync(silent: false);

    private async Task<UpdateCheckResult> CheckAsync(bool silent)
    {
        if (string.IsNullOrWhiteSpace(_feedUrl))
        {
            if (!silent) Log.Information("Auto-update is disabled (no updateFeedUrl in settings.json)");
            return UpdateCheckResult.Disabled;
        }

        try
        {
            var mgr = new UpdateManager(_feedUrl);

            if (!mgr.IsInstalled)
            {
                if (!silent)
                {
                    Log.Information("Auto-update skipped: this build wasn't installed via Velopack. " +
                                    "To enable: package via `vpk pack` and install that build.");
                }
                return UpdateCheckResult.NotInstalled;
            }

            var info = await mgr.CheckForUpdatesAsync().ConfigureAwait(false);
            if (info == null)
            {
                Log.Information("Update check: already on the latest version");
                return UpdateCheckResult.UpToDate;
            }

            var version = info.TargetFullRelease.Version.ToString();
            Log.Information("Update available: v{Version} — downloading in background", version);
            await mgr.DownloadUpdatesAsync(info).ConfigureAwait(false);
            Log.Information("Update v{Version} downloaded — will apply automatically on next launch", version);

            UpdateDownloaded?.Invoke(version);
            return UpdateCheckResult.DownloadedReady;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Update check failed");
            return UpdateCheckResult.Failed;
        }
    }

    public void Dispose() => _timer.Dispose();
}

public enum UpdateCheckResult
{
    Disabled,
    NotInstalled,
    UpToDate,
    DownloadedReady,
    Failed
}
