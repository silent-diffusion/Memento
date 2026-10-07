using System.Text.Json.Serialization.Metadata;
using Memento.Core.Agendas;
using Memento.Core.Bridge.Contracts;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>agenda.apply</c>: the reviewed items become the agenda; the original file is attached.</summary>
public sealed class AgendaApplyMethod(AgendaService agenda) : BridgeMethod<AgendaApplyParams, Project>
{
    public override string Name => BridgeMethodNames.AgendaApply;

    public override JsonTypeInfo<AgendaApplyParams> ParamsTypeInfo => M3BridgeJsonContext.Default.AgendaApplyParams;

    public override JsonTypeInfo<Project> ResultTypeInfo => M3BridgeJsonContext.Default.Project;

    public override Task<Project> InvokeAsync(AgendaApplyParams parameters, CancellationToken cancellationToken) =>
        agenda.ApplyAsync(parameters, cancellationToken);
}
