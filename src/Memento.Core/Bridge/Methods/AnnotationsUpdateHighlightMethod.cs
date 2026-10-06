using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Projects;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>annotations.updateHighlight</c>: <c>highlight.id</c> names the highlight; other fields are a partial update.</summary>
public sealed class AnnotationsUpdateHighlightMethod(ProjectService projects) : BridgeMethod<HighlightParams, HighlightsResult>
{
    public override string Name => BridgeMethodNames.AnnotationsUpdateHighlight;

    public override JsonTypeInfo<HighlightParams> ParamsTypeInfo => BridgeJsonContext.Default.HighlightParams;

    public override JsonTypeInfo<HighlightsResult> ResultTypeInfo => BridgeJsonContext.Default.HighlightsResult;

    public override async Task<HighlightsResult> InvokeAsync(HighlightParams parameters, CancellationToken cancellationToken)
    {
        return new HighlightsResult(await projects.UpdateHighlightAsync(parameters.RecordingId, parameters.Highlight, cancellationToken));
    }
}
