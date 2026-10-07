using Memento.Core.Bridge.Contracts;
using Memento.Core.Formatting;
using Memento.Core.Processing;
using Memento.Core.Projects;
using Memento.Core.Settings;
using Memento.Core.Transcripts;
using Microsoft.Extensions.Logging;

namespace Memento.Transcription.Stages;

/// <summary>
/// The <c>topics</c> stage (BRIDGE.md M2 clarification 1): a short local pass after <c>speakers</c> that finds up to
/// eight keyword topics on this PC, with no AI. The previous local topics are replaced; the user's own are kept.
/// Chapter suggestions are not made in this version.
/// </summary>
public sealed partial class TopicsStage(
    IProjectStore store,
    TranscriptWriter writer,
    StageStatusWriter status,
    TimeProvider time,
    ILogger<TopicsStage> logger) : IProcessingStage
{
    private readonly StageHistory _history = new(store, time, logger);
    private readonly ILogger<TopicsStage> _logger = logger;

    public string Name => StageNames.Topics;

    public int Order => 30;

    public bool IsHeavy => false;

    public bool AppliesTo(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.Transcription.Auto;
    }

    public async Task RunAsync(StageRun run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        var recordingId = run.RecordingId;
        var transcript = await writer.Store.LoadAsync(recordingId, cancellationToken);
        if (transcript is null)
        {
            await status.RemoveAsync(recordingId, Name, cancellationToken);
            return;
        }

        await status.SetAsync(recordingId, new StageStatus(Name, StageStates.Active, 0, "Finding topics"), cancellationToken);
        try
        {
            var labels = TopicExtractor.Extract(transcript.Segments);
            await store.UpdateAnnotationsAsync(
                recordingId,
                d =>
                {
                    var kept = d.Topics.Where(t => t.Origin != AnnotationOrigins.Local).ToList();
                    var added = labels
                        .Where(l => !kept.Any(t => string.Equals(t.Label, l, StringComparison.OrdinalIgnoreCase)))
                        .Select(l => new Topic(AnnotationIds.New('t'), l, AnnotationOrigins.Local));
                    return d with { Topics = kept.Concat(added).ToList() };
                },
                cancellationToken);
            await status.SetAsync(recordingId, new StageStatus(Name, StageStates.Done, null, "Done"), CancellationToken.None);
            writer.PublishTopicsChanged(recordingId, transcript.Version);
            await _history.AppendAsync(
                recordingId,
                Name,
                "completed",
                labels.Count == 0 ? "No topics found" : $"Found {HumanFormat.Count(labels.Count, "topic", "topics")}",
                labels.Count == 0 ? "The transcript is too short or too varied for keyword topics." : "Keyword scoring on this PC (no AI): " + string.Join(", ", labels));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ProjectSchemaException)
        {
            LogFailed(ex, recordingId);
            await status.FailAsync(
                recordingId,
                new ProjectStageFailure(Name, "Topics could not be saved.", "The transcript is kept; topics can be added by hand.", [new Remedy(Remedies.Retry, "Try again")], ProjectStageFailure.CauseEngine, time.GetLocalNow()),
                "Topics failed",
                CancellationToken.None);
            await _history.AppendAsync(recordingId, Name, "failed", "Topics could not be saved", "The transcript is kept; topics can be added by hand.");
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Recording {RecordingId}: topics could not be saved")]
    private partial void LogFailed(Exception exception, string recordingId);
}
