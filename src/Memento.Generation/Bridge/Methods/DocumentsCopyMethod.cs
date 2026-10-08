using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Generation.Documents;

namespace Memento.Generation.Bridge.Methods;

/// <summary><c>documents.copy</c>: one document on the clipboard as Markdown and as formatted HTML (after 1.2.0).</summary>
public sealed class DocumentsCopyMethod(DocumentClipboard clipboard) : M4Method<DocumentParams, DocumentCopyResult>
{
    public override string Name => BridgeMethodNames.DocumentsCopy;

    public override JsonTypeInfo<DocumentParams> ParamsTypeInfo => M4BridgeJsonContext.Default.DocumentParams;

    public override JsonTypeInfo<DocumentCopyResult> ResultTypeInfo => M4BridgeJsonContext.Default.DocumentCopyResult;

    public override Task<DocumentCopyResult> InvokeAsync(DocumentParams parameters, CancellationToken cancellationToken) =>
        clipboard.CopyAsync(parameters.RecordingId, parameters.DocumentId, cancellationToken);
}
