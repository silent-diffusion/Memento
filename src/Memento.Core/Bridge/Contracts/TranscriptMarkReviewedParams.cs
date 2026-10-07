namespace Memento.Core.Bridge.Contracts;

/// <summary>Parameters of <c>transcript.markReviewed</c>.</summary>
public sealed record TranscriptMarkReviewedParams
{
    public required string RecordingId { get; init; }

    public required bool Reviewed { get; init; }
}
