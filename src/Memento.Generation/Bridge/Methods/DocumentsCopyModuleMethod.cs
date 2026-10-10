using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Generation.Documents;

namespace Memento.Generation.Bridge.Methods;

/// <summary><c>documents.copyModule</c>: one module (the Follow-up email) on the clipboard as plain text and formatted HTML (2.0).</summary>
public sealed class DocumentsCopyModuleMethod(DocumentClipboard clipboard) : M4Method<DocumentModuleParams, DocumentCopyResult>
{
    public override string Name => BridgeMethodNames.DocumentsCopyModule;

    public override JsonTypeInfo<DocumentModuleParams> ParamsTypeInfo => M4BridgeJsonContext.Default.DocumentModuleParams;

    public override JsonTypeInfo<DocumentCopyResult> ResultTypeInfo => M4BridgeJsonContext.Default.DocumentCopyResult;

    public override Task<DocumentCopyResult> InvokeAsync(DocumentModuleParams parameters, CancellationToken cancellationToken) =>
        clipboard.CopyModuleAsync(parameters.RecordingId, parameters.DocumentId, parameters.ModuleId, cancellationToken);
}
