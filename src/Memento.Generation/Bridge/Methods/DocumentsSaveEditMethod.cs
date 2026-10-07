using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Settings;
using Memento.Documents.Model.Modules;
using Memento.Generation.Ai;
using Memento.Generation.Documents;
using Memento.Generation.Generation;

namespace Memento.Generation.Bridge.Methods;

/// <summary><c>documents.saveEdit</c>: the viewer's light edits, parsed back into blocks.</summary>
public sealed class DocumentsSaveEditMethod(DocumentService documents) : M4Method<DocumentSaveEditParams, DocumentSaveEditResult>
{
    public override string Name => BridgeMethodNames.DocumentsSaveEdit;

    public override JsonTypeInfo<DocumentSaveEditParams> ParamsTypeInfo => M4BridgeJsonContext.Default.DocumentSaveEditParams;

    public override JsonTypeInfo<DocumentSaveEditResult> ResultTypeInfo => M4BridgeJsonContext.Default.DocumentSaveEditResult;

    public override Task<DocumentSaveEditResult> InvokeAsync(DocumentSaveEditParams parameters, CancellationToken cancellationToken) =>
        documents.SaveEditAsync(parameters.RecordingId, parameters.DocumentId, parameters.Html ?? string.Empty, cancellationToken);
}
