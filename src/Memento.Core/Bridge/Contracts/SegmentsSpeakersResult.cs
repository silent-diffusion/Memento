namespace Memento.Core.Bridge.Contracts;

/// <summary>Result of <c>transcript.setSegmentsSpeaker</c>: the lines as they are now, in transcript order, and the speakers.</summary>
public sealed record SegmentsSpeakersResult(IReadOnlyList<TranscriptSegment> Segments, IReadOnlyList<Speaker> Speakers);
