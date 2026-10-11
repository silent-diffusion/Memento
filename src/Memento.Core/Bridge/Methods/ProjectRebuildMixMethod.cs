using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Processing;
using Memento.Core.Projects;

namespace Memento.Core.Bridge.Methods;

/// <summary>
/// <c>project.rebuildMix</c> (after 2.0.0-beta.1, Review › More › Reprocess): writes the request into the manifest and
/// queues the <c>mix</c> stage (<see cref="MixStage"/>), which mixes the stored tracks again with evened levels or as
/// recorded and replaces the mix; progress and the result arrive as <c>processing.progress</c> and History. Refused for a
/// recording that is still being recorded or stored, and for one whose separate tracks were removed.
/// </summary>
public sealed class ProjectRebuildMixMethod(IProjectStore store, ProcessingOrchestrator processing, TimeProvider time) : BridgeMethod<ProjectRebuildMixParams, EmptyResult>
{
    public override string Name => BridgeMethodNames.ProjectRebuildMix;

    public override JsonTypeInfo<ProjectRebuildMixParams> ParamsTypeInfo => BridgeJsonContext.Default.ProjectRebuildMixParams;

    public override JsonTypeInfo<EmptyResult> ResultTypeInfo => BridgeJsonContext.Default.EmptyResult;

    public override async Task<EmptyResult> InvokeAsync(ProjectRebuildMixParams parameters, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        if (!store.Exists(parameters.RecordingId))
        {
            throw ProjectService.NotFound(parameters.RecordingId);
        }

        var manifest = await store.LoadAsync(parameters.RecordingId, cancellationToken);
        if (manifest.State is not (ProjectStates.Ready or ProjectStates.Recovered) || manifest.Mix is null)
        {
            throw new BridgeException(
                DomainErrorCodes.ProjectRecording,
                "The mix can be built again once the recording is stored. Nothing was changed; try again when it shows in the Library as ready.");
        }

        if (manifest.MixOnly is not null)
        {
            throw new BridgeException(
                DomainErrorCodes.ProjectMixUnavailable,
                "The mix cannot be built again: this recording keeps only its mix, so its separate tracks are gone. Nothing was changed.");
        }

        var folder = store.GetProjectFolder(parameters.RecordingId);
        var missing = manifest.Tracks.FirstOrDefault(t => !ProjectPaths.IsSafeRelative(t.File) || !File.Exists(ProjectPaths.Resolve(folder, t.File)));
        if (manifest.Tracks.Count == 0 || missing is not null)
        {
            var name = missing is null ? "any track" : $"the {missing.Name} track ({missing.File})";
            throw new BridgeException(
                DomainErrorCodes.ProjectMixUnavailable,
                $"The mix cannot be built again: the file of {name} is not in the recording's folder. Nothing was changed; put the file back and try again.");
        }

        await store.UpdateAsync(parameters.RecordingId, m => m with { MixRebuild = new ProjectMixRebuild(parameters.EvenLevels, time.GetLocalNow()) }, cancellationToken);
        await processing.RetryAsync(parameters.RecordingId, StageNames.Mix, null, cancellationToken);
        return new EmptyResult();
    }
}
