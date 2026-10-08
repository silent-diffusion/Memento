using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Transcripts;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>transcript.removeSpeaker</c>: removes a speaker that no line is assigned to (Undo of adding one).</summary>
public sealed class TranscriptRemoveSpeakerMethod(TranscriptService transcripts) : BridgeMethod<TranscriptRemoveSpeakerParams, SpeakersResult>
{
    public override string Name => BridgeMethodNames.TranscriptRemoveSpeaker;

    public override JsonTypeInfo<TranscriptRemoveSpeakerParams> ParamsTypeInfo => BridgeJsonContext.Default.TranscriptRemoveSpeakerParams;

    public override JsonTypeInfo<SpeakersResult> ResultTypeInfo => BridgeJsonContext.Default.SpeakersResult;

    public override async Task<SpeakersResult> InvokeAsync(TranscriptRemoveSpeakerParams parameters, CancellationToken cancellationToken) =>
        new(await transcripts.RemoveSpeakerAsync(parameters.RecordingId, parameters.SpeakerId, cancellationToken));
}
