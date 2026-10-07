using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Library;
using Memento.Core.Projects;

namespace Memento.Core.Processing;

/// <summary>
/// Writes a stage's status (and failure) into the manifest, refreshes the index and publishes
/// <c>processing.progress</c>; progress percentages are published without a write.
/// </summary>
public sealed class StageStatusWriter(ProjectCatalog catalog, BridgeEventPublisher publisher)
{
    public static StageStatus QueuedStatus(string stage) => new(stage, StageStates.Queued, null, "Queued");

    public async Task<ProjectManifest> SetAsync(string recordingId, StageStatus status, CancellationToken cancellationToken, bool clearFailure = true)
    {
        var saved = await catalog.UpdateAsync(
            recordingId,
            m => m with
            {
                Stages = StageList.With(m.Stages, status),
                Failures = clearFailure ? StageList.WithFailure(m.Failures, null, status.Stage) : m.Failures,
            },
            cancellationToken);
        publisher.PublishProcessingProgress(new ProcessingProgressPayload(recordingId, saved.Stages));
        return saved;
    }

    public async Task<ProjectManifest> FailAsync(string recordingId, ProjectStageFailure failure, string label, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(failure);
        var saved = await catalog.UpdateAsync(
            recordingId,
            m => m with
            {
                Stages = StageList.With(m.Stages, new StageStatus(failure.Stage, StageStates.Failed, null, label)),
                Failures = StageList.WithFailure(m.Failures, failure, failure.Stage),
            },
            cancellationToken);
        publisher.PublishProcessingProgress(new ProcessingProgressPayload(recordingId, saved.Stages));
        return saved;
    }

    public async Task<ProjectManifest> RemoveAsync(string recordingId, string stage, CancellationToken cancellationToken)
    {
        var saved = await catalog.UpdateAsync(
            recordingId,
            m => m with { Stages = StageList.Without(m.Stages, stage), Failures = StageList.WithFailure(m.Failures, null, stage) },
            cancellationToken);
        publisher.PublishProcessingProgress(new ProcessingProgressPayload(recordingId, saved.Stages));
        return saved;
    }

    /// <summary>Publishes progress for a stage without writing the manifest (frequent updates).</summary>
    public void PublishProgress(ProjectManifest manifest, StageStatus status)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        publisher.PublishProcessingProgress(new ProcessingProgressPayload(manifest.Id, StageList.With(manifest.Stages, status)));
    }
}
