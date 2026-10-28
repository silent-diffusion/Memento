using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Memento.AI;
using Memento.AI.Ask;
using Memento.AI.Payload;
using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Projects;
using Memento.Core.Settings;
using Memento.Generation.Ai;
using Memento.Generation.Bridge;
using Memento.Generation.Documents;
using Memento.Generation.Generation;
using Microsoft.Extensions.Logging;

namespace Memento.Generation.Ask;

/// <summary>
/// <c>ask.*</c> (2.9.0): a conversation with an AI about one recording's transcript. Each question is one request to the
/// chosen provider with the recording material (the whole payload when it fits the model's budget, otherwise the
/// sections a local BM25 search picks, see <see cref="AskContextBuilder"/>) and the earlier turns. The privacy rules are
/// generation's: a provider that takes text off this PC needs "Allow external AI services" (checked before anything is
/// read), receives only what the share switches allow, and with "Ask before every send" waits for <c>ask.confirm</c>.
/// Nothing is sent without the person pressing Ask. One question is answered at a time; the answer streams as
/// <c>ask.progress</c> and is saved with its citations when it is complete (or, when stopped, as far as it got).
/// </summary>
public sealed partial class AskService(
    ProviderRegistry providers,
    RecordingMaterialLoader loader,
    AskConversationStore store,
    ISettingsStore settings,
    IProjectStore projects,
    M4EventPublisher events,
    TimeProvider time,
    ILogger<AskService> logger) : IDisposable
{
    /// <summary>The longest question Ask takes, in characters.</summary>
    public const int MaxQuestionLength = 4000;

    /// <summary>The most material tokens a cloud request carries (a transcript beyond it is searched instead).</summary>
    public const int CloudMaterialTokens = 96_000;

    /// <summary>Earlier turns sent with a question: at most this many pairs.</summary>
    public const int MaxHistoryPairs = 6;

    /// <summary>How often streamed text is passed to the UI at most.</summary>
    public static readonly TimeSpan TokenInterval = TimeSpan.FromMilliseconds(80);

    private readonly object _gate = new();
    private readonly ILogger<AskService> _logger = logger;
    private Job? _job;

    /// <summary>What the Ask page sends with a question (when the share switches allow it for a provider that leaves the PC).</summary>
    public static PayloadSelection Selection { get; } = new()
    {
        Transcript = true,
        Details = true,
        Participants = true,
        Agenda = true,
        ChaptersAndTopics = true,
        Highlights = true,
    };

    /// <summary>Completes when the running question (if any) has its final event.</summary>
    public Task WhenIdleAsync()
    {
        lock (_gate)
        {
            return _job?.Run ?? Task.CompletedTask;
        }
    }

    public async Task<AskConversationResult> GetAsync(string recordingId, CancellationToken cancellationToken)
    {
        EnsureProject(recordingId);
        var (turns, _) = await store.ReadAsync(recordingId, cancellationToken);
        return new AskConversationResult(recordingId, turns, RunningFor(recordingId));
    }

    public async Task<AskConversationResult> ClearAsync(string recordingId, CancellationToken cancellationToken)
    {
        EnsureProject(recordingId);
        if (RunningFor(recordingId) is not null)
        {
            throw AskErrors.ClearWhileAnswering();
        }

        await store.ClearAsync(recordingId, cancellationToken);
        await HistoryAsync(recordingId, "Ask conversation cleared", "The questions and answers were deleted from this recording.", CancellationToken.None);
        return new AskConversationResult(recordingId, [], null);
    }

    public async Task<AskSendResult> SendAsync(string recordingId, string question, string? providerId, CancellationToken cancellationToken)
    {
        var text = (question ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            throw AskErrors.EmptyQuestion();
        }

        if (text.Length > MaxQuestionLength)
        {
            throw AskErrors.QuestionTooLong(text.Length, MaxQuestionLength);
        }

        if (providerId is not null && !ProviderIds.IsValid(providerId))
        {
            throw AskErrors.UnknownProvider(providerId);
        }

        // The privacy rule first: nothing is read or built for a provider that may not receive it.
        var id = providers.ResolveId(providerId);
        if (providers.LeavesPc(id) && !settings.Current.Ai.Enabled)
        {
            throw id == ProviderIds.Server
                ? AskErrors.ServerDisabled(settings.Current.Ai.Server.ParsedAddress()?.Text ?? "the saved address")
                : AskErrors.AiDisabled(ProviderIds.DisplayName(id));
        }

        lock (_gate)
        {
            if (Busy())
            {
                throw AskErrors.Busy();
            }
        }

        var material = await loader.LoadAsync(recordingId, null, cancellationToken);
        var title = string.IsNullOrWhiteSpace(material.Details.Title) ? "Untitled recording" : material.Details.Title.Trim();
        if (!material.HasTranscript)
        {
            throw AskErrors.NoTranscript(title);
        }

        var status = providers.Status(id);
        if (!status.Ready)
        {
            throw M4Errors.ProviderNotReady(status.Code ?? DomainErrorCodes.AiProviderNotReady, status.Detail ?? status.Reason ?? $"{status.Name} is not ready, so nothing was sent.");
        }

        var selection = status.LeavesPc ? Selection.Intersect(PayloadSelection.FromShareSettings(settings.Current.Ai.Share)) : Selection;
        if (!selection.Transcript)
        {
            throw AskErrors.TranscriptNotShared(status.Name);
        }

        var (turns, writable) = await store.ReadAsync(recordingId, cancellationToken);
        if (!writable)
        {
            throw AskErrors.NewerVersion();
        }

        var inputs = material.ToPayloadInputs(null);
        var payload = PayloadComposer.Compose(inputs, selection);
        var preview = new PreviewProvider(status);
        var plan = Plan(status, preview, payload, inputs.Chapters, turns, text);
        var questionTurn = new AskTurn(AskConversationStore.NextId(turns), "user", text, time.GetLocalNow());
        var confirm = status.LeavesPc && settings.Current.Ai.AskBeforeSend;
        Job job;
        lock (_gate)
        {
            if (Busy())
            {
                throw AskErrors.Busy();
            }

            var jobId = "a" + Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant();
            job = new Job(jobId, recordingId, status, payload, plan, questionTurn, time.GetUtcNow()) { Pending = confirm };
            _job = job;
        }

        var result = new AskSendResult(job.Id, questionTurn);
        if (confirm)
        {
            return result with
            {
                ConfirmationRequired = true,
                Summary = new AskSendSummary(status.Id, status.Name, status.ModelLabel, plan.Info, Encoding.UTF8.GetByteCount(AiRequestText.Render(plan.Request))),
            };
        }

        await LaunchAsync(job);
        return result;
    }

    public async Task ConfirmAsync(string jobId, bool approved)
    {
        Job job;
        lock (_gate)
        {
            job = _job is { } current && current.Id == jobId && current.Pending && !current.Finished ? current : throw AskErrors.NotFound(jobId);
            job.Pending = false;
            if (!approved)
            {
                job.Finished = true;
            }
        }

        if (approved)
        {
            await LaunchAsync(job);
        }
        else
        {
            Publish(job, "cancelled", "Nothing was sent.");
        }
    }

    public void Cancel(string jobId)
    {
        Job job;
        lock (_gate)
        {
            job = _job is { } current && current.Id == jobId && !current.Finished ? current : throw AskErrors.NotFound(jobId);
            if (job.Pending)
            {
                job.Pending = false;
                job.Finished = true;
                Publish(job, "cancelled", "Nothing was sent.");
                return;
            }
        }

        job.Cancel.Cancel();
    }

    public void Dispose()
    {
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

    /// <summary>
    /// The request for a question: the instructions, up to <see cref="MaxHistoryPairs"/> earlier question-and-answer pairs
    /// (newest kept, within a quarter of the context), then the material and the question. The material's budget is what
    /// the context has left after the answer's room and everything else, capped for cloud models.
    /// </summary>
    internal static AskPlan Plan(ProviderStatus status, IAiProvider counter, ComposedPayload payload, IReadOnlyList<PayloadMarker> chapters, IReadOnlyList<AskTurn> turns, string question)
    {
        var window = counter.Capabilities.MaxContextTokens;
        var output = OutputTokens(status, window);
        var history = History(turns, counter, window / 4);
        var historyTokens = history.Sum(m => counter.CountTokens(m.Content) + 8);
        var fixedTokens = counter.CountTokens(AskPrompts.System) + historyTokens + counter.CountTokens(question) + 96;
        var budget = window - output - fixedTokens;
        if (status.IsCloud)
        {
            budget = Math.Min(budget, CloudMaterialTokens);
        }

        // The previous question helps a follow-up ("and who agreed to that?") find its sections.
        var previous = turns.LastOrDefault(t => t.Role == "user")?.Text;
        var query = previous is null ? question : question + "\n" + previous;
        var context = AskContextBuilder.Build(payload, chapters, query, new ProviderTokenCounter(counter), Math.Max(256, budget));
        var request = new AiRequest
        {
            Purpose = AskPrompts.Purpose,
            System = AskPrompts.System,
            Messages = [.. history, AiMessage.User(AskPrompts.Question(context.Text, question, context.Whole))],
            MaxOutputTokens = output,
        };
        return new AskPlan(context, request, Info(context, payload));
    }

    /// <summary>The answer's room: a quarter of a small context, at most 1,024 tokens locally, 2,048 on a server, 2,000 in the cloud.</summary>
    internal static int OutputTokens(ProviderStatus status, int window) =>
        status.Id == ProviderIds.Local ? Math.Clamp(window / 4, 256, 1024)
        : status.IsServer ? Math.Clamp(window / 4, 256, 2048)
        : 2000;

    /// <summary>Earlier answered questions as conversation, newest kept, oldest first.</summary>
    internal static List<AiMessage> History(IReadOnlyList<AskTurn> turns, IAiProvider counter, int budget)
    {
        var pairs = new List<(AskTurn Question, AskTurn Answer)>();
        for (var i = 0; i + 1 < turns.Count; i++)
        {
            if (turns[i].Role == "user" && turns[i + 1].Role == "assistant" && turns[i + 1].Text.Length > 0)
            {
                pairs.Add((turns[i], turns[i + 1]));
                i++;
            }
        }

        var kept = new List<(AskTurn Question, AskTurn Answer)>();
        var used = 0;
        for (var i = pairs.Count - 1; i >= 0 && kept.Count < MaxHistoryPairs; i--)
        {
            var cost = counter.CountTokens(pairs[i].Question.Text) + counter.CountTokens(pairs[i].Answer.Text) + 16;
            if (used + cost > budget)
            {
                break;
            }

            kept.Insert(0, pairs[i]);
            used += cost;
        }

        return kept.SelectMany(p => new[] { AiMessage.User(AskPrompts.Inert(PayloadComposer.Neutralise(p.Question.Text))), AiMessage.Assistant(p.Answer.Text) }).ToList();
    }

    private static AskContextInfo Info(AskContext context, ComposedPayload payload) => new(
        context.Whole ? "whole" : "sections",
        context.Excerpts.Select(e => new AskSection(e.Index + 1, e.FirstLine, e.LastLine, e.Start, e.End, e.Title, e.Matched)).ToList(),
        context.SectionCount,
        context.TotalLines,
        context.ContextIncluded ? payload.Sections.Where(s => s.Kind != PayloadSectionKind.Transcript).Select(s => s.Title).ToList() : [],
        context.AnyMatched,
        context.Tokens);

    private bool Busy() => _job is { Finished: false } current && !(current.Pending && time.GetUtcNow() - current.Created > GenerationService.ConfirmationTimeout);

    private string? RunningFor(string recordingId)
    {
        lock (_gate)
        {
            return Busy() && _job!.RecordingId == recordingId ? _job.Id : null;
        }
    }

    private void EnsureProject(string recordingId)
    {
        if (string.IsNullOrWhiteSpace(recordingId) || !projects.Exists(recordingId))
        {
            throw M4Errors.ProjectNotFound(recordingId ?? string.Empty);
        }
    }

    private async Task LaunchAsync(Job job)
    {
        // The question is kept from the moment it is sent, so it is there after a restart even without an answer.
        try
        {
            await store.AppendAsync(job.RecordingId, job.Question, CancellationToken.None);
        }
        catch
        {
            lock (_gate)
            {
                job.Finished = true;
            }

            throw;
        }

        lock (_gate)
        {
            job.Run = Task.Run(() => RunAsync(job), CancellationToken.None);
        }
    }

    private async Task RunAsync(Job job)
    {
        var token = job.Cancel.Token;
        var answer = new StringBuilder();
        var pending = new StringBuilder();
        long? lastSent = null;
        var plan = job.Plan;
        try
        {
            Publish(job, "context", string.Empty, context: plan.Info);

            // Video memory may have changed since the question was accepted; plan the local model again now. A provider that
            // sends keeps the status the question (and the person's confirmation) was given.
            var status = job.Status.Id == ProviderIds.Local ? providers.Status(job.Status.Id) : job.Status;
            if (!status.Ready)
            {
                throw new AiException(new AiError(status.Code ?? AiErrorCodes.ProviderError, status.Name, status.Detail ?? status.Reason ?? "The local model is not ready."));
            }

            var provider = providers.Create(status);
            LogStarted(job.Id, job.RecordingId, status.Id, plan.Info.Mode, plan.Info.Sections.Count);
            var progress = new InlineProgress<AiProgress>(p =>
            {
                if (string.IsNullOrEmpty(p.Delta) || token.IsCancellationRequested)
                {
                    return;
                }

                lock (answer)
                {
                    answer.Append(p.Delta);
                    pending.Append(p.Delta);
                    if (lastSent is not { } last || time.GetElapsedTime(last) >= TokenInterval)
                    {
                        Publish(job, "token", pending.ToString());
                        pending.Clear();
                        lastSent = time.GetTimestamp();
                    }
                }
            });
            var response = await provider.GenerateAsync(plan.Request, progress, token);
            token.ThrowIfCancellationRequested();
            lock (answer)
            {
                if (pending.Length > 0)
                {
                    Publish(job, "token", pending.ToString());
                    pending.Clear();
                }
            }

            var text = response.Text.Trim();
            var turn = Answer(job, status, text, stopped: false);
            await store.AppendAsync(job.RecordingId, turn, CancellationToken.None);
            await HistoryAsync(job.RecordingId, $"Question answered by {status.Name}", Sent(status, plan), CancellationToken.None);
            Finish(job, "done", string.Empty, turn: turn);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            string partial;
            lock (answer)
            {
                partial = answer.ToString().Trim();
            }

            if (partial.Length == 0)
            {
                Finish(job, "cancelled", "Stopped before any answer arrived. Nothing was saved.");
                return;
            }

            var turn = Answer(job, job.Status, partial, stopped: true);
            await TryAppendAsync(job, turn);
            Finish(job, "cancelled", "Stopped. The part of the answer that had arrived was saved.", turn: turn);
        }
        catch (AiException ex)
        {
            LogFailed(job.Id, ex.Code, ex.Error.Diagnostic ?? "-");
            Finish(job, "failed", ex.Message, code: ex.Code);
        }
        catch (BridgeException ex)
        {
            Finish(job, "failed", ex.Message, code: ex.Code);
        }
#pragma warning disable CA1031 // A question must end with a specific failed event, whatever went wrong.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogCrashed(ex, job.Id);
            Finish(job, "failed", "The question could not be answered because of an error in Memento. Your question was kept; the error was written to the Memento log. Ask again.", code: AiErrorCodes.ProviderError);
        }
    }

    private async Task TryAppendAsync(Job job, AskTurn turn)
    {
        try
        {
            await store.AppendAsync(job.RecordingId, turn, CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BridgeException)
        {
            LogSaveFailed(ex, job.RecordingId);
        }
    }

    private AskTurn Answer(Job job, ProviderStatus status, string text, bool stopped)
    {
        var citations = AskPrompts.CitedLines(text)
            .Where(job.Plan.Context.Sent)
            .Select(line => job.Payload.Line(line))
            .OfType<TranscriptLine>()
            .Select(line => new AskCitation(line.ShortId, line.SegmentId, line.Start))
            .Take(100)
            .ToList();
        return new AskTurn(AskConversationStore.NextId([job.Question]), "assistant", text, time.GetLocalNow())
        {
            ProviderId = status.Id,
            ProviderName = status.Name,
            ModelLabel = status.ModelLabel ?? status.Model,
            StayedOnPc = !status.LeavesPc,
            Citations = citations,
            Context = job.Plan.Info,
            Stopped = stopped ? true : null,
        };
    }

    private void Finish(Job job, string kind, string text, AskTurn? turn = null, string? code = null)
    {
        lock (_gate)
        {
            job.Finished = true;
        }

        Publish(job, kind, text, turn: turn, code: code);
    }

    private void Publish(Job job, string kind, string text, AskContextInfo? context = null, AskTurn? turn = null, string? code = null)
    {
        try
        {
            events.PublishAskProgress(new AskProgress(job.Id, job.RecordingId, kind, text) { Context = context, Turn = turn, Code = code });
        }
#pragma warning disable CA1031 // A failing sink must not stop the events that follow, above all the last one.
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }

    /// <summary>The History line's detail: where the question went and how much went with it; never the content.</summary>
    private static string Sent(ProviderStatus status, AskPlan plan)
    {
        var where = status.LeavesPc ? $"sent to {status.Name} ({status.ModelLabel ?? status.Model})" : status.IsServer ? $"sent to {status.Name} ({status.ModelLabel}); nothing left this PC" : "read by the local model on this PC; nothing was sent";
        var what = plan.Info.Mode == "whole"
            ? string.Create(CultureInfo.InvariantCulture, $"the whole transcript ({plan.Info.TotalLines:N0} lines)")
            : string.Create(CultureInfo.InvariantCulture, $"{plan.Info.Sections.Count} of {plan.Info.SectionCount} transcript sections");
        var extra = plan.Info.Inputs.Count == 0 ? string.Empty : " with " + string.Join(", ", plan.Info.Inputs);
        return $"One question and {what}{extra}, {where}. Audio and video were not sent.";
    }

    private async Task HistoryAsync(string recordingId, string summary, string detail, CancellationToken cancellationToken)
    {
        try
        {
            await projects.AppendHistoryAsync(recordingId, new HistoryEntry(time.GetLocalNow(), StageNames.Minutes, "info", summary, detail), cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ProjectNotFoundException)
        {
            LogHistoryFailed(ex, recordingId);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Ask {JobId} for recording {RecordingId} started with {ProviderId}: {Mode}, {Sections} section(s)")]
    private partial void LogStarted(string jobId, string recordingId, string providerId, string mode, int sections);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ask {JobId} failed: {Code} ({Diagnostic})")]
    private partial void LogFailed(string jobId, string code, string diagnostic);

    [LoggerMessage(Level = LogLevel.Error, Message = "Ask {JobId} failed unexpectedly")]
    private partial void LogCrashed(Exception exception, string jobId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A stopped Ask answer for recording {RecordingId} could not be saved")]
    private partial void LogSaveFailed(Exception exception, string recordingId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "History of recording {RecordingId} could not be appended")]
    private partial void LogHistoryFailed(Exception exception, string recordingId);

    internal sealed record AskPlan(AskContext Context, AiRequest Request, AskContextInfo Info);

    private sealed class Job(string id, string recordingId, ProviderStatus status, ComposedPayload payload, AskPlan plan, AskTurn question, DateTimeOffset created)
    {
        public string Id { get; } = id;

        public string RecordingId { get; } = recordingId;

        public ProviderStatus Status { get; } = status;

        public ComposedPayload Payload { get; } = payload;

        public AskPlan Plan { get; } = plan;

        public AskTurn Question { get; } = question;

        public DateTimeOffset Created { get; } = created;

        public bool Pending { get; set; }

        public bool Finished { get; set; }

        public Task? Run { get; set; }

        public CancellationTokenSource Cancel { get; } = new();
    }
}
