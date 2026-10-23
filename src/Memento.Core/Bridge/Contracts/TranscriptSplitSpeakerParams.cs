namespace Memento.Core.Bridge.Contracts;

/// <summary>Parameters of <c>transcript.splitSpeaker</c> (2.6.0): the speaker whose raw voices are grouped into two by sound.</summary>
public sealed record TranscriptSplitSpeakerParams
{
    public required string RecordingId { get; init; }

    public required string SpeakerId { get; init; }
}
