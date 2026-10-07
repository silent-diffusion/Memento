using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Transcripts;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>transcript.renameSpeaker</c>: changes the speaker's name once; segments refer to it by id.</summary>
public sealed class TranscriptRenameSpeakerMethod(TranscriptService transcripts) : BridgeMethod<TranscriptRenameSpeakerParams, SpeakersResult>
{
    public override string Name => BridgeMethodNames.TranscriptRenameSpeaker;

    public override JsonTypeInfo<TranscriptRenameSpeakerParams> ParamsTypeInfo => BridgeJsonContext.Default.TranscriptRenameSpeakerParams;

    public override JsonTypeInfo<SpeakersResult> ResultTypeInfo => BridgeJsonContext.Default.SpeakersResult;

    public override async Task<SpeakersResult> InvokeAsync(TranscriptRenameSpeakerParams parameters, CancellationToken cancellationToken) =>
        new(await transcripts.RenameSpeakerAsync(parameters.RecordingId, parameters.SpeakerId, parameters.Name, cancellationToken));
}
