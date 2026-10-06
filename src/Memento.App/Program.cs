using System.Windows;
using Memento.App.Bridge;
using Memento.App.Hosting;
using Memento.App.Theming;
using Memento.Core;
using Memento.Core.Bridge;
using Memento.Core.Host;
using Memento.Core.Settings;
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
                options.IsScreenshotRun ? " (screenshot run)" : string.Empty);

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
        builder.Services.AddSingleton<IFreeSpaceProbe, DriveFreeSpaceProbe>();
        builder.Services.AddSingleton<IExternalLauncher, ExternalLauncher>();
        builder.Services.AddSingleton<UiLifecycle>();
        builder.Services.AddSingleton<IUiLifecycle>(services => services.GetRequiredService<UiLifecycle>());
        builder.Services.AddSingleton<WebViewEventSink>();
        builder.Services.AddSingleton<IBridgeEventSink>(services => services.GetRequiredService<WebViewEventSink>());
        builder.Services.AddSingleton<WebViewBridge>();
        builder.Services.AddSingleton<WindowsThemeWatcher>();
        builder.Services.AddSingleton<ThemeService>();
        builder.Services.AddSingleton<IThemeState>(services => services.GetRequiredService<ThemeService>());
        builder.Services.AddMementoBridge();
        builder.Services.AddHostedService<FooterStatusLoop>();
        builder.Services.AddSingleton<MainWindow>();
        return builder.Build();
    }
}
