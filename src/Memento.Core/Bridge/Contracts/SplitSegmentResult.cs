namespace Memento.Core.Bridge.Contracts;

/// <summary>Result of <c>transcript.splitSegment</c> (2.6.0): the two parts in order (the first keeps the line's id), and every speaker as it is now.</summary>
public sealed record SplitSegmentResult(IReadOnlyList<TranscriptSegment> Segments, IReadOnlyList<Speaker> Speakers);
