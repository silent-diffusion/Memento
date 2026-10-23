using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Memento.AI;
using Memento.AI.Payload;
using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Export;
using Memento.Core.Formatting;
using Memento.Core.Engines;
using Memento.Core.Projects;
using Memento.Core.Recording;
using Memento.Core.Settings;
using Memento.Documents.Model;
using Memento.Documents.Model.Modules;
using Memento.Documents.Model.Records;
using Memento.Documents.Render;
using Memento.Documents.Templates;
using Memento.Generation.Ai;
using Memento.Generation.Bridge;
using Memento.Generation.Documents;
using Microsoft.Extensions.Logging;
using BridgeTemplate = Memento.Core.Bridge.Contracts.Template;

namespace Memento.Generation.Generation;

/// <summary>
/// <c>generation.*</c>: the payload preview, the Builder's preview paper, and generation as a cancellable job with
/// <c>generation.progress</c>. One generation runs at a time. External AI must be allowed for a provider that takes the
/// inputs off this PC (<see cref="ProviderStatus.LeavesPc"/>: a cloud one, or "Your own server" elsewhere), which is
/// checked before anything is constructed; "ask before every send" holds such a job until <c>generation.confirm</c>
/// (the local model and a server on this PC send nothing off it, so they are not asked). A failure keeps the provider's
/// specific copy and code and never touches an existing document; a regeneration keeps the replaced content as a version.
/// </summary>
public sealed partial class GenerationService(
    ProviderRegistry providers,
    RecordingMaterialLoader loader,
    ProjectDocumentStore store,
    DocumentService documents,
    TemplateService templates,
    StyleService styles,
    GenerationPipeline pipeline,
    ModuleCatalog catalog,
    DocumentHtmlRenderer renderer,
    ISettingsStore settings,
    IProjectStore projects,
    M4EventPublisher events,
    TimeProvider time,
    ILogger<GenerationService> logger,
    LocalLoadHistory? loads = null,
    RecordingCoordinator? recordings = null,
    IResourceProbe? probe = null) : IDisposable
{
    /// <summary>How long a job waits for "ask before every send" before it is forgotten.</summary>
    public static readonly TimeSpan ConfirmationTimeout = TimeSpan.FromMinutes(10);

    /// <summary>Below the project's <c>documents/</c>: the copies a template's output options ask for.</summary>
    public const string ExportsFolder = "exports";

    private readonly object _gate = new();
    private readonly ILogger<GenerationService> _logger = logger;
    private Job? _job;
    private int _watching;

    /// <summary>
    /// A recording started (2.2, ARCHITECTURE.md §8): a larger local model that is writing is unloaded when the RAM
    /// available now would leave capture short (<see cref="MemoryHeadroom.OsBytes"/> plus the capture reserve); the
    /// generation ends with <c>ai.notEnoughMemory</c> and History says why. The recording is never held up: this only
    /// cancels the job.
    /// </summary>
    public void OnRecordingStarted(bool withVideo)
    {
        Job? job;
        lock (_gate)
        {
            job = _job is { Finished: false, Pending: false, Status.MemoryPlan: { Fits: true } } running ? running : null;
        }

        if (job is null || probe?.Sample() is not { AvailableRamBytes: > 0 } sample)
        {
            return;
        }

        var room = ProviderRegistry.Headroom;
        var needed = room.OsBytes + room.CaptureBytes + (withVideo ? room.CaptureVideoBytes : 0);
        if (sample.AvailableRamBytes >= needed)
        {
            return;
        }

        LogUnloadForRecording(job.Id, sample.AvailableRamBytes, needed);
        job.UnloadedForRecording = sample.AvailableRamBytes;
        job.Cancel.Cancel();
    }

    /// <summary>Listens for recordings starting from the first generation on (only a running generation can be unloaded).</summary>
    private void WatchRecordings()
    {
        if (recordings is not null && Interlocked.Exchange(ref _watching, 1) == 0)
        {
            recordings.Started += OnStarted;
        }
    }

    private void OnStarted(object? sender, string recordingId) => OnRecordingStarted(recordings?.IsCapturingVideo == true);

    /// <summary>Completes when the running generation (if any) has finished.</summary>
    public Task WhenIdleAsync()
    {
        lock (_gate)
        {
            return _job?.Run ?? Task.CompletedTask;
        }
    }

    public async Task<GenerationPreviewResult> PreviewAsync(string recordingId, BridgeTemplate template, CancellationToken cancellationToken)
    {
        var prepared = await PrepareAsync(recordingId, template, null, cancellationToken);
        var preview = new PreviewProvider(prepared.Status);
        var warnings = new List<string>();
        if (!prepared.Status.Ready)
        {
            warnings.Add(prepared.Status.Detail ?? prepared.Status.Reason ?? "The provider is not ready.");
        }

        warnings.AddRange(prepared.Payload.Excluded.Select(e => $"{e.Name}: {e.Reason}"));
        if (prepared.NeedsTranscript && !prepared.Selection.Transcript)
        {
            warnings.Add("The transcript is not among the inputs, so the generated sections cannot cite it.");
        }

        // 2.0: the sheet's inputs, what is held back and every request as the provider will receive it.
        var status = prepared.Status;
        var input = new PipelineInput(
            prepared.Template,
            prepared.Material,
            prepared.Payload,
            prepared.Selection,
            preview,
            prepared.Facts,
            status.ChunkTokens,
            status.MapOutputTokens,
            Bounded: Bounded(status),
            VerifyBatch: VerifyBatch(status));
        var inputs = PayloadPreview.Inputs(prepared.Ticked, prepared.Selection, prepared.Payload, status.LeavesPc);
        return new GenerationPreviewResult(
            prepared.Payload.RenderPreview(preview),
            Encoding.UTF8.GetByteCount(prepared.Payload.Text),
            prepared.Chunks,
            M4Mapping.ToBridge(M4Mapping.Used(prepared.Selection, prepared.Payload)),
            warnings)
        {
            ProviderId = status.Id,
            StaysOnPc = !status.LeavesPc,
            Provider = new PreviewProviderInfo(status.Id, status.Name, status.Kind, status.ModelLabel ?? status.Model),
            AskBeforeSend = settings.Current.Ai.AskBeforeSend,
            Inputs = inputs,
            Redactions = PayloadPreview.Redactions(inputs, prepared.Payload),
            Passes = PayloadPreview.Passes(pipeline.MapRequests(input), preview, input.Bounded),
        };
    }

    /// <summary>The Builder's live preview paper: the real title and meta line, the headings in the style, a skeleton per module.</summary>
    /// <param name="recordingId"><c>null</c> while a template is edited without a recording: the sample minutes' title and meta line.</param>
    public async Task<HtmlResult> PreviewHtmlAsync(string? recordingId, BridgeTemplate template, string styleId, CancellationToken cancellationToken)
    {
        var stored = M4Mapping.FromBridge(template, null, catalog);
        var style = await styles.FindAsync(styleId, cancellationToken);
        if (recordingId is null)
        {
            var sample = SampleDocument.Minutes;
            return new HtmlResult(renderer.RenderSkeleton(stored, sample.Title, sample.Meta with { Kind = Kind(stored) }, style).Html);
        }

        var material = await loader.LoadAsync(recordingId, null, cancellationToken);
        return new HtmlResult(renderer.RenderSkeleton(stored, Title(material), Meta(material, stored), style).Html);
    }

    public async Task<GenerationStartResult> StartAsync(string recordingId, BridgeTemplate template, string? documentId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(template);

        // The privacy rule first: a provider that takes the inputs off this PC (a cloud one, or a server elsewhere) with
        // external AI off is refused before anything is read or built.
        var providerId = providers.ResolveId(template.ProviderId);
        if (providers.LeavesPc(providerId) && !settings.Current.Ai.Enabled)
        {
            throw providerId == ProviderIds.Server
                ? M4Errors.ServerDisabled(ProviderIds.DisplayName(providerId), settings.Current.Ai.Server.ParsedAddress()?.Text ?? "the saved address")
                : M4Errors.AiDisabled(ProviderIds.DisplayName(providerId));
        }

        if (documentId is not null)
        {
            await documents.LoadAsync(recordingId, documentId, cancellationToken);
        }

        var prepared = await PrepareAsync(recordingId, template, documentId, cancellationToken);
        if (prepared.NeedsTranscript && !prepared.Material.HasTranscript)
        {
            throw M4Errors.NoTranscript(Title(prepared.Material));
        }

        if (!prepared.Status.Ready)
        {
            throw M4Errors.ProviderNotReady(prepared.Status.Code ?? DomainErrorCodes.AiProviderNotReady, prepared.Status.Detail ?? prepared.Status.Reason ?? "The provider is not ready.");
        }

        if (prepared.NeedsTranscript && !prepared.Selection.Transcript)
        {
            throw M4Errors.TranscriptNotSent(Title(prepared.Material));
        }

        var confirm = prepared.Status.LeavesPc && settings.Current.Ai.AskBeforeSend;
        Job job;
        lock (_gate)
        {
            if (_job is { } current && !(current.Pending && time.GetUtcNow() - current.Created > ConfirmationTimeout) && !current.Finished)
            {
                throw M4Errors.Busy();
            }

            var id = "g" + Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant();
            job = new Job(id, recordingId, documentId, prepared, time.GetUtcNow(), new GenerationProgressFeed(events.PublishGenerationProgress, time), new GenerationOutputFeed(id, events.PublishGenerationOutput, time)) { Pending = confirm };
            _job = job;
        }

        if (confirm)
        {
            return new GenerationStartResult(job.Id)
            {
                ConfirmationRequired = true,
                Summary = new GenerationSendSummary(
                    prepared.Status.Id,
                    prepared.Status.Name,
                    prepared.Status.ModelLabel,
                    M4Mapping.ToBridge(M4Mapping.Used(prepared.Selection, prepared.Payload)),
                    Encoding.UTF8.GetByteCount(prepared.Payload.Text),
                    prepared.Chunks),
            };
        }

        Launch(job);
        return new GenerationStartResult(job.Id);
    }

    public void Confirm(string jobId, bool approved)
    {
        Job job;
        lock (_gate)
        {
            job = _job is { Id: var id } current && id == jobId && current.Pending && !current.Finished ? current : throw M4Errors.GenerationNotFound(jobId);
            job.Pending = false;
            if (!approved)
            {
                job.Finished = true;
            }
        }

        if (approved)
        {
            Launch(job);
        }
        else
        {
            _ = Finish(job, "cancelled", null, 0, "Nothing was sent.", null);
        }
    }

    public void Cancel(string jobId)
    {
        Job job;
        lock (_gate)
        {
            job = _job is { Id: var id } current && id == jobId && !current.Finished ? current : throw M4Errors.GenerationNotFound(jobId);
            if (job.Pending)
            {
                job.Pending = false;
                job.Finished = true;
            }
        }

        if (job.Run is null)
        {
            _ = Finish(job, "cancelled", null, 0, "Nothing was sent.", null);
            return;
        }

        job.Cancel.Cancel();
    }

    public void Dispose()
    {
        if (recordings is not null && Interlocked.Exchange(ref _watching, 0) == 1)
        {
            recordings.Started -= OnStarted;
        }

        Task? run;
        lock (_gate)
        {
            _job?.Cancel.Cancel();
            run = _job?.Run;
        }

        if (run is not null)
        {
            Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(5))).GetAwaiter().GetResult();
        }
    }

    private void Launch(Job job)
    {
        WatchRecordings();
        lock (_gate)
        {
            job.Run = Task.Run(() => RunAsync(job), CancellationToken.None);
        }
    }

    private async Task RunAsync(Job job)
    {
        var prepared = job.Prepared;
        var started = time.GetLocalNow();
        var clock = Stopwatch.StartNew();
        var token = job.Cancel.Token;
        try
        {
            Publish(job, "composing", null, 1, "Preparing the inputs", force: true);

            // Video memory may have changed since the start was accepted; plan the local model again now. A provider that
            // sends keeps the status the start (and the person's confirmation) was given, so a changed address is never used.
            var status = prepared.Status.Id == ProviderIds.Local ? providers.Status(prepared.Status.Id) : prepared.Status;
            if (!status.Ready)
            {
                throw new AiException(new AiError(status.Code ?? AiErrorCodes.ProviderError, status.Name, status.Detail ?? status.Reason ?? "The local model is not ready."));
            }

            var provider = providers.Create(status);
            job.Status = status;
            job.Provider = provider;
            LogStarted(job.Id, job.RecordingId, status.Id, status.Model ?? "-", prepared.Chunks);
            var input = new PipelineInput(
                prepared.Template,
                prepared.Material,
                prepared.Payload,
                prepared.Selection,
                provider,
                prepared.Facts,
                status.ChunkTokens,
                status.MapOutputTokens,
                Bounded: Bounded(status),
                VerifyBatch: VerifyBatch(status))
            {
                Output = job.Output,
            };
            var progress = new InlineProgress<PipelineProgress>(p => Publish(job, p.Stage, p.ModuleId, p.Percent, p.Message));
            var outcome = await pipeline.RunAsync(input, progress, token);
            token.ThrowIfCancellationRequested();

            var documentId = job.DocumentId ?? ProjectDocumentStore.NewId();
            var placement = await KeepMeasurementAsync(job);
            var record = Record(job, status, outcome, started, clock.ElapsedMilliseconds) with { Placement = placement };
            var reason = job.DocumentId is null ? DocumentChangeReasons.Generated : DocumentChangeReasons.Regenerated;
            var saved = await store.WriteAsync(
                job.RecordingId,
                documentId,
                reason,
                current => new Document
                {
                    Title = Title(prepared.Material),
                    Name = current?.Name ?? Kind(prepared.Template),
                    Meta = Meta(prepared.Material, prepared.Template),
                    StyleId = prepared.Template.DefaultStyleId,
                    Rows = outcome.Rows,
                    Generation = new GenerationReference
                    {
                        RecordId = record.Id,
                        TemplateId = prepared.Template.Id,
                        StyleId = prepared.Template.DefaultStyleId,
                        ProviderId = status.Id,
                        GeneratedAt = started,
                        DurationMs = record.DurationMs,
                    },
                    Record = record,
                    CreatedAt = current?.CreatedAt ?? default,
                },
                CancellationToken.None);
            await documents.ChangedAsync(job.RecordingId, documentId, DocumentChangeReasons.Generated, null, null, CancellationToken.None);
            await HistoryAsync(job.RecordingId, "completed", $"{Kind(prepared.Template)} {(reason == DocumentChangeReasons.Generated ? "generated" : "regenerated")} with {status.Name} ({status.ModelLabel})", placement is null ? Sent(record) : $"{Sent(record)} {placement}.", CancellationToken.None);
            await AlsoExportAsync(saved!, prepared, CancellationToken.None);
            var kept = record.Claims.Count(c => c.Kept);
            LogFinished(job.Id, documentId, record.DurationMs, record.Claims.Count, kept);
            await Finish(job, "done", documentId, 100, "The document is ready.", null);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested && job.UnloadedForRecording is { } available)
        {
            await KeepMeasurementAsync(job);
            var model = job.Status?.LocalModel?.Name ?? job.Status?.ModelLabel ?? "The local model";
            var error = AiErrors.UnloadedForRecording(job.Status?.Name ?? ProviderIds.DisplayName(ProviderIds.Local), model, available);
            await HistoryAsync(job.RecordingId, "failed", $"{Kind(prepared.Template)} stopped: {model} was unloaded for a recording", error.Message, CancellationToken.None);
            await Finish(job, "failed", job.DocumentId, 0, error.Message, error.Code);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            await HistoryAsync(job.RecordingId, "info", $"{Kind(prepared.Template)} generation cancelled", "No document was changed.", CancellationToken.None);
            await Finish(job, "cancelled", job.DocumentId, 0, "Generation was cancelled. No document was changed.", null);
        }
        catch (AiException ex)
        {
            await KeepMeasurementAsync(job);
            LogFailed(job.Id, ex.Code, ex.Error.Diagnostic ?? "-");
            await HistoryAsync(job.RecordingId, "failed", $"{Kind(prepared.Template)} could not be generated", ex.Message, CancellationToken.None);
            await Finish(job, "failed", job.DocumentId, 0, ex.Message, ex.Code);
        }
#pragma warning disable CA1031 // A generation job must end with a specific failed event, whatever went wrong.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogCrashed(ex, job.Id);
            const string Message = "The document could not be generated because of an error in Memento. Nothing was changed; the error was written to the Memento log. Try again.";
            await HistoryAsync(job.RecordingId, "failed", $"{Kind(prepared.Template)} could not be generated", Message, CancellationToken.None);
            await Finish(job, "failed", job.DocumentId, 0, Message, AiErrorCodes.ProviderError);
        }
    }

    /// <summary>
    /// A larger local model's load as the worker reported it: kept for the next plan (<see cref="LocalLoadHistory"/>) and
    /// returned as the sentence "How this was made" and History show, or <c>null</c> for any other provider.
    /// </summary>
    private async Task<string?> KeepMeasurementAsync(Job job)
    {
        if (job.Status?.MemoryPlan is null || job.Provider is not AI.Local.LocalAiProvider local || local.Measurement(time) is not { } measured)
        {
            return null;
        }

        if (loads is not null)
        {
            await loads.RecordAsync(measured, CancellationToken.None);
        }

        return AI.Local.MemoryPlanWording.Loaded(job.Status.LocalModel!.Llm, measured);
    }

    /// <summary>
    /// Ends the job with its final event, which follows every event raised before it; the returned task completes once it
    /// is sent. The Live output ends first (its <c>done</c>), so the sheet is read-only by the time the job is over.
    /// </summary>
    private async Task Finish(Job job, string stage, string? documentId, double percent, string message, string? code)
    {
        lock (_gate)
        {
            job.Finished = true;
        }

        await job.Output.CompleteAsync();
        await job.Events.CompleteAsync(new GenerationProgress(job.Id, job.RecordingId, documentId, stage, null, percent, message) { Code = code });
    }

    /// <summary>Queues a progress event on the job's ordered stream (throttled there; nothing passes after the final event).</summary>
    private static void Publish(Job job, string stage, string? moduleId, double percent, string? message, bool force = false) =>
        job.Events.Report(new GenerationProgress(job.Id, job.RecordingId, job.DocumentId, stage, moduleId, percent, message), force);

    private async Task<Prepared> PrepareAsync(string recordingId, BridgeTemplate template, string? excludeDocumentId, CancellationToken cancellationToken)
    {
        DocumentTemplate? existing = null;
        if (!string.IsNullOrWhiteSpace(template.Id))
        {
            try
            {
                existing = await templates.FindAsync(template.Id, cancellationToken);
            }
            catch (BridgeException)
            {
                existing = null;
            }
        }

        var stored = M4Mapping.FromBridge(template, existing, catalog) with { Id = template.Id ?? string.Empty };
        var material = await loader.LoadAsync(recordingId, excludeDocumentId, cancellationToken);
        var status = providers.Status(providers.ResolveId(stored.ProviderId));
        var selection = M4Mapping.Selection(stored.Inputs);
        if (status.LeavesPc)
        {
            // Settings › AI and privacy decides what may leave the PC; a template's ticks never exceed it.
            selection = selection.Intersect(PayloadSelection.FromShareSettings(settings.Current.Ai.Share));
        }

        var payload = PayloadComposer.Compose(material.ToPayloadInputs(stored.ProcessingInstructions), selection);
        var facts = new GenerationFacts(
            Title(material),
            DataModuleComposer.Purpose(material),
            DataModuleComposer.KnownPeople(material),
            DataModuleComposer.Agenda(material),
            DataModuleComposer.QuoteCandidates(material))
        {
            RecordedAt = material.Manifest.CreatedAt,
        };
        var preview = new PreviewProvider(status);
        var chunks = payload.TranscriptLines.Count == 0 ? 0 : TranscriptChunker.Chunk(payload.TranscriptLines, new ChunkOptions(status.ChunkTokens, new ProviderTokenCounter(preview))).Count;
        var needsTranscript = stored.Modules().Any(m => catalog.Find(m.Type) is { IsAiGenerated: true } && m.Type is not (ModuleIds.MeetingPurpose));
        return new Prepared(stored, material, payload, selection, status, facts, chunks, needsTranscript) { Ticked = M4Mapping.Selection(stored.Inputs) };
    }

    private DocumentGenerationRecord Record(Job job, ProviderStatus status, PipelineOutcome outcome, DateTimeOffset started, long durationMs)
    {
        var prepared = job.Prepared;
        return new DocumentGenerationRecord
        {
            Id = "r" + Convert.ToHexString(RandomNumberGenerator.GetBytes(5)).ToLowerInvariant(),
            TemplateId = prepared.Template.Id,
            TemplateName = prepared.Template.Name,
            StyleId = prepared.Template.DefaultStyleId,
            ProviderId = status.Id,
            ProviderName = status.Name,
            Model = status.Model ?? string.Empty,
            ModelLabel = RecordLabel(status),
            StartedAt = started,
            DurationMs = durationMs,
            Inputs = M4Mapping.ToRecord(prepared.Selection),
            Sent = prepared.Payload.Sections.Select(s => s.Summary.Length == 0 ? s.Title : $"{s.Title} ({s.Summary})").ToList(),
            NotSent = prepared.Payload.Excluded.Select(e => $"{e.Name}: {e.Reason}").ToList(),
            Bytes = Encoding.UTF8.GetByteCount(prepared.Payload.Text),
            PayloadHash = prepared.Payload.Hash,
            PayloadText = settings.Current.Ai.KeepRecord ? prepared.Payload.Text : null,
            StayedOnPc = !status.LeavesPc,
            Chunks = outcome.Chunks,
            Requests = outcome.Requests,
            Timings = outcome.Timings,
            Modules = outcome.Modules,
            Claims = outcome.Claims,
        };
    }

    /// <summary>
    /// The record's model label: the provider's, and for "Your own server" where it ran as well ("qwen3:8b at
    /// localhost:11434 (this PC)", "… (on your network)", "… (remote)"), so "How this was made" and History say it.
    /// </summary>
    private static string RecordLabel(ProviderStatus status)
    {
        var label = status.ModelLabel ?? status.Model ?? string.Empty;
        return status.ServerAddress is { } address ? $"{label} ({address.ZoneLabel})" : label;
    }

    /// <summary>
    /// Item limits in the map schemas: the local model's grammar, and "Your own server", which turns a schema into a
    /// grammar too (Ollama, llama.cpp, vLLM, LM Studio) and usually runs a small model in a small context.
    /// </summary>
    private static bool Bounded(ProviderStatus status) => !status.IsCloud;

    /// <summary>Questions per verify request: a cloud model's batches of 25 with reasons, else the local model's 6 per family (a server's context is small too).</summary>
    private static int VerifyBatch(ProviderStatus status) => status.IsCloud ? GenerationPipeline.VerifyBatchSize : GenerationPipeline.LocalVerifyBatchSize;

    /// <summary>The History line's detail: exactly what was sent, where, how much, and the result.</summary>
    private static string Sent(DocumentGenerationRecord record)
    {
        var ai = record.Modules.Where(m => m.Source == "ai").ToList();
        var where = record.ProviderId == ProviderIds.Server
            ? record.StayedOnPc ? $"sent to {record.ProviderName} ({record.ModelLabel}); nothing left this PC" : $"sent to {record.ProviderName} ({record.ModelLabel})"
            : record.StayedOnPc ? "read by the local model on this PC; nothing was sent" : $"sent to {record.ProviderName} ({record.Model})";
        return string.Create(
            CultureInfo.InvariantCulture,
            $"Inputs: {(record.Sent.Count == 0 ? "none" : string.Join("; ", record.Sent))} · {HumanFormat.Bytes(record.Bytes)} in {record.Chunks} chunk{(record.Chunks == 1 ? string.Empty : "s")}, {where}. Audio and video were not sent. {ai.Count} generated section{(ai.Count == 1 ? string.Empty : "s")}: {ai.Sum(m => m.Claims)} claims, {ai.Sum(m => m.Verified)} verified, {ai.Sum(m => m.Dropped)} dropped{(ai.Count(m => m.NotDiscussed) is var n and > 0 ? $", {n} not discussed" : string.Empty)}. Took {HumanFormat.Clock(record.DurationMs)}.");
    }

    /// <summary>
    /// The template's output options ("Also export Word / Markdown", and PDF): copies in the project's
    /// <c>documents/exports/</c>, named after the document and its version (never over an earlier copy), each listed in History.
    /// </summary>
    private async Task AlsoExportAsync(Document document, Prepared prepared, CancellationToken cancellationToken)
    {
        var output = prepared.Template.Output;
        var formats = new[] { (output.AlsoExportDocx, "docx"), (output.AlsoExportMarkdown, "markdown"), (output.AlsoExportPdf, "pdf") }.Where(f => f.Item1).Select(f => f.Item2).ToList();
        if (formats.Count == 0)
        {
            return;
        }

        var folder = Path.Combine(projects.GetProjectFolder(prepared.Material.RecordingId), ProjectLayout.DocumentsFolder, ExportsFolder);
        Directory.CreateDirectory(folder);
        var stem = DocumentService.FileStem(document.Name ?? document.Title) + string.Create(CultureInfo.InvariantCulture, $" v{document.Version}");
        foreach (var format in formats)
        {
            try
            {
                await documents.ExportAsync(prepared.Material.RecordingId, document.Id, format, Path.Combine(folder, stem), cancellationToken);
            }
            catch (BridgeException ex)
            {
                await HistoryAsync(prepared.Material.RecordingId, "info", $"The {format} copy was not exported", ex.Message, cancellationToken);
            }
        }
    }

    private async Task HistoryAsync(string recordingId, string kind, string summary, string? detail, CancellationToken cancellationToken)
    {
        try
        {
            await projects.AppendHistoryAsync(recordingId, new HistoryEntry(time.GetLocalNow(), StageNames.Minutes, kind, summary, detail), cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ProjectNotFoundException)
        {
            LogHistoryFailed(ex, recordingId);
        }
    }

    private static string Title(RecordingMaterial material) =>
        string.IsNullOrWhiteSpace(material.Details.Title) ? "Untitled recording" : material.Details.Title.Trim();

    private static string Kind(DocumentTemplate template) =>
        string.IsNullOrWhiteSpace(template.DocumentKind) ? template.Name : template.DocumentKind;

    private static DocumentMeta Meta(RecordingMaterial material, DocumentTemplate template) => new()
    {
        Kind = Kind(template),
        RecordedAt = material.Manifest.CreatedAt,
        DurationMs = material.Manifest.DurationMs,
        Platform = string.IsNullOrWhiteSpace(material.Details.Platform) ? null : material.Details.Platform.Trim(),
        ParticipantCount = DataModuleComposer.Participants(material).Count is var n and > 0 ? n : null,
        RecordingTitle = Title(material),
        RecordingId = material.RecordingId,
    };

    [LoggerMessage(Level = LogLevel.Information, Message = "Generation {JobId} for recording {RecordingId} started with {ProviderId} ({Model}), {Chunks} chunk(s)")]
    private partial void LogStarted(string jobId, string recordingId, string providerId, string model, int chunks);

    [LoggerMessage(Level = LogLevel.Information, Message = "Generation {JobId} wrote document {DocumentId} in {DurationMs} ms: {Kept} of {Claims} claims kept")]
    private partial void LogFinished(string jobId, string documentId, long durationMs, int claims, int kept);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Generation {JobId} failed: {Code} ({Diagnostic})")]
    private partial void LogFailed(string jobId, string code, string diagnostic);

    [LoggerMessage(Level = LogLevel.Error, Message = "Generation {JobId} failed unexpectedly")]
    private partial void LogCrashed(Exception exception, string jobId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Generation {JobId}: a recording started with {Available} bytes of RAM available (capture keeps {Needed}); unloading the local model")]
    private partial void LogUnloadForRecording(string jobId, long available, long needed);

    [LoggerMessage(Level = LogLevel.Warning, Message = "History of recording {RecordingId} could not be appended")]
    private partial void LogHistoryFailed(Exception exception, string recordingId);

    private sealed record Prepared(
        DocumentTemplate Template,
        RecordingMaterial Material,
        ComposedPayload Payload,
        PayloadSelection Selection,
        ProviderStatus Status,
        GenerationFacts Facts,
        int Chunks,
        bool NeedsTranscript)
    {
        /// <summary>The template's ticks before Settings' share switches narrowed them (the preview shows both).</summary>
        public PayloadSelection Ticked { get; init; } = Selection;
    }

    private sealed class Job(string id, string recordingId, string? documentId, Prepared prepared, DateTimeOffset created, GenerationProgressFeed events, GenerationOutputFeed output)
    {
        public string Id { get; } = id;

        public string RecordingId { get; } = recordingId;

        public string? DocumentId { get; } = documentId;

        public Prepared Prepared { get; } = prepared;

        public DateTimeOffset Created { get; } = created;

        public bool Pending { get; set; }

        public bool Finished { get; set; }

        public Task? Run { get; set; }

        public CancellationTokenSource Cancel { get; } = new();

        /// <summary>The provider status the job runs with (planned again when it starts).</summary>
        public ProviderStatus? Status { get; set; }

        /// <summary>The provider the job runs on, once constructed.</summary>
        public IAiProvider? Provider { get; set; }

        /// <summary>Set when a recording started and the larger model was unloaded: the RAM available then.</summary>
        public long? UnloadedForRecording { get; set; }

        /// <summary>This job's progress events, in the order they were raised; nothing after the final one.</summary>
        public GenerationProgressFeed Events { get; } = events;

        /// <summary>This job's Live output (<c>generation.output</c>), ended before the final progress event.</summary>
        public GenerationOutputFeed Output { get; } = output;
    }
}
