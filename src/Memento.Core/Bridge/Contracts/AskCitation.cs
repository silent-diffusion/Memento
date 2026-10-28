namespace Memento.Core.Bridge.Contracts;

/// <summary>A transcript line an Ask answer cites (2.9.0): its number as the model read it, the segment and when it starts.</summary>
/// <param name="Line">The line number in the answer (<c>[12]</c>), 1-based in transcript order.</param>
/// <param name="SegmentId">The transcript segment; Review opens at it.</param>
/// <param name="Start">The line's start, seconds.</param>
public sealed record AskCitation(int Line, string SegmentId, double Start);