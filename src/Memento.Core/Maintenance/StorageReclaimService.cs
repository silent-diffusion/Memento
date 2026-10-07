using System.Globalization;
using System.Security.Cryptography;
using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Export;
using Memento.Core.Formatting;
using Memento.Core.Processing;
using Memento.Core.Projects;
using Memento.Core.Recording;
using Memento.Core.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Memento.Core.Maintenance;

/// <summary>
/// <c>storage.reclaim</c> (Settings › Storage and history › Reclaim space): runs the <c>optimize</c> stage with the
/// chosen codec, bitrate and downmix on the given recordings, or on every stored recording older than
/// <c>storage.reclaimOlderThanDays</c>. The stage keeps its guarantees: each smaller file is decoded and checked
/// before the lossless one goes, History records it, and transcripts are never touched. A recording that is busy
/// (recording, processing) is skipped and named in the final message.
/// </summary>
public sealed partial class StorageReclaimService(
    IServiceProvider services,
    ISettingsStore settings,
    IProjectStore store,
    ProcessingOrchestrator processing,
    RecordingCoordinator recordings,
    LibraryActivity activity,
    M3EventPublisher events,
    TimeProvider time,
    ILogger<StorageReclaimService> logger) : IAsyncDisposable, IDisposable
{
    private readonly CancellationTokenSource _closing = new();
    private readonly ILogger<StorageReclaimService> _logger = logger;
    private Task _running = Task.CompletedTask;
    private int _disposed;

    public bool IsBusy => !_running.IsCompleted;

    public async Task<string> StartAsync(StorageReclaimParams parameters, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        activity.ThrowIfMoving();
        var storage = Options(parameters);
        var ids = await SelectAsync(parameters.RecordingIds, cancellationToken);
        if (IsBusy)
        {
            throw new BridgeException(DomainErrorCodes.LibraryBusy, "Recordings are already being made smaller. Nothing new was started; wait for that to finish.");
        }

        var jobId = "r" + Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant();
        var busy = activity.Begin(LibraryActivity.Reclaim);
        _running = Task.Run(() => RunAsync(jobId, ids, storage, busy), CancellationToken.None);
        LogStarted(jobId, ids.Count, storage.Codec);
        return jobId;
    }

    public Task WhenIdleAsync() => _running;

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        await _closing.CancelAsync();
        await _running.WaitAsync(TimeSpan.FromSeconds(10)).ContinueWith(_ => { }, TaskScheduler.Default);
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    private static StorageSettings Options(StorageReclaimParams parameters)
    {
        if (parameters.Codec is not (StorageSettings.Aac or StorageSettings.Mp3))
        {
            throw M3Errors.Invalid($"Codec '{parameters.Codec}' can't be used to reclaim space. Choose aac or mp3.");
        }

        var bitrate = parameters.BitrateKbps ?? StorageSettings.DefaultLossyBitrateKbps;
        var min = parameters.Codec == StorageSettings.Mp3 ? ExportRules.MinMp3BitrateKbps : StorageSettings.MinBitrateKbps;
        if (bitrate < min || bitrate > StorageSettings.MaxBitrateKbps)
        {
            throw M3Errors.Invalid(string.Create(CultureInfo.InvariantCulture, $"{parameters.Codec.ToUpperInvariant()} needs a bitrate of {min} to {StorageSettings.MaxBitrateKbps} kbps, not {bitrate}."));
        }

        return new StorageSettings { Codec = parameters.Codec, BitrateKbps = bitrate, DownmixMono = parameters.DownmixMono };
    }

    private async Task<List<string>> SelectAsync(IReadOnlyList<string>? recordingIds, CancellationToken cancellationToken)
    {
        if (recordingIds is not null)
        {
            if (recordingIds.Count == 0)
            {
                throw NothingToReclaim("No recording was chosen, so there is nothing to make smaller. Nothing was changed. Choose at least one recording.");
            }

            var missing = recordingIds.FirstOrDefault(id => !store.Exists(id));
            return missing is null ? recordingIds.Distinct(StringComparer.Ordinal).ToList() : throw ProjectService.NotFound(missing);
        }

        if (settings.Current.Storage.ReclaimOlderThanDays is not { } days)
        {
            throw NothingToReclaim("No age is set for making recordings smaller, so none were chosen. Nothing was changed. Choose an age for \"Downmix tracks older than\" in Settings › Storage and history first.");
        }

        var cutoff = time.GetUtcNow().AddDays(-days);
        var selected = new List<string>();
        foreach (var id in store.ListIds())
        {
            try
            {
                var manifest = await store.LoadAsync(id, cancellationToken);
                if (manifest.CreatedAt < cutoff && manifest.State is ProjectStates.Ready or ProjectStates.Recovered)
                {
                    selected.Add(id);
                }
            }
            catch (ProjectNotFoundException)
            {
                // Unreadable: nothing to make smaller.
            }
        }

        return selected.Count > 0
            ? selected
            : throw NothingToReclaim(string.Create(CultureInfo.InvariantCulture, $"No recording is older than {days} {(days == 1 ? "day" : "days")}, so there is nothing to make smaller yet. Nothing was changed."));
    }

    private static BridgeException NothingToReclaim(string message) => new(DomainErrorCodes.StorageNothingToReclaim, message);

    private async Task RunAsync(string jobId, List<string> ids, StorageSettings storage, IDisposable busy)
    {
        using (busy)
        {
            var token = _closing.Token;
            var stage = ActivatorUtilities.CreateInstance<OptimizeStage>(services, new OverrideSettingsStore(settings, storage));
            var throttle = new ProgressThrottle(time);
            var done = 0;
            long freed = 0;
            var skipped = new List<string>();
            Publish(new StorageReclaimProgressPayload(jobId, 0, "running", null, 0, 0), throttle, force: true);
            try
            {
                foreach (var id in ids)
                {
                    token.ThrowIfCancellationRequested();
                    if (recordings.IsBusy(id) || processing.IsBusy(id) || !store.Exists(id))
                    {
                        skipped.Add(id);
                        continue;
                    }

                    var before = store.GetSizeBytes(id);
                    await stage.RunAsync(id, token);
                    var after = store.GetSizeBytes(id);
                    freed += Math.Max(0, before - after);
                    done++;
                    var percent = (int)(100L * (done + skipped.Count) / Math.Max(1, ids.Count));
                    Publish(new StorageReclaimProgressPayload(jobId, Math.Min(99, percent), "running", null, done, freed), throttle, force: false);
                }

                var message = string.Create(
                    CultureInfo.InvariantCulture,
                    $"{HumanFormat.Count(done, "recording", "recordings")} checked; {HumanFormat.Bytes(freed)} freed. Transcripts were not changed.");
                if (skipped.Count > 0)
                {
                    message += $" {HumanFormat.Count(skipped.Count, "recording was", "recordings were")} busy and left as they are.";
                }

                Publish(new StorageReclaimProgressPayload(jobId, 100, "done", message, done, freed), throttle, force: true);
                LogDone(jobId, done, freed);
            }
            catch (OperationCanceledException)
            {
                Publish(new StorageReclaimProgressPayload(jobId, 0, "failed", "Making recordings smaller stopped because Memento is closing. Every recording is complete; the rest can be done later.", done, freed), throttle, force: true);
            }
#pragma warning disable CA1031 // The optimize stage records its own failures; anything else ends the job with a message.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                LogFailed(ex, jobId);
                Publish(
                    new StorageReclaimProgressPayload(jobId, 0, "failed", $"Making recordings smaller stopped: {M3Errors.Reason(ex)}. Every recording keeps its lossless files unless its smaller copy was checked first.", done, freed),
                    throttle,
                    force: true);
            }
        }
    }

    private void Publish(StorageReclaimProgressPayload payload, ProgressThrottle throttle, bool force)
    {
        if (throttle.TryPass(force))
        {
            events.PublishReclaimProgress(payload);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Reclaim {JobId} started for {Count} recordings ({Codec})")]
    private partial void LogStarted(string jobId, int count, string codec);

    [LoggerMessage(Level = LogLevel.Information, Message = "Reclaim {JobId} done: {Done} recordings, {Freed} bytes freed")]
    private partial void LogDone(string jobId, int done, long freed);

    [LoggerMessage(Level = LogLevel.Error, Message = "Reclaim {JobId} failed")]
    private partial void LogFailed(Exception exception, string jobId);
}
