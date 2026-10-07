using System.Globalization;

namespace Memento.AI.Payload;

/// <summary>
/// One transcript line as the model reads it: <c>[12] Ana: text</c>. <see cref="ShortId"/> stands in for the
/// timestamp (about 40% fewer tokens than times); citations come back as short ids and resolve to
/// <see cref="SegmentId"/> and its times in code. A segment too long for one chunk is split into parts that share
/// the short id.
/// </summary>
/// <param name="ShortId">1-based position of the segment in the recording, unique across chunks.</param>
/// <param name="Speaker">The speaker's name exactly as in the transcript ("Unknown speaker" when unassigned).</param>
/// <param name="Part">0 for a whole segment; 1, 2, … for the parts of a split one.</param>
public sealed record TranscriptLine(int ShortId, string SegmentId, double Start, double End, string? SpeakerId, string Speaker, string Text, int Part = 0)
{
    public string Rendered => string.Create(CultureInfo.InvariantCulture, $"[{ShortId}] {Speaker}: {Text}");
}
