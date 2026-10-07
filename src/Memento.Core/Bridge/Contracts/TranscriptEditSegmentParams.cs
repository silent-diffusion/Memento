namespace Memento.Core.Bridge.Contracts;

/// <summary>Parameters of <c>transcript.editSegment</c>.</summary>
public sealed record TranscriptEditSegmentParams
{
    public required string RecordingId { get; init; }

    public required string SegmentId { get; init; }

    public required string Text { get; init; }
}
