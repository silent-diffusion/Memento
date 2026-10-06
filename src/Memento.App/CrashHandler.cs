using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using Memento.App.Hosting;
using Memento.Core;
using Serilog;

namespace Memento.App;

/// <summary>
/// Last line of defence: any unhandled exception (UI thread, other threads, unobserved tasks)
/// writes a crash report to the logs folder and shuts Memento down with <see cref="ExitCode"/>.
/// The report carries the exception, version and OS only: no settings, paths of recordings or content.
/// </summary>
internal static class CrashHandler
{
    public const int ExitCode = 3;

    private static int _handled;

    /// <summary>Hooks the process-wide handlers. Call once, as early as possible.</summary>
    public static void InstallProcessHandlers()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Handle(e.ExceptionObject as Exception, "AppDomain.UnhandledException");
            // The runtime would terminate anyway; exit with our code instead of the Windows error dialog.
            Environment.Exit(ExitCode);
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            e.SetObserved();
            Handle(e.Exception, "TaskScheduler.UnobservedTaskException");
            ShutDown();
        };
    }

    /// <summary>Hooks the WPF dispatcher once the application object exists.</summary>
    public static void InstallDispatcherHandler(Application application)
    {
        ArgumentNullException.ThrowIfNull(application);
        application.DispatcherUnhandledException += (_, e) =>
        {
            e.Handled = true;
            Handle(e.Exception, "Application.DispatcherUnhandledException");
            ShutDown();
        };
    }

    private static void Handle(Exception? exception, string source)
    {
        if (Interlocked.Exchange(ref _handled, 1) == 1)
        {
            return;
        }

        try
        {
            Log.Fatal(exception, "Unhandled exception from {Source}; Memento is closing", source);
            var path = WriteReport(exception, source);
            Log.Information("Crash report written to {Path}", path);
        }
#pragma warning disable CA1031 // A failing crash report must never mask the original crash.
        catch (Exception reportFailure)
#pragma warning restore CA1031
        {
            Log.Error(reportFailure, "The crash report could not be written");
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    private static string WriteReport(Exception? exception, string source)
    {
        var now = DateTimeOffset.UtcNow;
        var report = new StringBuilder()
            .AppendLine("Memento crash report")
            .AppendLine(CultureInfo.InvariantCulture, $"Time (UTC): {now:yyyy-MM-ddTHH:mm:ss.fffZ}")
            .AppendLine(CultureInfo.InvariantCulture, $"Version: {AppInfo.ReadProductVersion()}")
            .AppendLine(CultureInfo.InvariantCulture, $"OS: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})")
            .AppendLine(CultureInfo.InvariantCulture, $"Runtime: {RuntimeInformation.FrameworkDescription} ({RuntimeInformation.ProcessArchitecture})")
            .AppendLine(CultureInfo.InvariantCulture, $"Source: {source}")
            .AppendLine()
            .AppendLine(exception?.ToString() ?? "No exception object was provided.")
            .ToString();

        Directory.CreateDirectory(AppPaths.Logs);
        var path = Path.Combine(AppPaths.Logs, $"crash-{now:yyyyMMdd-HHmmss-fff}.txt");
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, report, Encoding.UTF8);
        File.Move(temporary, path, overwrite: true);
        return path;
    }

    private static void ShutDown()
    {
        var application = Application.Current;
        if (application is null)
        {
            Environment.Exit(ExitCode);
            return;
        }

        application.Dispatcher.BeginInvoke(DispatcherPriority.Send, () => application.Shutdown(ExitCode));
    }
}
