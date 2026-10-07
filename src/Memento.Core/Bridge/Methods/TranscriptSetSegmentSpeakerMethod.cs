using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Transcripts;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>transcript.setSegmentSpeaker</c>: assign a speaker, clear it (<c>null</c>), or create a new one by name.</summary>
public sealed class TranscriptSetSegmentSpeakerMethod(TranscriptService transcripts) : BridgeMethod<TranscriptSetSegmentSpeakerParams, SegmentSpeakersResult>
{
    public override string Name => BridgeMethodNames.TranscriptSetSegmentSpeaker;

    public override JsonTypeInfo<TranscriptSetSegmentSpeakerParams> ParamsTypeInfo => BridgeJsonContext.Default.TranscriptSetSegmentSpeakerParams;

    public override JsonTypeInfo<SegmentSpeakersResult> ResultTypeInfo => BridgeJsonContext.Default.SegmentSpeakersResult;

    public override Task<SegmentSpeakersResult> InvokeAsync(TranscriptSetSegmentSpeakerParams parameters, CancellationToken cancellationToken) =>
        transcripts.SetSegmentSpeakerAsync(parameters.RecordingId, parameters.SegmentId, parameters.SpeakerId, parameters.NewSpeakerName, cancellationToken);
}
