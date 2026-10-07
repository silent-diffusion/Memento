using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Settings;
using Memento.Documents.Model.Modules;
using Memento.Generation.Ai;
using Memento.Generation.Documents;
using Memento.Generation.Generation;

namespace Memento.Generation.Bridge.Methods;

/// <summary><c>documents.duplicate</c>.</summary>
public sealed class DocumentsDuplicateMethod(DocumentService documents) : M4Method<DocumentNameParams, DocumentSummary>
{
    public override string Name => BridgeMethodNames.DocumentsDuplicate;

    public override JsonTypeInfo<DocumentNameParams> ParamsTypeInfo => M4BridgeJsonContext.Default.DocumentNameParams;

    public override JsonTypeInfo<DocumentSummary> ResultTypeInfo => M4BridgeJsonContext.Default.DocumentSummary;

    public override Task<DocumentSummary> InvokeAsync(DocumentNameParams parameters, CancellationToken cancellationToken) =>
        documents.DuplicateAsync(parameters.RecordingId, parameters.DocumentId, parameters.Name, cancellationToken);
}
