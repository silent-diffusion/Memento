using System.Text.Json.Serialization.Metadata;
using Memento.Core.Agendas;
using Memento.Core.Bridge.Contracts;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>agenda.discard</c>: drops a held original file. An unknown or expired token is not an error.</summary>
public sealed class AgendaDiscardMethod(AgendaService agenda) : BridgeMethod<AgendaDiscardParams, EmptyResult>
{
    public override string Name => BridgeMethodNames.AgendaDiscard;

    public override JsonTypeInfo<AgendaDiscardParams> ParamsTypeInfo => M3BridgeJsonContext.Default.AgendaDiscardParams;

    public override JsonTypeInfo<EmptyResult> ResultTypeInfo => M3BridgeJsonContext.Default.EmptyResult;

    public override Task<EmptyResult> InvokeAsync(AgendaDiscardParams parameters, CancellationToken cancellationToken)
    {
        agenda.Discard(parameters.AttachmentToken);
        return Task.FromResult(new EmptyResult());
    }
}
