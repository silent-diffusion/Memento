namespace Memento.Core.Bridge.Contracts;

/// <summary>Result of <c>transcript.editSegment</c>.</summary>
public sealed record TranscriptSegmentResult(TranscriptSegment Segment, int Version);
