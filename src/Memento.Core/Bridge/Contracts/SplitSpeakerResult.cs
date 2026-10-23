namespace Memento.Core.Bridge.Contracts;

/// <summary>
/// Result of <c>transcript.splitSpeaker</c> (2.6.0): every speaker as it is now, the speaker created, and the lines that
/// moved to it, in transcript order.
/// </summary>
public sealed record SplitSpeakerResult(IReadOnlyList<Speaker> Speakers, Speaker Created, IReadOnlyList<string> SegmentIds);
