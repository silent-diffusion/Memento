namespace Memento.Core.Bridge.Contracts;

/// <summary>Parameters of <c>transcript.setSegmentSpeaker</c>. <see cref="NewSpeakerName"/> creates a speaker and assigns it.</summary>
public sealed record TranscriptSetSegmentSpeakerParams
{
    public required string RecordingId { get; init; }

    public required string SegmentId { get; init; }

    public string? SpeakerId { get; init; }

    public string? NewSpeakerName { get; init; }
}
