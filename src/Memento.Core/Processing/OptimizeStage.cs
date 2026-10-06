using System.Diagnostics;
using System.Globalization;
using Memento.Core.Audio;
using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Formatting;
using Memento.Core.Library;
using Memento.Core.Projects;
using Memento.Core.Recording;
using Memento.Core.Settings;
using Microsoft.Extensions.Logging;

namespace Memento.Core.Processing;

/// <summary>
/// The <c>optimize</c> stage (ARCHITECTURE.md §5, "Storage format options"): finalize always stores lossless FLAC;
/// when Settings › Recording asks for AAC or MP3, this stage runs after every other stage and writes the smaller
/// files, decodes each one to prove it plays, updates the manifest and its hashes, appends a History line, and only
/// then removes the FLAC files it replaced. Any failure keeps every FLAC file and removes the partial lossy ones.
/// "Keep only the mix" is stored but not applied in this version; separate tracks are always kept.
/// </summary>
public sealed partial class OptimizeStage(
    IProjectStore store,
    ProjectCatalog catalog,
    ISettingsStore settings,
    IEnumerable<IAudioEncoder> encoders,
    BridgeEventPublisher publisher,
    TimeProvider time,
    ILogger<OptimizeStage> logger,
    IAudioFileVerifier? verifier = null)
{
    /// <summary>Lossy files carry encoder priming and padding (+11 ms AAC, +37 ms MP3; ENGINE-NOTES.md §A).</summary>
    private const long DurationToleranceMs = 250;

    private static readonly TimeSpan[] DeleteRetryDelays =
        [TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(10)];

    private readonly IReadOnlyList<IAudioEncoder> _encoders = encoders.ToList();
    private readonly ILogger<OptimizeStage> _logger = logger;

    /// <summary>Whether a recording finished now would get this stage: the storage format is a lossy one.</summary>
    public static bool AppliesTo(StorageSettings storage)
    {
        ArgumentNullException.ThrowIfNull(storage);
        return storage.IsLossy;
    }

    /// <summary>The queued stage, as the processing card shows it.</summary>
    public static StageStatus Queued => new(StageNames.Optimize, StageStates.Queued, null, "Queued");

    /// <summary>
    /// Runs the stage for one recording with the storage settings as they are now. Never throws for a conversion
    /// problem (it is recorded as a failed stage); throws <see cref="OperationCanceledException"/> when cancelled,
    /// after removing partial files and putting the stage back in the queue.
    /// </summary>
    public async Task RunAsync(string recordingId, CancellationToken cancellationToken)
    {
        var storage = settings.Current.Recording.Storage;
        var manifest = await store.LoadAsync(recordingId, cancellationToken);
        if (!storage.IsLossy || manifest.Mix is null || manifest.State is not (ProjectStates.Ready or ProjectStates.Recovered))
        {
            // Settings went back to FLAC, or the recording is not stored (failed): nothing to make smaller.
            await RemoveStageAsync(recordingId, cancellationToken);
            return;
        }

        var encoder = _encoders.FirstOrDefault(e => !e.IsLossless && string.Equals(e.Codec, storage.Codec, StringComparison.Ordinal));
        if (encoder is null || verifier is null)
        {
            LogEncoderMissing(storage.Codec);
            await RemoveStageAsync(recordingId, cancellationToken);
            await AppendQuietlyAsync(
                recordingId,
                new HistoryEntry(
                    time.GetLocalNow(),
                    StageNames.Optimize,
                    "info",
                    "Kept as lossless FLAC",
                    $"This build has no {CodecName(storage.Codec)} encoder, so the recording stays as FLAC. Nothing was lost; the files are larger."),
                cancellationToken);
            return;
        }

        var folder = store.GetProjectFolder(recordingId);
        var bitrate = storage.BitrateKbps ?? StorageSettings.DefaultLossyBitrateKbps;
        var format = string.Create(CultureInfo.InvariantCulture, $"{CodecName(storage.Codec)} {bitrate} kbps")
            + (storage.DownmixMono ? ", tracks in mono" : string.Empty);
        var work = PlanWork(folder, manifest, encoder);
        if (work.Count == 0)
        {
            await RemoveStageAsync(recordingId, cancellationToken);
            return;
        }

        await SetStageAsync(recordingId, StageStates.Active, 0, "0% · making smaller", cancellationToken);
        await AppendQuietlyAsync(
            recordingId,
            new HistoryEntry(time.GetLocalNow(), StageNames.Optimize, "started", "Making smaller files", format),
            cancellationToken);

        var stopwatch = Stopwatch.StartNew();
        var written = new List<Converted>();
        try
        {
            foreach (var item in work)
            {
                cancellationToken.ThrowIfCancellationRequested();
                written.Add(await ConvertAsync(folder, item, encoder, bitrate, storage.DownmixMono, cancellationToken));
                var percent = (int)(100L * written.Count / work.Count);
                PublishProgress(manifest, StageStates.Active, percent, string.Create(CultureInfo.InvariantCulture, $"{percent}% · making smaller"));
            }
        }
        catch (OperationCanceledException)
        {
            RemovePartial(folder, written);
            await SetStageAsync(recordingId, StageStates.Queued, null, "Queued", CancellationToken.None);
            LogCancelled(recordingId);
            throw;
        }
#pragma warning disable CA1031 // Any encoder or decoder failure keeps the FLAC files and is reported as a failed stage.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            RemovePartial(folder, written);
            await RecordFailureAsync(recordingId, ex, cancellationToken);
            return;
        }

        var computedAt = time.GetLocalNow();
        var byOld = written.ToDictionary(c => c.Item.File, StringComparer.Ordinal);
        await catalog.UpdateAsync(
            recordingId,
            m =>
            {
                var files = new Dictionary<string, string>(m.Integrity.Files, StringComparer.Ordinal);
                foreach (var converted in written)
                {
                    files.Remove(converted.Item.File);
                    files[converted.File] = converted.Sha256;
                }

                return m with
                {
                    Tracks = m.Tracks.Select(t => byOld.TryGetValue(t.File, out var c)
                        ? t with { File = c.File, Codec = encoder.Codec, Channels = c.Channels, Sha256 = c.Sha256 }
                        : t).ToList(),
                    Mix = m.Mix is { } mix && byOld.TryGetValue(mix.File, out var mc)
                        ? mix with { File = mc.File, Codec = encoder.Codec, Channels = mc.Channels, Sha256 = mc.Sha256 }
                        : m.Mix,
                    Integrity = m.Integrity with { ComputedAt = computedAt, Files = files },
                    Stages = WithOptimize(m.Stages, new StageStatus(StageNames.Optimize, StageStates.Done, null, "Done")),
                };
            },
            CancellationToken.None);

        var before = written.Sum(c => c.Item.SizeBytes);
        var after = written.Sum(c => c.SizeBytes);
        await AppendQuietlyAsync(
            recordingId,
            new HistoryEntry(
                computedAt,
                StageNames.Optimize,
                "completed",
                $"Saved smaller files · {HumanFormat.Bytes(before)} → {HumanFormat.Bytes(after)}",
                string.Join(
                    " · ",
                    $"{format}: {string.Join(", ", written.Select(c => c.File))}",
                    "each file decoded and checked before the FLAC it replaces was removed",
                    "SHA-256 computed for every file",
                    string.Create(CultureInfo.InvariantCulture, $"took {stopwatch.Elapsed.TotalSeconds:0.0} s"))),
            CancellationToken.None);
        if (storage.KeepOnlyMix)
        {
            await AppendQuietlyAsync(
                recordingId,
                new HistoryEntry(
                    computedAt,
                    StageNames.Optimize,
                    "info",
                    "Kept the separate tracks",
                    "\"Keep only the mix\" is saved in Settings but not applied in this version: every source keeps its own track."),
                CancellationToken.None);
        }

        PublishProgress(manifest, StageStates.Done, null, "Done");
        await DeleteReplacedAsync(recordingId, folder, written);
        await catalog.TouchedAsync(recordingId, CancellationToken.None);
        LogOptimized(recordingId, written.Count, encoder.Codec, before, after, stopwatch.ElapsedMilliseconds);
    }

    /// <summary>Puts the stage in the manifest as queued (before the recording waits for its turn).</summary>
    public async Task MarkQueuedAsync(string recordingId, CancellationToken cancellationToken)
    {
        var saved = await catalog.UpdateAsync(recordingId, m => m with { Stages = WithOptimize(m.Stages, Queued) }, cancellationToken);
        publisher.PublishProcessingProgress(new ProcessingProgressPayload(recordingId, saved.Stages));
    }

    internal static IReadOnlyList<StageStatus> WithOptimize(IReadOnlyList<StageStatus> stages, StageStatus? optimize)
    {
        var list = stages.Where(s => s.Stage != StageNames.Optimize).ToList();
        if (optimize is not null)
        {
            list.Add(optimize); // Always last: it runs after every other stage.
        }

        return list;
    }

    private static string CodecName(string codec) => codec.ToUpperInvariant();

    private static List<WorkItem> PlanWork(string folder, ProjectManifest manifest, IAudioEncoder encoder)
    {
        var work = new List<WorkItem>();
        foreach (var track in manifest.Tracks)
        {
            // A track kept as WAV after a failed FLAC encode may span several RIFF parts; it stays as it is.
            var multiPartWav = track.Codec == PassThroughWavEncoder.WavCodec && CaptureParts.Find(folder, track.File).Count > 1;
            if (IsLossless(track.Codec) && track.Sha256 is not null && !multiPartWav)
            {
                work.Add(new WorkItem(track.Id, track.File, track.Channels, track.DurationMs, IsMix: false, 0));
            }
        }

        if (manifest.Mix is { } mix && IsLossless(mix.Codec))
        {
            work.Add(new WorkItem("mix", mix.File, mix.Channels, mix.DurationMs, IsMix: true, 0));
        }

        return work.Where(w => !w.File.EndsWith(encoder.FileExtension, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private static bool IsLossless(string codec) => codec is StorageSettings.Flac or PassThroughWavEncoder.WavCodec;

    private static string Full(string folder, string relative) =>
        Path.GetFullPath(Path.Combine(folder, relative.Replace('/', Path.DirectorySeparatorChar)));

    private static void ClearReadOnly(string path)
    {
        if (File.Exists(path))
        {
            File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);
        }
    }

    private async Task<Converted> ConvertAsync(string folder, WorkItem item, IAudioEncoder encoder, int bitrate, bool downmix, CancellationToken cancellationToken)
    {
        var source = Full(folder, item.File);
        var relative = Path.ChangeExtension(item.File, encoder.FileExtension).Replace('\\', '/');
        var destination = Full(folder, relative);

        // Left over from an interrupted earlier attempt: the manifest never pointed at it.
        ClearReadOnly(destination);
        File.Delete(destination);

        var mono = downmix && !item.IsMix && item.Channels > 1;
        var expectedChannels = mono ? 1 : item.Channels;
        try
        {
            await encoder.EncodeAsync(source, destination, new AudioEncodeOptions(bitrate, mono), cancellationToken);
            var check = await verifier!.VerifyAsync(destination, cancellationToken);
            if (check.Channels != expectedChannels || Math.Abs(check.DurationMs - item.DurationMs) > DurationToleranceMs)
            {
                throw new InvalidDataException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{Path.GetFileName(relative)} decodes as {check.Channels} channels and {HumanFormat.Clock(check.DurationMs)}, but {Path.GetFileName(item.File)} has {expectedChannels} and {HumanFormat.Clock(item.DurationMs)}"));
            }
        }
        catch
        {
            File.Delete(destination);
            throw;
        }

        var sha256 = await FileHashes.Sha256Async(destination, cancellationToken);
        File.SetAttributes(destination, File.GetAttributes(destination) | FileAttributes.ReadOnly);
        return new Converted(item with { SizeBytes = new FileInfo(source).Length }, relative, expectedChannels, new FileInfo(destination).Length, sha256);
    }

    private void RemovePartial(string folder, List<Converted> written)
    {
        foreach (var converted in written)
        {
            var path = Full(folder, converted.File);
            try
            {
                ClearReadOnly(path);
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                LogPartialNotRemoved(ex, converted.File);
            }
        }
    }

    /// <summary>
    /// The FLAC files go only after the manifest points at their verified replacements. Review may still be
    /// streaming the old mix, so a file that is in use is retried for a while; a leftover only costs space.
    /// </summary>
    private async Task DeleteReplacedAsync(string recordingId, string folder, List<Converted> written)
    {
        foreach (var converted in written)
        {
            var path = Full(folder, converted.Item.File);
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    ClearReadOnly(path);
                    File.Delete(path);
                    break;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    if (attempt >= DeleteRetryDelays.Length)
                    {
                        LogReplacedNotRemoved(ex, recordingId, converted.Item.File);
                        break;
                    }

                    await Task.Delay(DeleteRetryDelays[attempt], time);
                }
            }
        }
    }

    private async Task RecordFailureAsync(string recordingId, Exception exception, CancellationToken cancellationToken)
    {
        var diskFull = DiskErrors.IsDiskFull(exception);
        LogFailed(exception, recordingId, diskFull);
        await SetStageAsync(recordingId, StageStates.Failed, null, diskFull ? "Smaller files failed · drive full" : "Smaller files failed", cancellationToken);
        await AppendQuietlyAsync(
            recordingId,
            new HistoryEntry(
                time.GetLocalNow(),
                StageNames.Optimize,
                "failed",
                "Making smaller files failed",
                (diskFull ? "The drive is full. " : $"{exception.Message.TrimEnd('.')}. ")
                    + "The lossless FLAC files are kept and nothing was lost; any partly written smaller file was removed."),
            cancellationToken);
    }

    private async Task SetStageAsync(string recordingId, string state, int? percent, string label, CancellationToken cancellationToken)
    {
        var saved = await catalog.UpdateAsync(
            recordingId,
            m => m with { Stages = WithOptimize(m.Stages, new StageStatus(StageNames.Optimize, state, percent, label)) },
            cancellationToken);
        publisher.PublishProcessingProgress(new ProcessingProgressPayload(recordingId, saved.Stages));
    }

    private async Task RemoveStageAsync(string recordingId, CancellationToken cancellationToken)
    {
        var saved = await catalog.UpdateAsync(recordingId, m => m with { Stages = WithOptimize(m.Stages, null) }, cancellationToken);
        publisher.PublishProcessingProgress(new ProcessingProgressPayload(recordingId, saved.Stages));
    }

    private void PublishProgress(ProjectManifest manifest, string state, int? percent, string label) =>
        publisher.PublishProcessingProgress(new ProcessingProgressPayload(
            manifest.Id,
            WithOptimize(manifest.Stages, new StageStatus(StageNames.Optimize, state, percent, label))));

    private async Task AppendQuietlyAsync(string recordingId, HistoryEntry entry, CancellationToken cancellationToken)
    {
        try
        {
            await store.AppendHistoryAsync(recordingId, entry, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogHistoryFailed(ex, recordingId);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Recording {RecordingId} optimized: {Files} files to {Codec}, {Before} → {After} bytes in {ElapsedMs} ms")]
    private partial void LogOptimized(string recordingId, int files, string codec, long before, long after, long elapsedMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No encoder for storage codec {Codec} is installed; recordings stay lossless")]
    private partial void LogEncoderMissing(string codec);

    [LoggerMessage(Level = LogLevel.Error, Message = "Optimizing recording {RecordingId} failed (disk full: {DiskFull}); the FLAC files are kept")]
    private partial void LogFailed(Exception exception, string recordingId, bool diskFull);

    [LoggerMessage(Level = LogLevel.Information, Message = "Optimizing recording {RecordingId} was interrupted; it is queued again")]
    private partial void LogCancelled(string recordingId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A partly written file {File} could not be removed")]
    private partial void LogPartialNotRemoved(Exception exception, string file);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Recording {RecordingId}: replaced file {File} could not be removed; it only costs space")]
    private partial void LogReplacedNotRemoved(Exception exception, string recordingId, string file);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A history line for recording {RecordingId} could not be written")]
    private partial void LogHistoryFailed(Exception exception, string recordingId);

    private sealed record WorkItem(string Id, string File, int Channels, long DurationMs, bool IsMix, long SizeBytes);

    private sealed record Converted(WorkItem Item, string File, int Channels, long SizeBytes, string Sha256);
}
