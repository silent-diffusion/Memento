namespace Memento.Core.Bridge.Contracts;

/// <summary>
/// Result of <c>transcript.joinSegments</c> (2.6.0): the joined line (it keeps the first line's id), every speaker as it is
/// now, the id of the line that is gone, and the recording's highlights (one on the removed line now points at the joined one).
/// </summary>
public sealed record JoinSegmentsResult(TranscriptSegment Segment, IReadOnlyList<Speaker> Speakers, string RemovedId, IReadOnlyList<Highlight> Highlights);
