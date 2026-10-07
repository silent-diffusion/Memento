namespace Memento.Core.Bridge.Contracts;

/// <summary>Parameters of <c>transcript.renameSpeaker</c>.</summary>
public sealed record TranscriptRenameSpeakerParams
{
    public required string RecordingId { get; init; }

    public required string SpeakerId { get; init; }

    public required string Name { get; init; }
}
