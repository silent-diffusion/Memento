using System.Text.Json.Serialization.Metadata;
using Memento.Core.Agendas;
using Memento.Core.Bridge.Contracts;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>agenda.importDropped</c>: a file dropped on the agenda drop zone; <c>agenda.dropUnavailable</c> when its path did not arrive.</summary>
public sealed class AgendaImportDroppedMethod(AgendaService agenda) : BridgeMethod<AgendaImportDroppedParams, AgendaImportResult>
{
    public override string Name => BridgeMethodNames.AgendaImportDropped;

    public override JsonTypeInfo<AgendaImportDroppedParams> ParamsTypeInfo => M3BridgeJsonContext.Default.AgendaImportDroppedParams;

    public override JsonTypeInfo<AgendaImportResult> ResultTypeInfo => M3BridgeJsonContext.Default.AgendaImportResult;

    public override Task<AgendaImportResult> InvokeAsync(AgendaImportDroppedParams parameters, CancellationToken cancellationToken) =>
        agenda.ImportDroppedAsync(parameters.RecordingId, parameters.Paths ?? [], cancellationToken);
}
