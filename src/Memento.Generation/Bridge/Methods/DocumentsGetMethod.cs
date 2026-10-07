using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Settings;
using Memento.Documents.Model.Modules;
using Memento.Generation.Ai;
using Memento.Generation.Documents;
using Memento.Generation.Generation;

namespace Memento.Generation.Bridge.Methods;

/// <summary><c>documents.get</c>.</summary>
public sealed class DocumentsGetMethod(DocumentService documents) : M4Method<DocumentParams, DocumentGetResult>
{
    public override string Name => BridgeMethodNames.DocumentsGet;

    public override JsonTypeInfo<DocumentParams> ParamsTypeInfo => M4BridgeJsonContext.Default.DocumentParams;

    public override JsonTypeInfo<DocumentGetResult> ResultTypeInfo => M4BridgeJsonContext.Default.DocumentGetResult;

    public override Task<DocumentGetResult> InvokeAsync(DocumentParams parameters, CancellationToken cancellationToken) =>
        documents.GetAsync(parameters.RecordingId, parameters.DocumentId, cancellationToken);
}
