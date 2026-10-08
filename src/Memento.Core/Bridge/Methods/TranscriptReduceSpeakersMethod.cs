using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Transcripts;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>transcript.reduceSpeakers</c>: merges the speakers whose voices are most alike until the count is left.</summary>
public sealed class TranscriptReduceSpeakersMethod(TranscriptService transcripts) : BridgeMethod<TranscriptReduceSpeakersParams, ReduceSpeakersResult>
{
    public override string Name => BridgeMethodNames.TranscriptReduceSpeakers;

    public override JsonTypeInfo<TranscriptReduceSpeakersParams> ParamsTypeInfo => BridgeJsonContext.Default.TranscriptReduceSpeakersParams;

    public override JsonTypeInfo<ReduceSpeakersResult> ResultTypeInfo => BridgeJsonContext.Default.ReduceSpeakersResult;

    public override Task<ReduceSpeakersResult> InvokeAsync(TranscriptReduceSpeakersParams parameters, CancellationToken cancellationToken) =>
        transcripts.ReduceSpeakersAsync(parameters.RecordingId, parameters.Count, cancellationToken);
}
