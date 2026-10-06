using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Recording;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>recording.markHighlight</c>: a highlight at the current recorded time.</summary>
public sealed class RecordingMarkHighlightMethod(RecordingCoordinator recordings) : BridgeMethod<RecordingMarkHighlightParams, HighlightResult>
{
    public override string Name => BridgeMethodNames.RecordingMarkHighlight;

    public override JsonTypeInfo<RecordingMarkHighlightParams> ParamsTypeInfo => BridgeJsonContext.Default.RecordingMarkHighlightParams;

    public override JsonTypeInfo<HighlightResult> ResultTypeInfo => BridgeJsonContext.Default.HighlightResult;

    public override async Task<HighlightResult> InvokeAsync(RecordingMarkHighlightParams parameters, CancellationToken cancellationToken)
    {
        return new HighlightResult(await recordings.MarkHighlightAsync(parameters.SessionId, parameters.Note, cancellationToken));
    }
}
