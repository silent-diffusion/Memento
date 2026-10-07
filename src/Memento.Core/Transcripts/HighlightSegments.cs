using Memento.Core.Bridge.Contracts;
using Memento.Core.Projects;

namespace Memento.Core.Transcripts;

/// <summary>
/// Once a recording has a transcript, every highlight carries the id of the line at its time (BRIDGE.md M2
/// clarification 5): the segment that contains the moment, or else the nearest one.
/// </summary>
public static class HighlightSegments
{
    /// <summary>The segment at <paramref name="seconds"/>, or the nearest; <c>null</c> when there are none.</summary>
    public static string? SegmentAt(IReadOnlyList<TranscriptSegment> segments, double seconds)
    {
        ArgumentNullException.ThrowIfNull(segments);
        TranscriptSegment? best = null;
        var bestDistance = double.MaxValue;
        foreach (var segment in segments)
        {
            var distance = seconds < segment.Start ? segment.Start - seconds : seconds >= segment.End ? seconds - segment.End : 0;
            if (distance < bestDistance)
            {
                best = segment;
                bestDistance = distance;
                if (distance == 0)
                {
                    break;
                }
            }
        }

        return best?.Id;
    }

    /// <summary>Points every highlight of the recording at its line (ids change when a transcript is replaced).</summary>
    public static async Task AttachAsync(IProjectStore store, string recordingId, IReadOnlyList<TranscriptSegment> segments, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (segments.Count == 0)
        {
            return;
        }

        await store.UpdateAnnotationsAsync(
            recordingId,
            d => d with { Highlights = d.Highlights.Select(h => h with { SegmentId = SegmentAt(segments, h.AtMs / 1000.0) }).ToList() },
            cancellationToken);
    }
}
