namespace Memento.Core.Bridge.Contracts;

/// <summary>
/// Parameters of <c>transcript.setSegmentsSpeaker</c> (2.0, selection mode): several lines to one speaker in one write.
/// <see cref="NewSpeakerName"/> creates a speaker and assigns it; otherwise <see cref="SpeakerId"/> (<c>null</c>: no speaker).
/// </summary>
public sealed record TranscriptSetSegmentsSpeakerParams
{
    public required string RecordingId { get; init; }

    public required IReadOnlyList<string> SegmentIds { get; init; }

    public string? SpeakerId { get; init; }

    public string? NewSpeakerName { get; init; }
}
