using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Transcripts;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>transcript.restoreSpeakers</c>: several speakers back with their lines, in order, in one write (Undo of a reduce).</summary>
public sealed class TranscriptRestoreSpeakersMethod(TranscriptService transcripts) : BridgeMethod<TranscriptRestoreSpeakersParams, MergeSpeakersResult>
{
    public override string Name => BridgeMethodNames.TranscriptRestoreSpeakers;

    public override JsonTypeInfo<TranscriptRestoreSpeakersParams> ParamsTypeInfo => BridgeJsonContext.Default.TranscriptRestoreSpeakersParams;

    public override JsonTypeInfo<MergeSpeakersResult> ResultTypeInfo => BridgeJsonContext.Default.MergeSpeakersResult;

    public override Task<MergeSpeakersResult> InvokeAsync(TranscriptRestoreSpeakersParams parameters, CancellationToken cancellationToken) =>
        transcripts.RestoreSpeakersAsync(parameters.RecordingId, parameters.Speakers, cancellationToken);
}
