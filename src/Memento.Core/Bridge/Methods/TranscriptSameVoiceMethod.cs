using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Transcripts;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>transcript.sameVoice</c> (2.6.0): the other lines the last speaker pass heard as the same voice. Reads only.</summary>
public sealed class TranscriptSameVoiceMethod(TranscriptService transcripts) : BridgeMethod<TranscriptSameVoiceParams, TranscriptSameVoiceResult>
{
    public override string Name => BridgeMethodNames.TranscriptSameVoice;

    public override JsonTypeInfo<TranscriptSameVoiceParams> ParamsTypeInfo => ReviewBridgeJsonContext.Default.TranscriptSameVoiceParams;

    public override JsonTypeInfo<TranscriptSameVoiceResult> ResultTypeInfo => ReviewBridgeJsonContext.Default.TranscriptSameVoiceResult;

    public override Task<TranscriptSameVoiceResult> InvokeAsync(TranscriptSameVoiceParams parameters, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        return transcripts.SameVoiceAsync(parameters.RecordingId, parameters.SegmentIds, cancellationToken);
    }
}
