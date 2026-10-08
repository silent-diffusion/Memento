using System.Diagnostics;
using System.Globalization;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Engines;
using Memento.Core.Formatting;
using Memento.Core.Models;
using Memento.Core.Processing;
using Memento.Core.Projects;
using Memento.Core.Settings;
using Memento.Core.Transcripts;
using Memento.Core.Workers;
using Memento.Transcription.Windows;
using Microsoft.Extensions.Logging;

namespace Memento.Transcription.Stages;

/// <summary>
/// The <c>transcript</c> stage: every track is transcribed separately in <c>Memento.Worker.exe</c> (Whisper.net,
/// Vulkan then CPU, from the lossless files) and the segments are merged by time, keeping their track. Silent tracks
/// are skipped. Finished windows are saved to <c>transcript.partial.json</c> as they arrive, so a pause, a crash or
/// closing Memento loses at most one window; the next run continues from there. A worker that dies is reported with
/// the most specific remedy first and the transcript so far is kept. After the pass: coverage gaps, and highlights attached to their lines.
/// </summary>
public sealed partial class TranscriptStage(
    IProjectStore store,
    TranscriptWriter writer,
    StageStatusWriter status,
    IModelManager models,
    EngineSelector selector,
    WorkerClient workers,
    ISettingsStore settings,
    TimeProvider time,
    ILogger<TranscriptStage> logger) : IProcessingStage
{
    private const int ManifestPercentStep = 10;

    private readonly StageHistory _history = new(store, time, logger);
    private readonly ILogger<TranscriptStage> _logger = logger;

    public string Name => StageNames.Transcript;

    public int Order => 10;

    public bool IsHeavy => true;

    /// <summary>The device this pass would choose now: the GPU unless the request forces the processor or none fits.</summary>
    public async Task<bool> UsesGpuAsync(string recordingId, CancellationToken cancellationToken)
    {
        var manifest = await store.LoadAsync(recordingId, cancellationToken);
        var request = manifest.Processing ?? new ProcessingRequest();
        var modelId = request.ModelId ?? selector.EffectiveModelId(settings.Current.Transcription);
        return models.Resolve(modelId) is not null && selector.SelectDevice(modelId, request.ForceCpu).UseGpu;
    }

    public bool AppliesTo(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.Transcription.Auto;
    }

    public async Task RunAsync(StageRun run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        var recordingId = run.RecordingId;
        var manifest = await store.LoadAsync(recordingId, cancellationToken);
        if (manifest.State is not (ProjectStates.Ready or ProjectStates.Recovered))
        {
            await status.RemoveAsync(recordingId, Name, cancellationToken);
            return;
        }

        var current = settings.Current.Transcription;
        var request = manifest.Processing ?? new ProcessingRequest();
        var modelId = request.ModelId ?? selector.EffectiveModelId(current);
        var entry = models.Catalog.Find(modelId);

        // Checked against its SHA-256 before use (hashed only when its stamp does not vouch for it), so a damaged file
        // is named as damaged rather than as not installed.
        if (entry is not null && await models.VerifyAsync(modelId, cancellationToken) == ModelCheck.Damaged)
        {
            await FailDamagedModelAsync(recordingId, entry, cancellationToken);
            return;
        }

        var modelPath = models.Resolve(modelId);
        if (entry is null || modelPath is null)
        {
            await FailNoModelAsync(recordingId, entry?.Name ?? modelId, cancellationToken);
            return;
        }

        var folder = store.GetProjectFolder(recordingId);
        var tracks = manifest.Tracks
            .Select(t => (Track: t, Path: Path.Combine(folder, t.File.Replace('/', Path.DirectorySeparatorChar))))
            .Where(t => File.Exists(t.Path))
            .ToList();
        if (tracks.Count == 0)
        {
            await FailAsync(
                recordingId,
                "Transcription could not start: none of the recording's track files were found in its folder.",
                "Nothing was changed. If the files were moved, put them back in the recording's tracks folder.",
                [new Remedy(Remedies.Retry, "Try again")],
                ProjectStageFailure.CauseEngine,
                "Transcript failed",
                cancellationToken);
            return;
        }

        var device = selector.SelectDevice(modelId, request.ForceCpu);
        var language = request.Language ?? current.Language;
        var keepWords = current.KeepWordTimestamps;
        // Windows already finished with the same model and language are kept, whichever device made them, so a pass
        // that crashed on the graphics card continues on the processor where it stopped.
        var signature = string.Join('|', modelId, language, keepWords ? "words" : "nowords");
        var partial = await writer.Store.LoadPartialAsync(recordingId, cancellationToken);
        var resuming = partial is not null && partial.Signature == signature && (partial.Segments.Count > 0 || partial.Tracks.Any(t => t.WindowsDone > 0));
        if (!resuming)
        {
            partial = TranscriptPartial.Empty(signature);
        }

        var state = new PassState(partial!);
        var job = new TranscribeJob(
            tracks.Select(t => new WorkerTrack(t.Track.Id, t.Path, t.Track.StartOffsetMs / 1000.0, state.WindowsDone(t.Track.Id))).ToList(),
            modelPath,
            modelId,
            device.UseGpu ? [TranscriptionDefaults.RuntimeVulkan, TranscriptionDefaults.RuntimeCpu] : [TranscriptionDefaults.RuntimeCpu],
            -1,
            device.Gpu?.Name,
            language,
            TranscriptionDefaults.Prompt,
            TranscriptionDefaults.CpuThreads,
            keepWords,
            WindowPlanner.DefaultWindowSeconds,
            WindowPlanner.DefaultOverlapSeconds);

        var progress = new StageStatus(Name, StageStates.Active, 0, "0% · " + device.ProgressWord);
        manifest = await status.SetAsync(recordingId, progress, cancellationToken);
        await _history.AppendAsync(
            recordingId,
            Name,
            "started",
            resuming ? "Transcribing (continuing where it stopped)" : "Transcribing",
            string.Join(" · ", EngineSelector.WhisperEngineName, entry.Name, device.UseGpu ? $"GPU ({device.Gpu!.Name})" : "CPU", HumanFormat.Count(tracks.Count, "track", "tracks"))
                + (device.Reason is { } why && !request.ForceCpu ? $" · on the processor because {why}" : string.Empty));
        LogStarting(recordingId, modelId, device.Kind, resuming);

        var stopwatch = Stopwatch.StartNew();
        WorkerReply result;
        var lastManifestPercent = 0;
        using var lease = models.Use(modelId);
        try
        {
            result = await workers.RunAsync(
                new WorkerJob(WorkerJobKinds.Transcribe, Transcribe: job),
                async reply =>
                {
                    if (state.Apply(reply))
                    {
                        await writer.Store.SavePartialAsync(recordingId, state.ToPartial(stopwatch.ElapsedMilliseconds), CancellationToken.None);
                    }

                    // Within a window the engine reports its own percentage (no segments yet); it is shown too.
                    if (reply.Type == WorkerMessageTypes.Progress && reply.Percent is { } percent)
                    {
                        var whole = (int)Math.Clamp(Math.Floor(percent), 0, 99);
                        var word = state.Device?.Runtime == TranscriptionDefaults.RuntimeCpu ? "CPU" : device.ProgressWord;
                        var label = string.Create(CultureInfo.InvariantCulture, $"{whole}% · {word}");
                        if (whole - lastManifestPercent >= ManifestPercentStep)
                        {
                            lastManifestPercent = whole;
                            manifest = await status.SetAsync(recordingId, progress with { Percent = whole, Label = label }, CancellationToken.None);
                        }
                        else
                        {
                            status.PublishProgress(manifest, progress with { Percent = whole, Label = label });
                        }
                    }
                },
                cancellationToken);
        }
        catch (OperationCanceledException) when (run.StopReason == StageStopReason.Cancelled)
        {
            await KeepPartialAsync(recordingId, state, stopwatch.ElapsedMilliseconds, entry, current);
            await FailAsync(
                recordingId,
                "Transcription was cancelled" + (state.Segments.Count > 0 ? string.Create(CultureInfo.InvariantCulture, $" at {HumanFormat.Clock((long)(state.LastEnd * 1000))}.") : "."),
                Kept(state),
                [new Remedy(Remedies.Retry, "Continue transcribing")],
                ProjectStageFailure.CauseCancelled,
                "Cancelled",
                CancellationToken.None);
            return;
        }
        catch (WorkerCrashedException crash)
        {
            await KeepPartialAsync(recordingId, state, stopwatch.ElapsedMilliseconds, entry, current);
            await FailCrashedAsync(recordingId, crash, state, device, modelId, current.CpuFallbackModelId);
            return;
        }
        catch (WorkerJobException error)
        {
            await KeepPartialAsync(recordingId, state, stopwatch.ElapsedMilliseconds, entry, current);
            await FailJobAsync(recordingId, error, state, device, modelId, current.CpuFallbackModelId, entry.Name);
            return;
        }
        catch (WorkerUnavailableException missing)
        {
            LogWorkerMissing(missing, recordingId);
            await FailAsync(
                recordingId,
                "Transcription could not start: the transcription worker is missing from this installation or Windows blocked it.",
                "The recording is safe. Reinstalling Memento restores the worker.",
                [new Remedy(Remedies.Retry, "Try again")],
                ProjectStageFailure.CauseNoWorker,
                "Transcript failed",
                CancellationToken.None);
            return;
        }

        await CompleteAsync(recordingId, manifest, state, result.Transcription!, entry, request, current, stopwatch.ElapsedMilliseconds, tracks.Select(t => t.Track).ToList());
    }

    private static string Kept(PassState state) =>
        state.Segments.Count == 0
            ? "The recording is safe; no part of the transcript had finished yet."
            : $"The recording is safe, and the transcript up to {HumanFormat.Clock((long)(state.LastEnd * 1000))} is kept. Continuing starts from there.";

    private async Task CompleteAsync(
        string recordingId,
        ProjectManifest manifest,
        PassState state,
        TranscribeResult result,
        ModelCatalogEntry entry,
        ProcessingRequest request,
        TranscriptionSettings current,
        long elapsedMs,
        IReadOnlyList<ProjectTrack> tracks)
    {
        // A resumed pass with nothing left reports no device; the windows were made where the partial says.
        if (result.Device.Runtime == "none" && state.Device is { } earlier)
        {
            result = result with { Device = earlier };
        }

        var repeats = RepeatFilter.Apply(state.Segments);
        var segments = TranscriptMerger.Merge(repeats.Segments);
        var speech = state.Tracks.Values.ToDictionary(
            t => t.TrackId,
            t => (IReadOnlyList<(double Start, double End)>)t.Speech.Select(r => (r[0], r[1])).ToList(),
            StringComparer.Ordinal);
        var gaps = CoverageCheck.Find(speech, segments);
        var totalMs = state.ElapsedBeforeMs + elapsedMs;
        var document = new TranscriptDocument
        {
            Language = result.Language,
            LanguageDetected = result.LanguageDetected,
            Engine = new TranscriptEngineInfo(EngineSelector.WhisperEngineName, entry.Id, result.Device.Device, result.Device.EngineVersion, totalMs),
            Speakers = [],
            Segments = segments,
            Reviewed = false,
            LowConfidenceThreshold = current.LowConfidenceThreshold,
            CoverageGaps = gaps,
            Complete = true,
        };
        var reason = request.Retranscribe ? TranscriptChangeReasons.Retranscribed : TranscriptChangeReasons.Transcribed;
        var saved = await writer.UpdateAsync(recordingId, reason, _ => document, CancellationToken.None);
        writer.Store.DeletePartial(recordingId);
        await store.UpdateAsync(recordingId, m => m with { Processing = null }, CancellationToken.None);
        await status.SetAsync(recordingId, new StageStatus(Name, StageStates.Done, null, "Done"), CancellationToken.None);

        var words = segments.Sum(s => s.Words.Count);
        var low = segments.Sum(s => s.Words.Count(w => w.C < current.LowConfidenceThreshold));
        var audioSeconds = result.AudioSeconds;
        var detail = string.Join(
            " · ",
            EngineSelector.WhisperEngineName + " " + result.Device.EngineVersion,
            entry.Name,
            result.Device.GpuName is { } gpu ? $"{result.Device.Device}, {gpu}" : result.Device.Device,
            string.Create(CultureInfo.InvariantCulture, $"{HumanFormat.Clock((long)(audioSeconds * 1000))} of speech tracks in {totalMs / 1000.0:0.0} s"),
            HumanFormat.Count(segments.Count, "segment", "segments"),
            HumanFormat.Count(words, "word", "words"),
            string.Create(CultureInfo.InvariantCulture, $"{low} below {current.LowConfidenceThreshold:0.##} confidence"),
            result.LanguageDetected ? $"language detected: {result.Language}" : $"language: {result.Language}");
        await _history.AppendAsync(recordingId, Name, "completed", $"Transcribed · {HumanFormat.Count(segments.Count, "line", "lines")}", detail);
        foreach (var skipped in state.Tracks.Values.Where(t => t.Silent))
        {
            var name = tracks.FirstOrDefault(t => t.Id == skipped.TrackId)?.Name ?? skipped.TrackId;
            await _history.AppendAsync(recordingId, Name, "info", $"Skipped {name}", "The track was silent the whole time, so there was nothing to transcribe.");
        }

        if (repeats.Runs.Count > 0)
        {
            await _history.AppendAsync(recordingId, Name, "info", $"Dropped {HumanFormat.Count(repeats.DroppedCount, "repeated line", "repeated lines")}", RepeatsDetail(repeats, tracks));
            LogRepeatsDropped(recordingId, repeats.DroppedCount, repeats.Runs.Count);
        }

        foreach (var gap in gaps)
        {
            var name = tracks.FirstOrDefault(t => t.Id == gap.Track)?.Name ?? gap.Track;
            await _history.AppendAsync(
                recordingId,
                Name,
                "info",
                $"Speech without a transcript at {HumanFormat.Clock((long)(gap.Start * 1000))}–{HumanFormat.Clock((long)(gap.End * 1000))}",
                $"{name} has speech there but the engine wrote nothing; listen to that part, or transcribe again with another model.");
        }

        LogCompleted(recordingId, segments.Count, words, totalMs);
        await AttachHighlightsAsync(recordingId, saved ?? document);
    }

    /// <summary>"“Thank you.” 12 times in a row at 4:10–4:52 on Microphone; …": what the repeat filter removed.</summary>
    private static string RepeatsDetail(RepeatFilter.Result repeats, IReadOnlyList<ProjectTrack> tracks)
    {
        const int Listed = 5;
        var runs = repeats.Runs.Take(Listed).Select(r =>
        {
            var name = tracks.FirstOrDefault(t => t.Id == r.Kept.Track)?.Name ?? r.Kept.Track ?? "a track";
            var text = r.Kept.Text.Length > 80 ? r.Kept.Text[..80] + "…" : r.Kept.Text;
            return string.Create(CultureInfo.InvariantCulture, $"“{text}” {r.Dropped + 1} times in a row at {HumanFormat.Clock((long)(r.Kept.Start * 1000))}–{HumanFormat.Clock((long)(r.End * 1000))} on {name}");
        });
        var more = repeats.Runs.Count > Listed ? string.Create(CultureInfo.InvariantCulture, $"; and {repeats.Runs.Count - Listed} more") : string.Empty;
        return string.Join("; ", runs) + more + ". The engine sometimes repeats one line when it loses its place; the first one is kept. Listen to that part to check nothing was said there.";
    }

    /// <summary>Once there is a transcript, every highlight points at the line at its time (M2 clarification 5).</summary>
    private async Task AttachHighlightsAsync(string recordingId, TranscriptDocument transcript)
    {
        try
        {
            await HighlightSegments.AttachAsync(store, recordingId, transcript.Segments, CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ProjectSchemaException)
        {
            LogHighlightsFailed(ex, recordingId);
        }
    }

    /// <summary>A pass that stopped early leaves what it finished as the transcript, unless a complete one exists.</summary>
    private async Task KeepPartialAsync(string recordingId, PassState state, long elapsedMs, ModelCatalogEntry entry, TranscriptionSettings current)
    {
        await writer.Store.SavePartialAsync(recordingId, state.ToPartial(elapsedMs), CancellationToken.None);
        if (state.Segments.Count == 0)
        {
            return;
        }

        var existing = await writer.Store.LoadAsync(recordingId, CancellationToken.None);
        if (existing is { Complete: true })
        {
            return;
        }

        var document = new TranscriptDocument
        {
            Language = state.Language ?? "en",
            Engine = new TranscriptEngineInfo(EngineSelector.WhisperEngineName, entry.Id, state.Device?.Device ?? string.Empty, state.Device?.EngineVersion ?? string.Empty, state.ElapsedBeforeMs + elapsedMs),
            Segments = TranscriptMerger.Merge(RepeatFilter.Apply(state.Segments).Segments),
            LowConfidenceThreshold = current.LowConfidenceThreshold,
            Complete = false,
        };
        var saved = await writer.UpdateAsync(recordingId, TranscriptChangeReasons.Transcribed, _ => document, CancellationToken.None);
        await AttachHighlightsAsync(recordingId, saved ?? document);
    }

    private Task FailNoModelAsync(string recordingId, string modelName, CancellationToken cancellationToken)
    {
        var installed = models.Catalog.OfKind(ModelKinds.Transcription).FirstOrDefault(e => models.IsInstalled(e.Id));
        var remedies = new List<Remedy>();
        if (installed is not null)
        {
            remedies.Add(new Remedy(Remedies.Model(installed.Id), $"Use the {installed.Name} model (installed)"));
        }

        remedies.Add(new Remedy(Remedies.Retry, "Try again"));
        return FailAsync(
            recordingId,
            $"Transcription needs the {modelName} model, and it is not installed.",
            "The recording is safe. Install the model in Settings › Transcription and transcription starts by itself.",
            remedies,
            ProjectStageFailure.CauseNoModel,
            "Waiting for a model",
            cancellationToken);
    }

    private Task FailDamagedModelAsync(string recordingId, ModelCatalogEntry entry, CancellationToken cancellationToken)
    {
        var remedies = new List<Remedy> { new(Remedies.Install(entry.Id), $"Download {entry.Name} again") };
        var installed = models.Catalog.OfKind(ModelKinds.Transcription).FirstOrDefault(e => e.Id != entry.Id && models.IsInstalled(e.Id));
        if (installed is not null)
        {
            remedies.Add(new Remedy(Remedies.Model(installed.Id), $"Use the {installed.Name} model (installed)"));
        }

        LogModelDamaged(recordingId, entry.Id);
        return FailAsync(
            recordingId,
            $"Transcription could not start: the installed {entry.Name} model file is damaged (its SHA-256 checksum does not match the published one), so Memento set it aside instead of using it.",
            "The recording is safe. Download the model again and transcription starts by itself.",
            remedies,
            ProjectStageFailure.CauseNoModel,
            "Waiting for a model",
            cancellationToken);
    }

    private Task FailCrashedAsync(string recordingId, WorkerCrashedException crash, PassState state, EngineDevice device, string modelId, string fallbackModelId)
    {
        var at = state.Segments.Count > 0 ? $" at {HumanFormat.Clock((long)(state.LastEnd * 1000))}" : string.Empty;
        var onGpu = state.Device?.Runtime != TranscriptionDefaults.RuntimeCpu && device.UseGpu;
        var cause = crash.LooksLikeOutOfMemory
            ? "it ran out of memory"
            : onGpu ? "the engine stopped while using the graphics card" : "the engine stopped unexpectedly";
        var message = string.Create(CultureInfo.InvariantCulture, $"Transcription stopped{at}: {cause} (code 0x{crash.ExitCode:X8}).");
        var remedies = new List<Remedy>();
        var fallback = models.Catalog.Find(fallbackModelId);
        var offerSmaller = fallback is not null && fallbackModelId != modelId;
        if (crash.LooksLikeOutOfMemory && offerSmaller)
        {
            remedies.Add(new Remedy(Remedies.Model(fallbackModelId), $"Use the {fallback!.Name} model"));
        }

        if (onGpu)
        {
            remedies.Add(new Remedy(Remedies.Cpu, "Retry on CPU"));
        }

        if (offerSmaller && !crash.LooksLikeOutOfMemory)
        {
            remedies.Add(new Remedy(Remedies.Model(fallbackModelId), $"Use the {fallback!.Name} model"));
        }

        remedies.Add(new Remedy(Remedies.Retry, "Try again"));
        LogCrashed(recordingId, crash.ExitCode, state.Segments.Count);
        return FailAsync(recordingId, message, Kept(state), remedies, ProjectStageFailure.CauseCrashed, "Transcript failed", CancellationToken.None);
    }

    private Task FailJobAsync(string recordingId, WorkerJobException error, PassState state, EngineDevice device, string modelId, string fallbackModelId, string modelName)
    {
        var fallback = models.Catalog.Find(fallbackModelId);
        var remedies = new List<Remedy>();
        var message = error.Code switch
        {
            WorkerErrorCodes.ModelLoad => $"Transcription could not start: the {modelName} model could not be loaded ({error.Message.TrimEnd('.')}).",
            WorkerErrorCodes.OutOfMemory => $"Transcription stopped: there was not enough memory for the {modelName} model.",
            WorkerErrorCodes.Audio => $"Transcription stopped: {error.Message.TrimEnd('.')}.",
            _ => $"Transcription stopped: {error.Message.TrimEnd('.')}.",
        };
        if (fallback is not null && fallbackModelId != modelId && error.Code is WorkerErrorCodes.ModelLoad or WorkerErrorCodes.OutOfMemory)
        {
            remedies.Add(new Remedy(Remedies.Model(fallbackModelId), $"Use the {fallback.Name} model"));
        }

        if (device.UseGpu && error.Code != WorkerErrorCodes.Audio)
        {
            remedies.Add(new Remedy(Remedies.Cpu, "Retry on CPU"));
        }

        remedies.Add(new Remedy(Remedies.Retry, "Try again"));
        LogJobFailed(recordingId, error.Code);
        return FailAsync(recordingId, message, Kept(state), remedies, ProjectStageFailure.CauseEngine, "Transcript failed", CancellationToken.None);
    }

    private async Task FailAsync(string recordingId, string message, string kept, IReadOnlyList<Remedy> remedies, string cause, string label, CancellationToken cancellationToken)
    {
        var failure = new ProjectStageFailure(Name, message, kept, remedies, cause, time.GetLocalNow());
        await status.FailAsync(recordingId, failure, label, cancellationToken);

        // Speakers and topics wait for a complete transcript; they are queued again when the transcript is retried.
        var manifest = await store.LoadAsync(recordingId, CancellationToken.None);
        foreach (var dependent in new[] { StageNames.Speakers, StageNames.Topics })
        {
            if (StageList.Find(manifest.Stages, dependent) is { State: StageStates.Queued })
            {
                await status.RemoveAsync(recordingId, dependent, CancellationToken.None);
            }
        }

        await _history.AppendAsync(recordingId, Name, "failed", cause == ProjectStageFailure.CauseNoModel ? "Waiting for a transcription model" : "Transcription failed", message + " " + kept);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Recording {RecordingId}: transcribing with {ModelId} on {Device} (resuming: {Resuming})")]
    private partial void LogStarting(string recordingId, string modelId, string device, bool resuming);

    [LoggerMessage(Level = LogLevel.Information, Message = "Recording {RecordingId}: transcript done, {Segments} segments, {Words} words in {ElapsedMs} ms")]
    private partial void LogCompleted(string recordingId, int segments, int words, long elapsedMs);

    [LoggerMessage(Level = LogLevel.Information, Message = "Recording {RecordingId}: dropped {Dropped} repeated segments in {Runs} runs")]
    private partial void LogRepeatsDropped(string recordingId, int dropped, int runs);

    [LoggerMessage(Level = LogLevel.Error, Message = "Recording {RecordingId}: the transcription worker exited with code {ExitCode}; {Segments} segments kept")]
    private partial void LogCrashed(string recordingId, int exitCode, int segments);

    [LoggerMessage(Level = LogLevel.Error, Message = "Recording {RecordingId}: transcription model {ModelId} failed its checksum and was set aside")]
    private partial void LogModelDamaged(string recordingId, string modelId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Recording {RecordingId}: the transcription worker reported {Code}")]
    private partial void LogJobFailed(string recordingId, string code);

    [LoggerMessage(Level = LogLevel.Error, Message = "Recording {RecordingId}: the transcription worker is unavailable")]
    private partial void LogWorkerMissing(Exception exception, string recordingId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Recording {RecordingId}: highlights could not be attached to transcript lines")]
    private partial void LogHighlightsFailed(Exception exception, string recordingId);

    /// <summary>What the pass has finished so far, kept in step with <c>transcript.partial.json</c>.</summary>
    private sealed class PassState
    {
        private readonly string _signature;

        public PassState(TranscriptPartial partial)
        {
            _signature = partial.Signature;
            Segments = partial.Segments.ToList();
            Tracks = partial.Tracks.ToDictionary(t => t.TrackId, StringComparer.Ordinal);
            Language = partial.Language;
            ElapsedBeforeMs = partial.ElapsedMs;
            Device = partial.Device;
        }

        public List<TranscriptSegment> Segments { get; }

        public Dictionary<string, TranscriptPartialTrack> Tracks { get; }

        public string? Language { get; private set; }

        public WorkerDevice? Device { get; private set; }

        public long ElapsedBeforeMs { get; }

        public double LastEnd => Segments.Count == 0 ? 0 : Segments.Max(s => s.End);

        public int WindowsDone(string trackId) => Tracks.TryGetValue(trackId, out var t) ? t.WindowsDone : 0;

        /// <summary>Applies one worker line; <c>true</c> when the partial file should be saved.</summary>
        public bool Apply(WorkerReply reply)
        {
            switch (reply.Type)
            {
                case WorkerMessageTypes.Device:
                    Device = reply.Device ?? Device;
                    return true;
                case WorkerMessageTypes.Track when reply.Track is { } info:
                    var done = WindowsDone(info.TrackId);
                    Tracks[info.TrackId] = new TranscriptPartialTrack(info.TrackId, info.Silent ? info.Windows : done, info.Windows, info.Silent, info.DurationSeconds, info.Speech);
                    return true;
                case WorkerMessageTypes.Progress:
                    Language = reply.Language ?? Language;
                    // Segments count only together with the window they finish, and only once: a window that was
                    // already kept (a resumed pass, or a worker that sends a window again) adds nothing, so the
                    // partial file can never hold the same speech twice. A window cut short is sent again whole.
                    if (reply.TrackId is { } trackId && Tracks.TryGetValue(trackId, out var track) && reply.WindowsDone is { } windowsDone && windowsDone > track.WindowsDone)
                    {
                        foreach (var segment in reply.Segments ?? [])
                        {
                            Segments.Add(new TranscriptSegment(string.Empty, segment.Start, segment.End, trackId, null, null, segment.Text, segment.Confidence, segment.Words, null));
                        }

                        Tracks[trackId] = track with { WindowsDone = windowsDone };
                        return true;
                    }

                    return false;
                default:
                    return false;
            }
        }

        public TranscriptPartial ToPartial(long elapsedMs) =>
            new(TranscriptPartial.CurrentSchemaVersion, _signature, Tracks.Values.ToList(), Segments, Language, ElapsedBeforeMs + elapsedMs, Device);
    }
}
