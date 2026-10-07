using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Transcripts;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>transcript.retranscribe</c>: queues a new pass; the current transcript becomes a version when it is replaced.</summary>
public sealed class TranscriptRetranscribeMethod(TranscriptService transcripts) : BridgeMethod<TranscriptRetranscribeParams, EmptyResult>
{
    public override string Name => BridgeMethodNames.TranscriptRetranscribe;

    public override JsonTypeInfo<TranscriptRetranscribeParams> ParamsTypeInfo => BridgeJsonContext.Default.TranscriptRetranscribeParams;

    public override JsonTypeInfo<EmptyResult> ResultTypeInfo => BridgeJsonContext.Default.EmptyResult;

    public override async Task<EmptyResult> InvokeAsync(TranscriptRetranscribeParams parameters, CancellationToken cancellationToken)
    {
        await transcripts.RetranscribeAsync(parameters.RecordingId, parameters.ModelId, parameters.Language, cancellationToken);
        return new EmptyResult();
    }
}
