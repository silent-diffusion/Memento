using Memento.Core.Bridge.Contracts;

namespace Memento.Core.Transcripts;

/// <summary>
/// Review's "This line and the {n} that sound the same" (<c>transcript.sameVoice</c>, 2.6.0): every line was won by one
/// raw diarizer voice, and a voice's lines are nearly always one person (ENGINE-NOTES.md §L), so when one of them is on
/// the wrong speaker the others most likely are too. <c>voices.json</c> keeps each voice with the lines it won, whatever
/// speaker they have now; this finds the other lines of the voices behind the asked lines. Reads only.
/// </summary>
public static class SameVoice
{
    /// <summary>
    /// The ids of every line that shares a voice with any of <paramref name="segmentIds"/>: the union of the lines of each
    /// voice that holds one of them, limited to the lines the transcript still has, without the asked lines, in transcript
    /// order. Empty without <paramref name="voices"/> or when no voice holds an asked line.
    /// </summary>
    public static IReadOnlyList<string> Lines(IReadOnlyList<TranscriptSegment> segments, VoicesDocument? voices, IReadOnlyCollection<string> segmentIds)
    {
        ArgumentNullException.ThrowIfNull(segments);
        ArgumentNullException.ThrowIfNull(segmentIds);
        var asked = segmentIds.ToHashSet(StringComparer.Ordinal);
        var alike = new HashSet<string>(StringComparer.Ordinal);
        foreach (var cluster in voices?.Clusters ?? [])
        {
            if (cluster.SegmentIds.Any(asked.Contains))
            {
                alike.UnionWith(cluster.SegmentIds);
            }
        }

        return segments.Where(s => alike.Contains(s.Id) && !asked.Contains(s.Id)).Select(s => s.Id).ToList();
    }
}
