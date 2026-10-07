using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Settings;
using Memento.Documents.Model.Modules;
using Memento.Generation.Ai;
using Memento.Generation.Documents;
using Memento.Generation.Generation;

namespace Memento.Generation.Bridge.Methods;

/// <summary><c>documents.export</c>: one document as Word, PDF or Markdown.</summary>
public sealed class DocumentsExportMethod(DocumentService documents) : M4Method<DocumentExportParams, DocumentFileResult>
{
    public override string Name => BridgeMethodNames.DocumentsExport;

    public override JsonTypeInfo<DocumentExportParams> ParamsTypeInfo => M4BridgeJsonContext.Default.DocumentExportParams;

    public override JsonTypeInfo<DocumentFileResult> ResultTypeInfo => M4BridgeJsonContext.Default.DocumentFileResult;

    public override Task<DocumentFileResult> InvokeAsync(DocumentExportParams parameters, CancellationToken cancellationToken) =>
        documents.ExportAsync(parameters.RecordingId, parameters.DocumentId, parameters.Format, parameters.Path, cancellationToken);
}
