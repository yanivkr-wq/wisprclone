using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using Velopack;
using Velopack.Sources;

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
    private int _inFlight;

    /// <summary>Fires when a new version has been downloaded and is queued for apply.</summary>
    public event Action<string>? UpdateDownloaded;

    public UpdateService(string feedUrl)
    {
        _feedUrl = feedUrl ?? string.Empty;

        // Clear any stale Velopack lock left behind by a previous crashed
        // process. The lock is a file at %LocalAppData%\WisprClone\packages\
        // .velopack_lock — if our process owns the binary now, no other
        // legitimate Velopack process is holding it.
        ClearStaleLock();

        // Wait 30 s after startup to let the app settle, then every 4 hours.
        _timer = new System.Threading.Timer(
            _ => _ = CheckGuarded(silent: true),
            null,
            TimeSpan.FromSeconds(30),
            TimeSpan.FromHours(4));
    }

    private static void ClearStaleLock()
    {
        try
        {
            var lockPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WisprClone", "packages", ".velopack_lock");
            if (File.Exists(lockPath))
            {
                File.Delete(lockPath);
                Log.Information("Cleared stale Velopack lock at startup: {Path}", lockPath);
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Couldn't clear stale Velopack lock (non-fatal)");
        }
    }

    /// <summary>User-triggered check (e.g. from the tray menu). Logs verbosely.</summary>
    public Task<UpdateCheckResult> CheckNowAsync() => CheckGuarded(silent: false);

    /// <summary>
    /// Wrapper around CheckAsync that enforces a single-flight semantic:
    /// only one update check can be in progress at a time. Without this,
    /// overlapping checks (e.g. user clicks Check for Updates twice while
    /// the first is still downloading) fight for Velopack's
    /// .velopack_lock file and the second crashes with
    /// AcquireLockFailedException.
    /// </summary>
    private async Task<UpdateCheckResult> CheckGuarded(bool silent)
    {
        if (Interlocked.CompareExchange(ref _inFlight, 1, 0) != 0)
        {
            if (!silent)
                Log.Information("Update check already in progress — ignoring duplicate request");
            else
                Log.Debug("Periodic update check skipped (another check is in flight)");
            return UpdateCheckResult.AlreadyRunning;
        }
        try
        {
            return await CheckAsync(silent).ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Exchange(ref _inFlight, 0);
        }
    }

    private async Task<UpdateCheckResult> CheckAsync(bool silent)
    {
        if (string.IsNullOrWhiteSpace(_feedUrl))
        {
            if (!silent) Log.Information("Auto-update is disabled (no updateFeedUrl in settings.json)");
            return UpdateCheckResult.Disabled;
        }

        try
        {
            // Velopack's plain-URL constructor treats the feed as a generic
            // SimpleWebSource and looks for /RELEASES at that path — which
            // 404s on github.com. For GitHub Releases we must use GithubSource
            // explicitly so Velopack queries the GH API and resolves the
            // *-Setup.exe / *.nupkg assets attached to the latest release.
            IUpdateSource source = _feedUrl.Contains("github.com", StringComparison.OrdinalIgnoreCase)
                ? new GithubSource(_feedUrl, accessToken: null, prerelease: false)
                : new SimpleWebSource(_feedUrl);

            var mgr = new UpdateManager(source);

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
    Failed,
    AlreadyRunning
}
