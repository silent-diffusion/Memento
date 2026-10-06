using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Projects;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>annotations.addHighlight</c>.</summary>
public sealed class AnnotationsAddHighlightMethod(ProjectService projects) : BridgeMethod<HighlightParams, HighlightsResult>
{
    public override string Name => BridgeMethodNames.AnnotationsAddHighlight;

    public override JsonTypeInfo<HighlightParams> ParamsTypeInfo => BridgeJsonContext.Default.HighlightParams;

    public override JsonTypeInfo<HighlightsResult> ResultTypeInfo => BridgeJsonContext.Default.HighlightsResult;

    public override async Task<HighlightsResult> InvokeAsync(HighlightParams parameters, CancellationToken cancellationToken)
    {
        return new HighlightsResult(await projects.AddHighlightAsync(parameters.RecordingId, parameters.Highlight, cancellationToken));
    }
}
