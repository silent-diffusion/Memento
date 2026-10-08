using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Generation.Documents;

namespace Memento.Generation.Bridge.Methods;

/// <summary><c>documents.getVersion</c>: a kept version and its paper, to read (the viewer and Review's History open it).</summary>
public sealed class DocumentsGetVersionMethod(DocumentService documents) : M4Method<DocumentRestoreParams, DocumentVersionResult>
{
    public override string Name => BridgeMethodNames.DocumentsGetVersion;

    public override JsonTypeInfo<DocumentRestoreParams> ParamsTypeInfo => M4BridgeJsonContext.Default.DocumentRestoreParams;

    public override JsonTypeInfo<DocumentVersionResult> ResultTypeInfo => M4BridgeJsonContext.Default.DocumentVersionResult;

    public override Task<DocumentVersionResult> InvokeAsync(DocumentRestoreParams parameters, CancellationToken cancellationToken) =>
        documents.GetVersionAsync(parameters.RecordingId, parameters.DocumentId, parameters.VersionId, cancellationToken);
}
