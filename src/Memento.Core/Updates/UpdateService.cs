using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Host;
using Memento.Core.Processing;
using Memento.Core.Settings;
using Memento.Core.Status;
using Microsoft.Extensions.Logging;

namespace Memento.Core.Updates;

/// <summary>
/// Self-update (README "Updates"): checks the release feed once the interface is ready and then every 24 hours while
/// Memento runs, never while a recording or processing stage is active (it waits until both are idle), downloads in
/// the background with progress in the status footer, and then offers "Restart to update". Nothing is installed
/// until the person clicks it; a downloaded update that was not applied installs at the next launch. "Install updates
/// automatically" off means no automatic check at all; "Check now" still works. Failures of automatic checks are only
/// logged; a check by hand says what happened. Never a modal.
/// </summary>
public sealed partial class UpdateService : IDisposable
{
    public static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);

    public static readonly TimeSpan IdlePollInterval = TimeSpan.FromSeconds(30);

    private readonly IUpdateClient _client;
    private readonly ISettingsStore _settings;
    private readonly RecordingStatusBoard _recording;
    private readonly ProcessingOrchestrator? _processing;
    private readonly UpdateStatusBoard _footerBoard;
    private readonly FooterStatusService? _footer;
    private readonly IBridgeEventSink _sink;
    private readonly TimeProvider _time;
    private readonly ILogger<UpdateService> _logger;
    private readonly object _gate = new();
    private readonly CancellationTokenSource _stopping = new();
    private UpdateStatus _status;
    private Task _download = Task.CompletedTask;

    public UpdateService(
        IUpdateClient client,
        ISettingsStore settings,
        RecordingStatusBoard recording,
        UpdateStatusBoard footerBoard,
        IBridgeEventSink sink,
        TimeProvider time,
        ILogger<UpdateService> logger,
        ProcessingOrchestrator? processing = null,
        FooterStatusService? footer = null)
    {
        _client = client;
        _settings = settings;
        _recording = recording;
        _processing = processing;
        _footerBoard = footerBoard;
        _footer = footer;
        _sink = sink;
        _time = time;
        _logger = logger;
        _status = !client.CanUpdate
            ? new UpdateStatus(client.CurrentVersion, UpdateStates.Unavailable, null, null, null, null, false)
            : client.PendingVersion is { } pending
                ? new UpdateStatus(client.CurrentVersion, UpdateStates.Ready, pending, 100, null, null, false)
                : new UpdateStatus(client.CurrentVersion, UpdateStates.Idle, null, null, null, null, false);
    }

    /// <summary>Overridable for tests: how often a deferred check or download looks again for idle.</summary>
    public TimeSpan IdlePoll { get; set; } = IdlePollInterval;

    public UpdateStatus Status
    {
        get
        {
            lock (_gate)
            {
                return _status;
            }
        }
    }

    /// <summary>A recording, or a processing stage queued, waiting or running.</summary>
    public bool IsBusy => _recording.Recording.Active || (_processing?.IsProcessing ?? false);

    /// <summary>The download started by the latest check (tests).</summary>
    public Task Download
    {
        get
        {
            lock (_gate)
            {
                return _download;
            }
        }
    }

    /// <summary>
    /// The schedule: once <paramref name="uiReady"/> completes, then every <see cref="CheckInterval"/>, when "Install
    /// updates automatically" is on. Returns when <paramref name="cancellationToken"/> is cancelled.
    /// </summary>
    public async Task RunAsync(Task uiReady, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uiReady);
        if (!_client.CanUpdate)
        {
            LogUnavailable(_client.CurrentVersion);
            return;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stopping.Token);
        var token = linked.Token;
        try
        {
            await uiReady.WaitAsync(token);
            while (!token.IsCancellationRequested)
            {
                if (_settings.Current.General.AutoUpdate && Status.State is UpdateStates.Idle or UpdateStates.Failed)
                {
                    await WaitUntilIdleAsync(token);
                    await CheckCoreAsync(manual: false, token);
                }

                await Task.Delay(CheckInterval, _time, token);
            }
        }
        catch (OperationCanceledException)
        {
            // Memento is closing.
        }
    }

    /// <summary><c>updates.check</c>: checks now and, when a newer version exists, downloads it (once idle).</summary>
    /// <exception cref="BridgeException"><c>updates.unavailable</c> for a copy that cannot update itself.</exception>
    public async Task<UpdateStatus> CheckNowAsync(CancellationToken cancellationToken)
    {
        if (!_client.CanUpdate)
        {
            throw new BridgeException(
                DomainErrorCodes.UpdatesUnavailable,
                "This copy of Memento was not installed with Setup, so it cannot update itself. Install the latest release from GitHub to get updates; your recordings are not affected.");
        }

        if (Status.State is UpdateStates.Checking or UpdateStates.Downloading or UpdateStates.Ready)
        {
            return Status;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stopping.Token);
        await CheckCoreAsync(manual: true, linked.Token);
        return Status;
    }

    /// <summary><c>updates.apply</c>: closes Memento, installs the downloaded version and starts it again.</summary>
    /// <exception cref="BridgeException"><c>updates.notReady</c> or <c>updates.busy</c>.</exception>
    public void Apply()
    {
        var status = Status;
        if (status.State != UpdateStates.Ready)
        {
            throw new BridgeException(
                DomainErrorCodes.UpdatesNotReady,
                "No update has been downloaded yet, so there is nothing to install. Use Check now in Settings › General; Memento keeps working as it is.");
        }

        if (_recording.Recording.Active)
        {
            throw new BridgeException(
                DomainErrorCodes.UpdatesBusy,
                $"Memento is recording, so it was not restarted. Stop the recording first, then choose Restart to update {status.AvailableVersion}; the update also installs by itself the next time Memento starts.");
        }

        LogApplying(status.CurrentVersion, status.AvailableVersion ?? "?");
        _client.ApplyAndRestart();
    }

    public void Dispose()
    {
        _stopping.Cancel();
        _stopping.Dispose();
    }

    private async Task CheckCoreAsync(bool manual, CancellationToken cancellationToken)
    {
        Set(s => s with { State = UpdateStates.Checking, Message = null, Percent = null });
        string? available;
        try
        {
            available = await _client.CheckAsync(cancellationToken);
        }
        catch (UpdateCheckException ex)
        {
            LogCheckFailed(ex, manual);
            Set(s => s with
            {
                State = manual ? UpdateStates.Failed : UpdateStates.Idle,
                Message = manual
                    ? $"Memento could not check for updates: {ex.Message.TrimEnd('.')}. Nothing was downloaded and this version keeps working as it is. Check the internet connection, then choose Check now again."
                    : null,
            });
            return;
        }

        var now = _time.GetLocalNow();
        if (available is null)
        {
            LogNoUpdate(_client.CurrentVersion);
            Set(s => s with
            {
                State = UpdateStates.Idle,
                AvailableVersion = null,
                LastCheckedAt = now,
                Message = manual ? $"Memento {_client.CurrentVersion} is the newest version." : null,
            });
            return;
        }

        LogFound(available, _client.CurrentVersion);
        Set(s => s with { State = UpdateStates.Idle, AvailableVersion = available, LastCheckedAt = now });
        lock (_gate)
        {
            _download = Task.Run(() => DownloadAsync(available, manual, _stopping.Token), CancellationToken.None);
        }
    }

    private async Task DownloadAsync(string version, bool manual, CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                if (IsBusy)
                {
                    Set(s => s with
                    {
                        State = UpdateStates.Idle,
                        Deferred = true,
                        Percent = null,
                        Message = manual ? $"Memento {version} is available. It downloads once the recording and processing have finished." : null,
                    });
                    LogDeferred(version);
                    await WaitUntilIdleAsync(cancellationToken);
                }

                using var download = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                using var watch = WatchForBusy(download);
                Set(s => s with { State = UpdateStates.Downloading, Deferred = false, Percent = 0, Message = null });
                var progress = new DownloadProgress(this, version);
                try
                {
                    await _client.DownloadAsync(progress, download.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // A recording or processing started: stop and download again once idle.
                    _footerBoard.Set(UpdateFooterStatus.Idle);
                    continue;
                }

                _footerBoard.Set(UpdateFooterStatus.Idle);
                Set(s => s with { State = UpdateStates.Ready, Percent = 100, Deferred = false, Message = null });
                LogReady(version);
                return;
            }
        }
        catch (UpdateCheckException ex)
        {
            LogDownloadFailed(ex, version);
            _footerBoard.Set(UpdateFooterStatus.Idle);
            Set(s => s with
            {
                State = manual ? UpdateStates.Failed : UpdateStates.Idle,
                Percent = null,
                Message = manual
                    ? $"Memento {version} could not be downloaded: {ex.Message.TrimEnd('.')}. Nothing was changed and this version keeps working as it is. Choose Check now to try again."
                    : null,
            });
        }
        catch (OperationCanceledException)
        {
            // Memento is closing; the next run checks again.
            _footerBoard.Set(UpdateFooterStatus.Idle);
        }
    }

    /// <summary>Cancels <paramref name="download"/> as soon as a recording or processing starts.</summary>
    private ITimer WatchForBusy(CancellationTokenSource download) =>
        _time.CreateTimer(
            _ =>
            {
                if (IsBusy)
                {
                    try
                    {
                        download.Cancel();
                    }
                    catch (ObjectDisposedException)
                    {
                        // The download already finished.
                    }
                }
            },
            null,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(1));

    private async Task WaitUntilIdleAsync(CancellationToken cancellationToken)
    {
        while (IsBusy)
        {
            await Task.Delay(IdlePoll, _time, cancellationToken);
        }
    }

    private void Set(Func<UpdateStatus, UpdateStatus> change)
    {
        UpdateStatus next;
        lock (_gate)
        {
            next = change(_status);
            if (next == _status)
            {
                return;
            }

            _status = next;
        }

        _sink.Post(BridgeEventPublisher.Serialize(BridgeEventNames.UpdatesProgress, next, UpdatesBridgeJsonContext.Default.BridgeEventEnvelopeUpdateStatus));
    }

    private void ReportPercent(string version, int percent)
    {
        var clamped = Math.Clamp(percent, 0, 100);
        Set(s => s.State == UpdateStates.Downloading ? s with { Percent = clamped } : s);
        _footerBoard.Set(new UpdateFooterStatus(true, clamped, version));
        try
        {
            _footer?.Publish(force: false);
        }
#pragma warning disable CA1031 // The footer is best effort; the download goes on.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogFooterFailed(ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Updates: this copy ({Version}) cannot update itself; no checks")]
    private partial void LogUnavailable(string version);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Updates: the check failed (by hand: {Manual})")]
    private partial void LogCheckFailed(Exception exception, bool manual);

    [LoggerMessage(Level = LogLevel.Information, Message = "Updates: {Version} is the newest version")]
    private partial void LogNoUpdate(string version);

    [LoggerMessage(Level = LogLevel.Information, Message = "Updates: {Available} is available (running {Current})")]
    private partial void LogFound(string available, string current);

    [LoggerMessage(Level = LogLevel.Information, Message = "Updates: downloading {Version} waits for the recording and processing to finish")]
    private partial void LogDeferred(string version);

    [LoggerMessage(Level = LogLevel.Information, Message = "Updates: {Version} is downloaded and installs on restart")]
    private partial void LogReady(string version);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Updates: downloading {Version} failed")]
    private partial void LogDownloadFailed(Exception exception, string version);

    [LoggerMessage(Level = LogLevel.Information, Message = "Updates: restarting from {Current} to install {Available}")]
    private partial void LogApplying(string current, string available);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Updates: the footer could not show the download")]
    private partial void LogFooterFailed(Exception exception);

    private sealed class DownloadProgress(UpdateService owner, string version) : IProgress<int>
    {
        private int _last = -1;

        public void Report(int value)
        {
            if (Interlocked.Exchange(ref _last, value) != value)
            {
                owner.ReportPercent(version, value);
            }
        }
    }
}
