using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Projects;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>annotations.removeHighlight</c>.</summary>
public sealed class AnnotationsRemoveHighlightMethod(ProjectService projects) : BridgeMethod<HighlightIdParams, HighlightsResult>
{
    public override string Name => BridgeMethodNames.AnnotationsRemoveHighlight;

    public override JsonTypeInfo<HighlightIdParams> ParamsTypeInfo => BridgeJsonContext.Default.HighlightIdParams;

    public override JsonTypeInfo<HighlightsResult> ResultTypeInfo => BridgeJsonContext.Default.HighlightsResult;

    public override async Task<HighlightsResult> InvokeAsync(HighlightIdParams parameters, CancellationToken cancellationToken)
    {
        return new HighlightsResult(await projects.RemoveHighlightAsync(parameters.RecordingId, parameters.HighlightId, cancellationToken));
    }
}
