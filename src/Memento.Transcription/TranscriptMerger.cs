using System.Globalization;
using Memento.Core.Bridge.Contracts;

namespace Memento.Transcription;

/// <summary>Merges the per-track segments of a pass by time and numbers them <c>s0001…</c>.</summary>
public static class TranscriptMerger
{
    public static IReadOnlyList<TranscriptSegment> Merge(IEnumerable<TranscriptSegment> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);
        return segments
            .OrderBy(s => s.Start)
            .ThenBy(s => s.Track, StringComparer.Ordinal)
            .Select((s, i) => s with { Id = string.Create(CultureInfo.InvariantCulture, $"s{i + 1:0000}") })
            .ToList();
    }
}
