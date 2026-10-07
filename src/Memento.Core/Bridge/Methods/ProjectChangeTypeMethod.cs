using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Maintenance;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>project.changeType</c>.</summary>
public sealed class ProjectChangeTypeMethod(ProjectTypeService types) : BridgeMethod<ProjectChangeTypeParams, Project>
{
    public override string Name => BridgeMethodNames.ProjectChangeType;

    public override JsonTypeInfo<ProjectChangeTypeParams> ParamsTypeInfo => M3BridgeJsonContext.Default.ProjectChangeTypeParams;

    public override JsonTypeInfo<Project> ResultTypeInfo => M3BridgeJsonContext.Default.Project;

    public override Task<Project> InvokeAsync(ProjectChangeTypeParams parameters, CancellationToken cancellationToken) =>
        types.ChangeTypeAsync(parameters.RecordingId, parameters.Type, cancellationToken);
}
