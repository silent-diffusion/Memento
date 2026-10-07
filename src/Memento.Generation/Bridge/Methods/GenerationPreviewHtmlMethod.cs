using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Settings;
using Memento.Documents.Model.Modules;
using Memento.Generation.Ai;
using Memento.Generation.Documents;
using Memento.Generation.Generation;

namespace Memento.Generation.Bridge.Methods;

/// <summary><c>generation.previewHtml</c>: the Builder's preview paper with skeletons.</summary>
public sealed class GenerationPreviewHtmlMethod(GenerationService generation) : M4Method<GenerationPreviewHtmlParams, HtmlResult>
{
    public override string Name => BridgeMethodNames.GenerationPreviewHtml;

    public override JsonTypeInfo<GenerationPreviewHtmlParams> ParamsTypeInfo => M4BridgeJsonContext.Default.GenerationPreviewHtmlParams;

    public override JsonTypeInfo<HtmlResult> ResultTypeInfo => M4BridgeJsonContext.Default.HtmlResult;

    public override Task<HtmlResult> InvokeAsync(GenerationPreviewHtmlParams parameters, CancellationToken cancellationToken) =>
        generation.PreviewHtmlAsync(parameters.RecordingId, Template(parameters.Template), parameters.StyleId, cancellationToken);
}
