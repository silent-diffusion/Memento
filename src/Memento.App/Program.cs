using System.IO;
using System.Windows;
using Memento.App.Bridge;
using Memento.Audio.Adapters;
using Memento.App.Hosting;
using Memento.App.Theming;
using Memento.Core;
using Memento.Core.Bridge;
using Memento.Core.Engines;
using Memento.Core.Host;
using Memento.Core.Recording;
using Memento.Core.Recording.Simulation;
using Memento.Core.Settings;
using Memento.Transcription;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Velopack;

namespace Memento.App;

internal static class Program
{
    private const int UsageErrorExitCode = 2;

    [STAThread]
    public static int Main(string[] args)
    {
        // Velopack install/update/uninstall hooks run here and exit the process when they apply.
        VelopackApp.Build().Run();

        AppLogging.Configure();
        CrashHandler.InstallProcessHandlers();
        try
        {
            CommandLineOptions options;
            try
            {
                options = CommandLineOptions.Parse(args);
            }
            catch (ArgumentException ex)
            {
                Log.Error("Invalid command line: {Problem}", ex.Message);
                return UsageErrorExitCode;
            }

            Log.Information(
                "Memento {Version} starting on {Os}{Mode}",
                AppInfo.ReadProductVersion(),
                Environment.OSVersion.VersionString,
                options.IsScreenshotRun ? " (screenshot run)" : options.SimulateAudio is not null ? " (simulated audio)" : string.Empty);

            if (AppContext.BaseDirectory.StartsWith(AppPaths.DataRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                // The installer replaces or deletes its own folder; user data must never live inside it (build/pack.ps1).
                Log.Error("Memento is installed inside its data folder {DataRoot}; an uninstall or update would delete user data", AppPaths.DataRoot);
            }

            // A screenshot run is a separate, short-lived process; it must not hand off to a running Memento.
            using var guard = options.IsScreenshotRun ? null : SingleInstanceGuard.Acquire();
            if (guard is { IsFirstInstance: false })
            {
                var signalled = SingleInstanceGuard.SignalFirstInstance();
                Log.Information("Memento is already running; {Outcome}", signalled ? "asked it to come forward" : "it is still starting");
                return 0;
            }

            return Run(options, guard);
        }
        finally
        {
            Log.Information("Memento exited");
            Log.CloseAndFlush();
        }
    }

    private static int Run(CommandLineOptions options, SingleInstanceGuard? guard)
    {
        using var host = BuildHost(options);

        // Settings and hosted services start before any WPF object exists, so there is no UI
        // synchronization context to deadlock on while waiting here.
        host.Services.GetRequiredService<ISettingsStore>().LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
        Log.Debug("Settings loaded");
        host.StartAsync().GetAwaiter().GetResult();
        Log.Debug("Host started");

        Log.Information("Recording engine: {Engine}", host.Services.GetRequiredService<IRecordingEngine>().Name);

        // Before any window: open the index and recover interrupted recordings, so the first screen is complete.
        host.Services.GetRequiredService<LibraryStartup>().RunAsync(CancellationToken.None).GetAwaiter().GetResult();
        var recordings = host.Services.GetRequiredService<RecordingCoordinator>();
        CrashHandler.BeforeExit = () => recordings.FlushForCrash(TimeSpan.FromSeconds(2));

        var application = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        CrashHandler.InstallDispatcherHandler(application);

        var window = host.Services.GetRequiredService<MainWindow>();
        guard?.ListenForActivation(() => window.Dispatcher.BeginInvoke(window.BringToFront));
        Log.Debug("Main window created");

        var exitCode = application.Run(window);

        // Stop on the pool: the WPF context is gone and must not receive continuations.
        Task.Run(() => host.StopAsync(TimeSpan.FromSeconds(5))).GetAwaiter().GetResult();
        return exitCode;
    }

    private static IHost BuildHost(CommandLineOptions options)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings
        {
            ApplicationName = "Memento",
            ContentRootPath = AppContext.BaseDirectory,
        });

        builder.Services.AddSerilog(dispose: false);
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<ISettingsStore>(services =>
            new JsonSettingsStore(AppPaths.SettingsFile, services.GetRequiredService<ILogger<JsonSettingsStore>>()));
        builder.Services.AddSingleton<IAppInfo, AppInfo>();
        if (options.FreeSpaceOverride is { } freeSpace)
        {
            Log.Warning("Free space is overridden for testing: {Override}", freeSpace);
            builder.Services.AddSingleton<IFreeSpaceProbe>(new OverrideFreeSpaceProbe(freeSpace, new DriveFreeSpaceProbe()));
        }
        else
        {
            builder.Services.AddSingleton<IFreeSpaceProbe, DriveFreeSpaceProbe>();
        }

        builder.Services.AddSingleton<IExternalLauncher, ExternalLauncher>();
        builder.Services.AddSingleton<UiLifecycle>();
        builder.Services.AddSingleton<IUiLifecycle>(services => services.GetRequiredService<UiLifecycle>());
        builder.Services.AddSingleton<WebViewEventSink>();
        builder.Services.AddSingleton<IBridgeEventSink>(services => services.GetRequiredService<WebViewEventSink>());
        builder.Services.AddSingleton<WebViewBridge>();
        builder.Services.AddSingleton<WindowsThemeWatcher>();
        builder.Services.AddSingleton<ThemeService>();
        builder.Services.AddSingleton<IThemeState>(services => services.GetRequiredService<ThemeService>());
        builder.Services.AddSingleton<IFolderPicker, WpfFolderPicker>();
        builder.Services.AddSingleton<IResourceProbe, WindowsResourceProbe>();
        if (options.ModelMirror is { } mirror)
        {
            Log.Warning("Models download from the test mirror {Mirror}", mirror);
            builder.Services.AddSingleton(Core.Models.ModelCatalog.Default.WithMirror(mirror));
        }

        builder.Services.AddMementoBridge();
        builder.Services.AddMementoLibrary();

        // Transcription and speakers run in Memento.Worker.exe from the "worker" folder beside the app.
        builder.Services.AddMementoTranscription();

        // The simulated engine stays registered for --simulate-audio; otherwise the WASAPI engine and sources replace it
        // (the last registration wins). Either way recordings are stored as verified FLAC through Media Foundation.
        builder.Services.AddSimulatedAudio(options.SimulateAudio ?? new SimulatedEngineOptions());
        if (options.SimulateAudio is null)
        {
            WasapiEngineOptions? engine = null;
            if (options.RolloverBytes is { } rollover)
            {
                Log.Warning("Capture tracks roll over at {Bytes} bytes for testing", rollover);
                engine = new WasapiEngineOptions { RolloverBytes = rollover };
            }

            builder.Services.AddWasapiAudio(engine);
        }

        builder.Services.AddMediaFoundationStorage();
        builder.Services.AddMementoM3Host();
        builder.Services.AddSingleton<LibraryStartup>();
        builder.Services.AddHostedService<RecordingLifetime>();
        builder.Services.AddHostedService<FooterStatusLoop>();

        // Self-update from the GitHub releases (a screenshot run never checks).
        builder.Services.AddSingleton<Core.Updates.IUpdateClient>(services => new VelopackUpdateClient(
            options.IsScreenshotRun ? VelopackUpdateClient.FeedOff : options.UpdateFeed,
            services.GetRequiredService<IAppInfo>().Version,
            services.GetRequiredService<ILogger<VelopackUpdateClient>>()));
        builder.Services.AddHostedService<UpdateLoop>();
        builder.Services.AddSingleton<MainWindow>();
        return builder.Build();
    }
}
