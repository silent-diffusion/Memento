using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Transcripts;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>transcript.editSegment</c>: new text, words re-aligned in proportion, the first original kept.</summary>
public sealed class TranscriptEditSegmentMethod(TranscriptService transcripts) : BridgeMethod<TranscriptEditSegmentParams, TranscriptSegmentResult>
{
    public override string Name => BridgeMethodNames.TranscriptEditSegment;

    public override JsonTypeInfo<TranscriptEditSegmentParams> ParamsTypeInfo => BridgeJsonContext.Default.TranscriptEditSegmentParams;

    public override JsonTypeInfo<TranscriptSegmentResult> ResultTypeInfo => BridgeJsonContext.Default.TranscriptSegmentResult;

    public override Task<TranscriptSegmentResult> InvokeAsync(TranscriptEditSegmentParams parameters, CancellationToken cancellationToken) =>
        transcripts.EditSegmentAsync(parameters.RecordingId, parameters.SegmentId, parameters.Text, cancellationToken);
}
