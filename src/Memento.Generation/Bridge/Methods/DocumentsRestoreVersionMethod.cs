using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Settings;
using Memento.Documents.Model.Modules;
using Memento.Generation.Ai;
using Memento.Generation.Documents;
using Memento.Generation.Generation;

namespace Memento.Generation.Bridge.Methods;

/// <summary><c>documents.restoreVersion</c>: the content it replaces is kept as a version.</summary>
public sealed class DocumentsRestoreVersionMethod(DocumentService documents) : M4Method<DocumentRestoreParams, DocumentRestoreResult>
{
    public override string Name => BridgeMethodNames.DocumentsRestoreVersion;

    public override JsonTypeInfo<DocumentRestoreParams> ParamsTypeInfo => M4BridgeJsonContext.Default.DocumentRestoreParams;

    public override JsonTypeInfo<DocumentRestoreResult> ResultTypeInfo => M4BridgeJsonContext.Default.DocumentRestoreResult;

    public override Task<DocumentRestoreResult> InvokeAsync(DocumentRestoreParams parameters, CancellationToken cancellationToken) =>
        documents.RestoreVersionAsync(parameters.RecordingId, parameters.DocumentId, parameters.VersionId, cancellationToken);
}
