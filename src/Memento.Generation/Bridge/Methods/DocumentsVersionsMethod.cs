using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Settings;
using Memento.Documents.Model.Modules;
using Memento.Generation.Ai;
using Memento.Generation.Documents;
using Memento.Generation.Generation;

namespace Memento.Generation.Bridge.Methods;

/// <summary><c>documents.versions</c>: empty when history is off.</summary>
public sealed class DocumentsVersionsMethod(DocumentService documents) : M4Method<DocumentParams, DocumentVersionsResult>
{
    public override string Name => BridgeMethodNames.DocumentsVersions;

    public override JsonTypeInfo<DocumentParams> ParamsTypeInfo => M4BridgeJsonContext.Default.DocumentParams;

    public override JsonTypeInfo<DocumentVersionsResult> ResultTypeInfo => M4BridgeJsonContext.Default.DocumentVersionsResult;

    public override Task<DocumentVersionsResult> InvokeAsync(DocumentParams parameters, CancellationToken cancellationToken) =>
        Wrap(documents.VersionsAsync(parameters.RecordingId, parameters.DocumentId, cancellationToken), v => new DocumentVersionsResult(v));
}
