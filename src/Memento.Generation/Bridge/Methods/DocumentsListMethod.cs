using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Settings;
using Memento.Documents.Model.Modules;
using Memento.Generation.Ai;
using Memento.Generation.Documents;
using Memento.Generation.Generation;

namespace Memento.Generation.Bridge.Methods;

/// <summary><c>documents.list</c>: the recording's documents, newest change first.</summary>
public sealed class DocumentsListMethod(DocumentService documents) : M4Method<RecordingIdParams, DocumentsListResult>
{
    public override string Name => BridgeMethodNames.DocumentsList;

    public override JsonTypeInfo<RecordingIdParams> ParamsTypeInfo => M4BridgeJsonContext.Default.RecordingIdParams;

    public override JsonTypeInfo<DocumentsListResult> ResultTypeInfo => M4BridgeJsonContext.Default.DocumentsListResult;

    public override Task<DocumentsListResult> InvokeAsync(RecordingIdParams parameters, CancellationToken cancellationToken) =>
        Wrap(documents.ListAsync(parameters.RecordingId, cancellationToken), d => new DocumentsListResult(d));
}
