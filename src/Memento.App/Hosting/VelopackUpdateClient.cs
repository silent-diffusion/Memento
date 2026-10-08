using System.IO;
using System.Net.Http;
using System.Windows;
using Memento.Core.Updates;
using Microsoft.Extensions.Logging;
using Velopack;
using Velopack.Sources;

namespace Memento.App.Hosting;

/// <summary>
/// <see cref="IUpdateClient"/> over Velopack's <see cref="UpdateManager"/> and the GitHub releases of the project (the
/// <c>releases.win.json</c> feed <c>vpk upload github</c> publishes). Pre-releases count only for a pre-release build
/// (<see cref="UpdatePolicy"/>). The hidden <c>--update-feed=&lt;url|folder|off&gt;</c> switch points it at a local
/// feed for testing, or turns updates off. Installing waits for this process to exit normally, then restarts Memento.
/// </summary>
internal sealed partial class VelopackUpdateClient : IUpdateClient
{
    public const string RepositoryUrl = "https://github.com/silent-diffusion/Memento";
    public const string FeedOff = "off";

    private readonly UpdateManager? _manager;
    private readonly ILogger<VelopackUpdateClient> _logger;
    private UpdateInfo? _found;

    public VelopackUpdateClient(string? feed, string runningVersion, ILogger<VelopackUpdateClient> logger)
    {
        _logger = logger;
        CurrentVersion = runningVersion;
        if (string.Equals(feed, FeedOff, StringComparison.OrdinalIgnoreCase))
        {
            LogOff();
            return;
        }

        var prerelease = UpdatePolicy.AcceptsPreReleases(runningVersion);
        IUpdateSource source = feed switch
        {
            null => new GithubSource(RepositoryUrl, null, prerelease),
            _ when Uri.TryCreate(feed, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) => new SimpleWebSource(uri),
            _ => new SimpleFileSource(new DirectoryInfo(feed)),
        };
        try
        {
            _manager = new UpdateManager(source);
            CurrentVersion = _manager.CurrentVersion?.ToString() ?? runningVersion;
            LogSource(feed ?? RepositoryUrl, prerelease, _manager.IsInstalled);
        }
#pragma warning disable CA1031 // A copy Velopack cannot locate simply does not update itself.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogNoManager(ex);
            _manager = null;
        }
    }

    public string CurrentVersion { get; }

    public bool CanUpdate => _manager is { IsInstalled: true };

    public string? PendingVersion => _manager is { IsInstalled: true } manager ? manager.UpdatePendingRestart?.Version?.ToString() : null;

    public async Task<string?> CheckAsync(CancellationToken cancellationToken)
    {
        var manager = _manager ?? throw new UpdateCheckException("this copy cannot update itself");
        try
        {
            _found = await manager.CheckForUpdatesAsync().WaitAsync(TimeSpan.FromSeconds(60), cancellationToken);
            return _found?.TargetFullRelease.Version.ToString();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new UpdateCheckException(Describe(ex), ex);
        }
    }

    public async Task DownloadAsync(IProgress<int> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);
        var manager = _manager ?? throw new UpdateCheckException("this copy cannot update itself");
        var found = _found ?? throw new UpdateCheckException("no update was found to download");
        try
        {
            await manager.DownloadUpdatesAsync(found, progress.Report, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new UpdateCheckException(Describe(ex), ex);
        }
    }

    public void ApplyAndRestart()
    {
        var manager = _manager ?? throw new InvalidOperationException("This copy cannot update itself.");
        var pending = manager.UpdatePendingRestart ?? throw new InvalidOperationException("No update is downloaded.");

        // The updater waits for this process to end; closing the window the normal way stops every service cleanly
        // (processing is queued again, logs flushed) instead of the immediate exit of ApplyUpdatesAndRestart.
        manager.WaitExitThenApplyUpdates(pending, silent: true, restart: true);
        var version = pending.Version?.ToString() ?? "?";
        LogApplying(version);
        Application.Current?.Dispatcher.BeginInvoke(() => Application.Current.Shutdown());
    }

    private static string Describe(Exception exception) => exception switch
    {
        HttpRequestException { StatusCode: { } status } => $"the update server answered {(int)status} {status}",
        HttpRequestException => "the update server could not be reached",
        TimeoutException => "the update server did not answer within a minute",
        IOException io => $"the update could not be saved ({io.Message.TrimEnd('.')})",
        _ => exception.Message.TrimEnd('.'),
    };

    [LoggerMessage(Level = LogLevel.Information, Message = "Updates are off for this run (--update-feed=off)")]
    private partial void LogOff();

    [LoggerMessage(Level = LogLevel.Information, Message = "Updates from {Feed} (pre-releases: {PreRelease}; installed: {Installed})")]
    private partial void LogSource(string feed, bool preRelease, bool installed);

    [LoggerMessage(Level = LogLevel.Information, Message = "Velopack could not locate this installation; updates are off")]
    private partial void LogNoManager(Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Closing to install update {Version}")]
    private partial void LogApplying(string version);
}
