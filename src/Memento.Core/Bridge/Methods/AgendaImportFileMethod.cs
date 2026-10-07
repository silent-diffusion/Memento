using System.Text.Json.Serialization.Metadata;
using Memento.Core.Agendas;
using Memento.Core.Bridge.Contracts;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>agenda.importFile</c>: the file picker (or a given path), parsed on this PC.</summary>
public sealed class AgendaImportFileMethod(AgendaService agenda) : BridgeMethod<AgendaImportFileParams, AgendaImportResult>
{
    public override string Name => BridgeMethodNames.AgendaImportFile;

    public override JsonTypeInfo<AgendaImportFileParams> ParamsTypeInfo => M3BridgeJsonContext.Default.AgendaImportFileParams;

    public override JsonTypeInfo<AgendaImportResult> ResultTypeInfo => M3BridgeJsonContext.Default.AgendaImportResult;

    public override Task<AgendaImportResult> InvokeAsync(AgendaImportFileParams parameters, CancellationToken cancellationToken) =>
        agenda.ImportFileAsync(parameters.RecordingId, parameters.Path, cancellationToken);
}
