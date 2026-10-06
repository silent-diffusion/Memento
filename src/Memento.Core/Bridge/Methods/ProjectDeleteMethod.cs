using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Projects;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>project.delete</c>: the designed Delete flow. Refused with <c>project.recording</c> while that recording is active.</summary>
public sealed class ProjectDeleteMethod(ProjectService projects) : BridgeMethod<RecordingIdParams, EmptyResult>
{
    public override string Name => BridgeMethodNames.ProjectDelete;

    public override JsonTypeInfo<RecordingIdParams> ParamsTypeInfo => BridgeJsonContext.Default.RecordingIdParams;

    public override JsonTypeInfo<EmptyResult> ResultTypeInfo => BridgeJsonContext.Default.EmptyResult;

    public override async Task<EmptyResult> InvokeAsync(RecordingIdParams parameters, CancellationToken cancellationToken)
    {
        await projects.DeleteAsync(parameters.RecordingId, cancellationToken);
        return new EmptyResult();
    }
}
