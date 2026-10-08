using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Transcripts;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>transcript.restoreSpeaker</c>: puts a speaker back with its id, name and colour and assigns lines to it (Undo).</summary>
public sealed class TranscriptRestoreSpeakerMethod(TranscriptService transcripts) : BridgeMethod<TranscriptRestoreSpeakerParams, MergeSpeakersResult>
{
    public override string Name => BridgeMethodNames.TranscriptRestoreSpeaker;

    public override JsonTypeInfo<TranscriptRestoreSpeakerParams> ParamsTypeInfo => BridgeJsonContext.Default.TranscriptRestoreSpeakerParams;

    public override JsonTypeInfo<MergeSpeakersResult> ResultTypeInfo => BridgeJsonContext.Default.MergeSpeakersResult;

    public override Task<MergeSpeakersResult> InvokeAsync(TranscriptRestoreSpeakerParams parameters, CancellationToken cancellationToken) =>
        transcripts.RestoreSpeakerAsync(parameters.RecordingId, parameters.Speaker, parameters.SegmentIds, cancellationToken);
}
