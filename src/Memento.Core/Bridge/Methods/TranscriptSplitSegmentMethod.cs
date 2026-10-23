using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Transcripts;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>transcript.splitSegment</c> (2.6.0): one line cut in two at a word, so the rest can go to another speaker.</summary>
public sealed class TranscriptSplitSegmentMethod(TranscriptService transcripts) : BridgeMethod<TranscriptSplitSegmentParams, SplitSegmentResult>
{
    public override string Name => BridgeMethodNames.TranscriptSplitSegment;

    public override JsonTypeInfo<TranscriptSplitSegmentParams> ParamsTypeInfo => ReviewBridgeJsonContext.Default.TranscriptSplitSegmentParams;

    public override JsonTypeInfo<SplitSegmentResult> ResultTypeInfo => ReviewBridgeJsonContext.Default.SplitSegmentResult;

    public override Task<SplitSegmentResult> InvokeAsync(TranscriptSplitSegmentParams parameters, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        return transcripts.SplitSegmentAsync(parameters.RecordingId, parameters.SegmentId, parameters.At, cancellationToken);
    }
}
