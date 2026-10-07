using System.Text.Json.Serialization.Metadata;
using Memento.Core.Agendas;
using Memento.Core.Bridge.Contracts;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>agenda.setCovered</c>: ticks an agenda item off (works during recording).</summary>
public sealed class AgendaSetCoveredMethod(AgendaService agenda) : BridgeMethod<AgendaSetCoveredParams, AgendaResult>
{
    public override string Name => BridgeMethodNames.AgendaSetCovered;

    public override JsonTypeInfo<AgendaSetCoveredParams> ParamsTypeInfo => M3BridgeJsonContext.Default.AgendaSetCoveredParams;

    public override JsonTypeInfo<AgendaResult> ResultTypeInfo => M3BridgeJsonContext.Default.AgendaResult;

    public override async Task<AgendaResult> InvokeAsync(AgendaSetCoveredParams parameters, CancellationToken cancellationToken) =>
        new(await agenda.SetCoveredAsync(parameters.RecordingId, parameters.ItemId, parameters.Covered, cancellationToken));
}
