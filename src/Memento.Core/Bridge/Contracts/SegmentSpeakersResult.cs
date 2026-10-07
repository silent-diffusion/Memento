namespace Memento.Core.Bridge.Contracts;

/// <summary>Result of <c>transcript.setSegmentSpeaker</c>.</summary>
public sealed record SegmentSpeakersResult(TranscriptSegment Segment, IReadOnlyList<Speaker> Speakers);
