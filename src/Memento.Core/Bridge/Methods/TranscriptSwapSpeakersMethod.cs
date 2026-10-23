using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Transcripts;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>transcript.swapSpeakers</c> (2.6.0): two speakers trade their lines, everywhere or from a line on, in one write.</summary>
public sealed class TranscriptSwapSpeakersMethod(TranscriptService transcripts) : BridgeMethod<TranscriptSwapSpeakersParams, SegmentsSpeakersResult>
{
    public override string Name => BridgeMethodNames.TranscriptSwapSpeakers;

    public override JsonTypeInfo<TranscriptSwapSpeakersParams> ParamsTypeInfo => ReviewBridgeJsonContext.Default.TranscriptSwapSpeakersParams;

    public override JsonTypeInfo<SegmentsSpeakersResult> ResultTypeInfo => ReviewBridgeJsonContext.Default.SegmentsSpeakersResult;

    public override Task<SegmentsSpeakersResult> InvokeAsync(TranscriptSwapSpeakersParams parameters, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        return transcripts.SwapSpeakersAsync(parameters.RecordingId, parameters.SpeakerId, parameters.OtherSpeakerId, parameters.FromSegmentId, cancellationToken);
    }
}
