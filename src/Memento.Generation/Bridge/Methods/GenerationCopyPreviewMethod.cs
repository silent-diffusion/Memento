using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Generation.Generation;

namespace Memento.Generation.Bridge.Methods;

/// <summary><c>generation.copyPreview</c>: the preview of what will be sent, or one request of it, on the clipboard as text (2.0). Nothing is sent.</summary>
public sealed class GenerationCopyPreviewMethod(PreviewClipboard clipboard) : M4Method<GenerationCopyPreviewParams, PreviewCopyResult>
{
    public override string Name => BridgeMethodNames.GenerationCopyPreview;

    public override JsonTypeInfo<GenerationCopyPreviewParams> ParamsTypeInfo => M4BridgeJsonContext.Default.GenerationCopyPreviewParams;

    public override JsonTypeInfo<PreviewCopyResult> ResultTypeInfo => M4BridgeJsonContext.Default.PreviewCopyResult;

    public override Task<PreviewCopyResult> InvokeAsync(GenerationCopyPreviewParams parameters, CancellationToken cancellationToken) =>
        clipboard.CopyAsync(parameters.RecordingId, Template(parameters.Template), parameters.PassId, cancellationToken);
}
