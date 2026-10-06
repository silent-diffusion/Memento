using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Projects;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>project.get</c>: everything Review needs for one recording.</summary>
public sealed class ProjectGetMethod(ProjectService projects) : BridgeMethod<RecordingIdParams, Project>
{
    public override string Name => BridgeMethodNames.ProjectGet;

    public override JsonTypeInfo<RecordingIdParams> ParamsTypeInfo => BridgeJsonContext.Default.RecordingIdParams;

    public override JsonTypeInfo<Project> ResultTypeInfo => BridgeJsonContext.Default.Project;

    public override async Task<Project> InvokeAsync(RecordingIdParams parameters, CancellationToken cancellationToken)
    {
        return await projects.GetAsync(parameters.RecordingId, cancellationToken);
    }
}
