using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Settings;
using Memento.Documents.Model.Modules;
using Memento.Generation.Ai;
using Memento.Generation.Documents;
using Memento.Generation.Generation;

namespace Memento.Generation.Bridge.Methods;

/// <summary><c>documents.renderHtml</c>: the viewer paper (<c>view</c>) or the print page (<c>print</c>).</summary>
public sealed class DocumentsRenderHtmlMethod(DocumentService documents) : M4Method<DocumentRenderParams, HtmlResult>
{
    public override string Name => BridgeMethodNames.DocumentsRenderHtml;

    public override JsonTypeInfo<DocumentRenderParams> ParamsTypeInfo => M4BridgeJsonContext.Default.DocumentRenderParams;

    public override JsonTypeInfo<HtmlResult> ResultTypeInfo => M4BridgeJsonContext.Default.HtmlResult;

    public override Task<HtmlResult> InvokeAsync(DocumentRenderParams parameters, CancellationToken cancellationToken) =>
        documents.RenderHtmlAsync(parameters.RecordingId, parameters.DocumentId, parameters.Mode, cancellationToken);
}
