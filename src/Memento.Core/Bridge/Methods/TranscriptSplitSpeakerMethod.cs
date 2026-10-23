using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Transcripts;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>transcript.splitSpeaker</c> (2.6.0): a speaker's raw voices grouped into two by sound; the smaller half becomes a new speaker.</summary>
public sealed class TranscriptSplitSpeakerMethod(TranscriptService transcripts) : BridgeMethod<TranscriptSplitSpeakerParams, SplitSpeakerResult>
{
    public override string Name => BridgeMethodNames.TranscriptSplitSpeaker;

    public override JsonTypeInfo<TranscriptSplitSpeakerParams> ParamsTypeInfo => ReviewBridgeJsonContext.Default.TranscriptSplitSpeakerParams;

    public override JsonTypeInfo<SplitSpeakerResult> ResultTypeInfo => ReviewBridgeJsonContext.Default.SplitSpeakerResult;

    public override Task<SplitSpeakerResult> InvokeAsync(TranscriptSplitSpeakerParams parameters, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        return transcripts.SplitSpeakerAsync(parameters.RecordingId, parameters.SpeakerId, cancellationToken);
    }
}
