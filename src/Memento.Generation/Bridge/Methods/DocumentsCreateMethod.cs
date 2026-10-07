using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Settings;
using Memento.Documents.Model.Modules;
using Memento.Generation.Ai;
using Memento.Generation.Documents;
using Memento.Generation.Generation;

namespace Memento.Generation.Bridge.Methods;

/// <summary><c>documents.create</c>: a hand-written document.</summary>
public sealed class DocumentsCreateMethod(DocumentService documents) : M4Method<DocumentCreateParams, DocumentSummary>
{
    public override string Name => BridgeMethodNames.DocumentsCreate;

    public override JsonTypeInfo<DocumentCreateParams> ParamsTypeInfo => M4BridgeJsonContext.Default.DocumentCreateParams;

    public override JsonTypeInfo<DocumentSummary> ResultTypeInfo => M4BridgeJsonContext.Default.DocumentSummary;

    public override Task<DocumentSummary> InvokeAsync(DocumentCreateParams parameters, CancellationToken cancellationToken) =>
        documents.CreateAsync(parameters.RecordingId, parameters.Name, parameters.StyleId, cancellationToken);
}
