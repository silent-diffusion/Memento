using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Transcripts;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>transcript.get</c>: the transcript (or <c>null</c>), the pass's status and its failure.</summary>
public sealed class TranscriptGetMethod(TranscriptService transcripts) : BridgeMethod<RecordingIdParams, TranscriptGetResult>
{
    public override string Name => BridgeMethodNames.TranscriptGet;

    public override JsonTypeInfo<RecordingIdParams> ParamsTypeInfo => BridgeJsonContext.Default.RecordingIdParams;

    public override JsonTypeInfo<TranscriptGetResult> ResultTypeInfo => BridgeJsonContext.Default.TranscriptGetResult;

    public override Task<TranscriptGetResult> InvokeAsync(RecordingIdParams parameters, CancellationToken cancellationToken) =>
        transcripts.GetAsync(parameters.RecordingId, cancellationToken);
}
