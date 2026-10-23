using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Transcripts;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>transcript.joinSegments</c> (2.6.0): a line joined with the next line of its track (the Undo of a split).</summary>
public sealed class TranscriptJoinSegmentsMethod(TranscriptService transcripts) : BridgeMethod<TranscriptJoinSegmentsParams, JoinSegmentsResult>
{
    public override string Name => BridgeMethodNames.TranscriptJoinSegments;

    public override JsonTypeInfo<TranscriptJoinSegmentsParams> ParamsTypeInfo => ReviewBridgeJsonContext.Default.TranscriptJoinSegmentsParams;

    public override JsonTypeInfo<JoinSegmentsResult> ResultTypeInfo => ReviewBridgeJsonContext.Default.JoinSegmentsResult;

    public override Task<JoinSegmentsResult> InvokeAsync(TranscriptJoinSegmentsParams parameters, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        return transcripts.JoinSegmentsAsync(parameters.RecordingId, parameters.SegmentId, cancellationToken);
    }
}
