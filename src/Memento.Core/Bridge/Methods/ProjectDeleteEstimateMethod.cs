using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Projects;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>project.deleteEstimate</c>: what a delete would remove, for the confirmation copy.</summary>
public sealed class ProjectDeleteEstimateMethod(ProjectService projects) : BridgeMethod<RecordingIdParams, ProjectDeleteEstimateResult>
{
    public override string Name => BridgeMethodNames.ProjectDeleteEstimate;

    public override JsonTypeInfo<RecordingIdParams> ParamsTypeInfo => BridgeJsonContext.Default.RecordingIdParams;

    public override JsonTypeInfo<ProjectDeleteEstimateResult> ResultTypeInfo => BridgeJsonContext.Default.ProjectDeleteEstimateResult;

    public override async Task<ProjectDeleteEstimateResult> InvokeAsync(RecordingIdParams parameters, CancellationToken cancellationToken)
    {
        return await projects.DeleteEstimateAsync(parameters.RecordingId, cancellationToken);
    }
}
