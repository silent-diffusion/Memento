namespace Memento.Core.Bridge.Contracts;

/// <summary>Parameters of <c>transcript.removeSpeaker</c>: removes a speaker no line is assigned to (the inverse of adding one).</summary>
public sealed record TranscriptRemoveSpeakerParams
{
    public required string RecordingId { get; init; }

    public required string SpeakerId { get; init; }
}
