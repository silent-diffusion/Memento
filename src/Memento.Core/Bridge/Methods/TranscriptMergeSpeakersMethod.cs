using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Transcripts;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>transcript.mergeSpeakers</c>: every line of one speaker moves to another, which keeps its name.</summary>
public sealed class TranscriptMergeSpeakersMethod(TranscriptService transcripts) : BridgeMethod<TranscriptMergeSpeakersParams, MergeSpeakersResult>
{
    public override string Name => BridgeMethodNames.TranscriptMergeSpeakers;

    public override JsonTypeInfo<TranscriptMergeSpeakersParams> ParamsTypeInfo => BridgeJsonContext.Default.TranscriptMergeSpeakersParams;

    public override JsonTypeInfo<MergeSpeakersResult> ResultTypeInfo => BridgeJsonContext.Default.MergeSpeakersResult;

    public override Task<MergeSpeakersResult> InvokeAsync(TranscriptMergeSpeakersParams parameters, CancellationToken cancellationToken) =>
        transcripts.MergeSpeakersAsync(parameters.RecordingId, parameters.FromSpeakerId, parameters.IntoSpeakerId, cancellationToken);
}
