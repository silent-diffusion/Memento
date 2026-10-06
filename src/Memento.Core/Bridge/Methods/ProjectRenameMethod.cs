using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Projects;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>project.rename</c>.</summary>
public sealed class ProjectRenameMethod(ProjectService projects) : BridgeMethod<ProjectRenameParams, Project>
{
    public override string Name => BridgeMethodNames.ProjectRename;

    public override JsonTypeInfo<ProjectRenameParams> ParamsTypeInfo => BridgeJsonContext.Default.ProjectRenameParams;

    public override JsonTypeInfo<Project> ResultTypeInfo => BridgeJsonContext.Default.Project;

    public override async Task<Project> InvokeAsync(ProjectRenameParams parameters, CancellationToken cancellationToken)
    {
        return await projects.RenameAsync(parameters.RecordingId, parameters.Title, cancellationToken);
    }
}
