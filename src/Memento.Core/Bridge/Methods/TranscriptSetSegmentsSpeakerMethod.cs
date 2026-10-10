using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Transcripts;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>transcript.setSegmentsSpeaker</c> (2.0): several lines to one speaker in one write (selection mode).</summary>
public sealed class TranscriptSetSegmentsSpeakerMethod(TranscriptService transcripts) : BridgeMethod<TranscriptSetSegmentsSpeakerParams, SegmentsSpeakersResult>
{
    public override string Name => BridgeMethodNames.TranscriptSetSegmentsSpeaker;

    public override JsonTypeInfo<TranscriptSetSegmentsSpeakerParams> ParamsTypeInfo => ReviewBridgeJsonContext.Default.TranscriptSetSegmentsSpeakerParams;

    public override JsonTypeInfo<SegmentsSpeakersResult> ResultTypeInfo => ReviewBridgeJsonContext.Default.SegmentsSpeakersResult;

    public override Task<SegmentsSpeakersResult> InvokeAsync(TranscriptSetSegmentsSpeakerParams parameters, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        return transcripts.SetSegmentsSpeakerAsync(parameters.RecordingId, parameters.SegmentIds, parameters.SpeakerId, parameters.NewSpeakerName, cancellationToken);
    }
}
