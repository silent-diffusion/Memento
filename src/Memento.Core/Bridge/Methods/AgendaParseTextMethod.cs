using System.Text.Json.Serialization.Metadata;
using Memento.Core.Agendas;
using Memento.Core.Bridge.Contracts;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>agenda.parseText</c>: pasted text, parsed on this PC.</summary>
public sealed class AgendaParseTextMethod(AgendaService agenda) : BridgeMethod<AgendaParseTextParams, AgendaPreviewResult>
{
    public override string Name => BridgeMethodNames.AgendaParseText;

    public override JsonTypeInfo<AgendaParseTextParams> ParamsTypeInfo => M3BridgeJsonContext.Default.AgendaParseTextParams;

    public override JsonTypeInfo<AgendaPreviewResult> ResultTypeInfo => M3BridgeJsonContext.Default.AgendaPreviewResult;

    public override Task<AgendaPreviewResult> InvokeAsync(AgendaParseTextParams parameters, CancellationToken cancellationToken) =>
        agenda.ParseTextAsync(parameters.RecordingId, parameters.Text, cancellationToken);
}
