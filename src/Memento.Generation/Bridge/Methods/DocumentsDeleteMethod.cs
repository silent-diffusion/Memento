using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Settings;
using Memento.Documents.Model.Modules;
using Memento.Generation.Ai;
using Memento.Generation.Documents;
using Memento.Generation.Generation;

namespace Memento.Generation.Bridge.Methods;

/// <summary><c>documents.delete</c>: the UI confirms first; the host asks nothing.</summary>
public sealed class DocumentsDeleteMethod(DocumentService documents) : M4Method<DocumentParams, EmptyResult>
{
    public override string Name => BridgeMethodNames.DocumentsDelete;

    public override JsonTypeInfo<DocumentParams> ParamsTypeInfo => M4BridgeJsonContext.Default.DocumentParams;

    public override JsonTypeInfo<EmptyResult> ResultTypeInfo => M4BridgeJsonContext.Default.EmptyResult;

    public override Task<EmptyResult> InvokeAsync(DocumentParams parameters, CancellationToken cancellationToken) =>
        Done(documents.DeleteAsync(parameters.RecordingId, parameters.DocumentId, cancellationToken));
}
