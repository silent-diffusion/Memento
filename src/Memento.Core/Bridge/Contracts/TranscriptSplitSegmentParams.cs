namespace Memento.Core.Bridge.Contracts;

/// <summary>Parameters of <c>transcript.splitSegment</c> (2.6.0): the line to cut and where in its text (a character offset).</summary>
public sealed record TranscriptSplitSegmentParams
{
    public required string RecordingId { get; init; }

    public required string SegmentId { get; init; }

    /// <summary>A character offset into the line's text; the cut moves to the nearest word boundary.</summary>
    public required int At { get; init; }
}
