namespace Memento.Core.Bridge.Contracts;

/// <summary>Parameters of <c>transcript.joinSegments</c> (2.6.0): the line to join with the next line of its track.</summary>
public sealed record TranscriptJoinSegmentsParams
{
    public required string RecordingId { get; init; }

    public required string SegmentId { get; init; }
}
