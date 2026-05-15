using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using Serilog;
using Velopack;
using WisprClone.App.Audio;
using WisprClone.App.Hotkey;
using WisprClone.App.Injection;
using WisprClone.App.Pipeline;
using WisprClone.App.Refinement;
using WisprClone.App.Settings;
using WisprClone.App.Transcription;
using WisprClone.App.Tray;
using WisprClone.App.Ui;
using WisprClone.App.Update;

namespace WisprClone.App;

internal static class Program
{
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(int dwProcessId);

    private const int ATTACH_PARENT_PROCESS = -1;

    // Lifetime objects we want to dispose on shutdown.
    private static LowLevelKeyboardHook? _hook;
    private static MicCapture? _mic;
    private static WhisperEngine? _whisper;
    private static DictationCoordinator? _coordinator;
    private static TrayManager? _tray;
    private static FloatingPill? _pill;
    private static UpdateService? _updates;
    private static RefinementService? _refiner;

    [STAThread]
    private static int Main()
    {
        // Velopack MUST run first — it intercepts install / uninstall /
        // first-run / update CLI args and exits without falling through to
        // app startup. For normal launches it returns immediately.
        VelopackApp.Build().Run();

        EnsureConsole();
        ConfigureLogging();

        Log.Information("WisprClone starting (cloud mode — OpenAI Whisper API)");

        var settings = AppSettings.LoadOrCreate(out var settingsPath);

        var app = new Application
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown
        };

        // Defer construction of the rest of the app until the WPF dispatcher
        // is running. That lets us pop the WelcomeWindow as a real WPF dialog
        // if we need an API key from the user.
        app.Startup += (_, _) =>
        {
            try
            {
                Initialize(app, settings, settingsPath);
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Initialization failed");
                app.Shutdown(1);
            }
        };

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            app.Dispatcher.Invoke(app.Shutdown);
        };

        int exitCode = 0;
        try
        {
            exitCode = app.Run();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Fatal error in message loop");
            exitCode = 1;
        }
        finally
        {
            _updates?.Dispose();
            _tray?.Dispose();
            _coordinator?.Dispose();
            _hook?.Dispose();
            _mic?.Dispose();
            _whisper?.Dispose();
            _refiner?.Dispose();
            Log.Information("WisprClone exited (code {Code})", exitCode);
            Log.CloseAndFlush();
        }

        return exitCode;
    }

    private static void Initialize(Application app, AppSettings settings, string settingsPath)
    {
        // Resolve API key — settings.json wins, env var is fallback, then
        // prompt via WelcomeWindow if neither is set.
        var apiKey = ResolveOrPromptApiKey(settings, settingsPath);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            Log.Information("No API key provided — exiting cleanly");
            app.Shutdown();
            return;
        }

        // Sweep stale wav debug files from previous runs.
        Audio.WavJanitor.PruneOlderThanRetention();

        _whisper = new WhisperEngine(apiKey);
        _refiner = new RefinementService(apiKey, settings.RefinementModel);

        var hotkeySpec = HotkeySpec.Parse(settings.Hotkey);
        Log.Information("Hotkey: {Hotkey}", hotkeySpec.Name);
        _hook = new LowLevelKeyboardHook(hotkeySpec);
        _mic = new MicCapture();
        _coordinator = new DictationCoordinator(
            _hook, _mic, _whisper,
            settings.HotkeyMode,
            settings.MaxRecordingSeconds,
            settings.KeepWavFiles,
            _refiner,
            settings.OfferRefinement);
        _coordinator.Start();

        _pill = new FloatingPill();
        var history = new HistoryStore();
        var injector = new ClipboardInjector();

        // Auto-update: silently checks the configured feed every 4h and
        // queues new versions for next launch.
        _updates = new UpdateService(settings.UpdateFeedUrl);

        // The Settings dialog needs live refs to whisper + coordinator so it
        // can apply changes without restart. We pass a factory closure into
        // the tray so it constructs a fresh dialog each time the user opens it.
        var settingsRef = settings;
        _tray = new TrayManager(
            history,
            injector,
            openSettings: () =>
            {
                try
                {
                    var win = new SettingsWindow(settingsRef, settingsPath, _whisper!, _refiner!, _coordinator!);
                    // No Owner — the app has no main window (tray-only), and
                    // assigning Application.Current.MainWindow can throw when
                    // it's null.
                    win.ShowDialog();
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to open Settings window");
                }
            },
            checkForUpdates: () =>
            {
                _ = HandleCheckForUpdatesAsync();
            },
            openAbout: () =>
            {
                try
                {
                    new AboutWindow().ShowDialog();
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to open About window");
                }
            });

        _coordinator.RecordingStarted     += () => _pill.SetState(PillState.Recording);
        _coordinator.RecordingStopped     += () => _pill.SetState(PillState.Transcribing);
        _coordinator.TranscriptionCompleted += text =>
        {
            _pill.SetState(PillState.Hidden);
            if (!string.IsNullOrWhiteSpace(text))
            {
                history.Add(text);
            }
        };

        Log.Information("UI ready. Hold Ctrl+Win to dictate. Right-click the tray icon for options. Quit from the tray.");
    }

    private static string ResolveOrPromptApiKey(AppSettings settings, string settingsPath)
    {
        if (IsRealKey(settings.OpenAiApiKey)) return settings.OpenAiApiKey;

        var envKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? "";
        if (IsRealKey(envKey))
        {
            Log.Information("Using OpenAI key from OPENAI_API_KEY environment variable");
            return envKey;
        }

        // No key available — show the first-run wizard.
        Log.Information("No API key configured — showing first-run wizard");
        var welcome = new WelcomeWindow();
        var ok = welcome.ShowDialog() == true && !string.IsNullOrWhiteSpace(welcome.EnteredKey);
        if (!ok || welcome.EnteredKey == null)
        {
            return "";
        }

        settings.OpenAiApiKey = welcome.EnteredKey;
        try
        {
            AppSettings.Save(settings, settingsPath);
            Log.Information("Saved new API key from wizard to {Path}", settingsPath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to persist API key — will use in memory only this session");
        }
        return welcome.EnteredKey;
    }

    private static bool IsRealKey(string? key) =>
        !string.IsNullOrWhiteSpace(key)
        && !key.Contains("REPLACE-WITH-YOUR-OPENAI-KEY", StringComparison.OrdinalIgnoreCase);

    private static async System.Threading.Tasks.Task HandleCheckForUpdatesAsync()
    {
        if (_updates == null) return;

        var result = await _updates.CheckNowAsync().ConfigureAwait(false);
        var msg = result switch
        {
            UpdateCheckResult.Disabled        => "Auto-update is disabled. Set updateFeedUrl in settings.json to a Velopack release feed (e.g. a GitHub Releases URL).",
            UpdateCheckResult.NotInstalled    => "This build wasn't installed via Velopack, so updates can't be applied automatically. Reinstall using a vpk-packaged installer to enable.",
            UpdateCheckResult.UpToDate        => "WisprClone is up to date.",
            UpdateCheckResult.DownloadedReady => "A new version was downloaded and will be applied the next time WisprClone starts.",
            UpdateCheckResult.Failed          => "Update check failed. See the log for details.",
            _ => "Update check completed."
        };

        // Tray-balloon-style feedback via WPF dispatcher.
        Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
        {
            MessageBox.Show(msg, "WisprClone updates", MessageBoxButton.OK, MessageBoxImage.Information);
        }));
    }

    /// <summary>
    /// If launched from a terminal, attach to that console so log lines are
    /// visible during development. If launched from the Start menu / Explorer
    /// / autostart, no console window pops up — logs go to file only.
    /// </summary>
    private static void EnsureConsole()
    {
        if (AttachConsole(ATTACH_PARENT_PROCESS))
        {
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
            Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
            Console.OutputEncoding = Encoding.UTF8;
        }
    }

    private static void ConfigureLogging()
    {
        var logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WisprClone", "logs");
        Directory.CreateDirectory(logDir);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console(outputTemplate:
                "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.File(
                Path.Combine(logDir, "wisprclone-.log"),
                rollingInterval: RollingInterval.Day,
                outputTemplate:
                    "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
    }
}
