using System.Globalization;
using System.IO;
using Memento.Core;
using Serilog;
using Serilog.Events;

namespace Memento.App;

/// <summary>
/// Serilog set-up: rolling files in <c>%LOCALAPPDATA%\Memento\logs\memento-.log</c>, 10 MB per file, 7 days kept.
/// Logs may name files and engines; they must never contain transcript or document text, names or keys.
/// </summary>
internal static class AppLogging
{
    public const long FileSizeLimitBytes = 10L * 1024 * 1024;

    public static void Configure()
    {
        var configuration = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.File(
                Path.Combine(AppPaths.Logs, "memento-.log"),
                formatProvider: CultureInfo.InvariantCulture,
                rollingInterval: RollingInterval.Day,
                fileSizeLimitBytes: FileSizeLimitBytes,
                rollOnFileSizeLimit: true,
                retainedFileCountLimit: null,
                retainedFileTimeLimit: TimeSpan.FromDays(7),
                // A --screenshot run may write while the user's own instance is open.
                shared: true,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}");

#if DEBUG
        configuration = configuration
            .MinimumLevel.Debug()
            .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture, outputTemplate: "{Timestamp:HH:mm:ss.fff} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}");
#endif

        Log.Logger = configuration.CreateLogger();
    }
}
