using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Projects;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>project.updateDetails</c>: partial update of the Details sheet; works during recording too.</summary>
public sealed class ProjectUpdateDetailsMethod(ProjectService projects) : BridgeMethod<ProjectUpdateDetailsParams, Project>
{
    public override string Name => BridgeMethodNames.ProjectUpdateDetails;

    public override JsonTypeInfo<ProjectUpdateDetailsParams> ParamsTypeInfo => BridgeJsonContext.Default.ProjectUpdateDetailsParams;

    public override JsonTypeInfo<Project> ResultTypeInfo => BridgeJsonContext.Default.Project;

    public override async Task<Project> InvokeAsync(ProjectUpdateDetailsParams parameters, CancellationToken cancellationToken)
    {
        return await projects.UpdateDetailsAsync(parameters.RecordingId, parameters.Details, cancellationToken);
    }
}
