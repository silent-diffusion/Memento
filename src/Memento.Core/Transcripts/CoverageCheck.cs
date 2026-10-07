using Memento.Core.Bridge.Contracts;

namespace Memento.Core.Transcripts;

/// <summary>
/// Flags stretches where a track has speech energy but the transcript has nothing (ENGINE-NOTES.md §D: Whisper
/// sometimes drops 15 s of real speech). Speech pieces not covered by a segment of that track are joined when they are
/// at most <see cref="JoinSeconds"/> apart; a joined stretch longer than <see cref="MinGapSeconds"/> that is at least
/// half speech is a gap.
/// </summary>
public static class CoverageCheck
{
    public const double MinGapSeconds = 20;
    public const double JoinSeconds = 2;
    private const double SegmentSlack = 0.5;

    /// <param name="speech">Per track id: speech-energy regions in seconds on the recording timeline.</param>
    public static IReadOnlyList<CoverageGap> Find(
        IReadOnlyDictionary<string, IReadOnlyList<(double Start, double End)>> speech,
        IReadOnlyList<TranscriptSegment> segments,
        double minGapSeconds = MinGapSeconds)
    {
        ArgumentNullException.ThrowIfNull(speech);
        ArgumentNullException.ThrowIfNull(segments);
        var gaps = new List<CoverageGap>();
        foreach (var (track, regions) in speech)
        {
            var covered = Merge(segments
                .Where(s => s.Track == track)
                .Select(s => (Start: s.Start - SegmentSlack, End: s.End + SegmentSlack)));
            var uncovered = regions.SelectMany(r => Subtract(r, covered)).OrderBy(r => r.Start).ToList();
            var run = new List<(double Start, double End)>();
            foreach (var piece in uncovered)
            {
                if (run.Count > 0 && piece.Start - run[^1].End > JoinSeconds)
                {
                    AddIfGap(gaps, run, track, minGapSeconds);
                    run.Clear();
                }

                run.Add(piece);
            }

            AddIfGap(gaps, run, track, minGapSeconds);
        }

        return gaps.OrderBy(g => g.Start).ToList();
    }

    private static void AddIfGap(List<CoverageGap> gaps, List<(double Start, double End)> run, string track, double minGapSeconds)
    {
        if (run.Count == 0)
        {
            return;
        }

        var start = run[0].Start;
        var end = run[^1].End;
        var speech = run.Sum(r => r.End - r.Start);
        if (end - start > minGapSeconds && speech >= (end - start) / 2)
        {
            gaps.Add(new CoverageGap(Math.Round(start, 2), Math.Round(end, 2), track));
        }
    }

    private static List<(double Start, double End)> Merge(IEnumerable<(double Start, double End)> intervals)
    {
        var merged = new List<(double Start, double End)>();
        foreach (var interval in intervals.OrderBy(i => i.Start))
        {
            if (merged.Count > 0 && interval.Start <= merged[^1].End)
            {
                merged[^1] = (merged[^1].Start, Math.Max(merged[^1].End, interval.End));
            }
            else
            {
                merged.Add(interval);
            }
        }

        return merged;
    }

    private static IEnumerable<(double Start, double End)> Subtract((double Start, double End) region, List<(double Start, double End)> covered)
    {
        var at = region.Start;
        foreach (var c in covered)
        {
            if (c.End <= at)
            {
                continue;
            }

            if (c.Start >= region.End)
            {
                break;
            }

            if (c.Start > at)
            {
                yield return (at, Math.Min(c.Start, region.End));
            }

            at = Math.Max(at, c.End);
            if (at >= region.End)
            {
                yield break;
            }
        }

        if (at < region.End)
        {
            yield return (at, region.End);
        }
    }
}
