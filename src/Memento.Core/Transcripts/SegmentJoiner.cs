using Memento.Core.Bridge.Contracts;

namespace Memento.Core.Transcripts;

/// <summary>
/// Joins a transcript line with the next one of the same track (<c>transcript.joinSegments</c>, 2.6.0): the Undo of
/// <see cref="SegmentSplitter"/>, and a way to mend a line the engine cut in two. The next line in transcript order must
/// be on the same track (a mix counts as one track); otherwise nothing is joined and <see cref="Result.Refusal"/> says
/// why. The joined line keeps the first line's id, speaker, speaker confidence and <c>speakerByUser</c>; its text is both
/// texts with one space between, its words both lists in order (none when either line has none), its span from the
/// first's start to the second's end, its confidence the lower of the two. It reads as edited when either line was: the
/// original is the two originals joined, or one of them when both parts came from the same edit (a split line's parts
/// carry the same original). Two unedited lines joined stay unedited: together they say what the engine said.
/// </summary>
public static class SegmentJoiner
{
    /// <param name="Segments">The transcript's lines with the two replaced by the joined one; <c>null</c> with a <see cref="Refusal"/>.</param>
    /// <param name="Joined">The joined line.</param>
    /// <param name="RemovedId">The id of the second line, gone now.</param>
    /// <param name="Refusal">Why the lines were not joined (DESIGN.md §17), or <c>null</c>.</param>
    public sealed record Result(IReadOnlyList<TranscriptSegment>? Segments, TranscriptSegment? Joined, string? RemovedId, string? Refusal)
    {
        public static Result Refused(string why) => new(null, null, null, why);
    }

    /// <param name="now">When the join happened: the joined line's edit time when either line was edited.</param>
    public static Result Join(IReadOnlyList<TranscriptSegment> segments, string segmentId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(segments);
        ArgumentNullException.ThrowIfNull(segmentId);
        var index = -1;
        for (var i = 0; i < segments.Count; i++)
        {
            if (string.Equals(segments[i].Id, segmentId, StringComparison.Ordinal))
            {
                index = i;
                break;
            }
        }

        if (index < 0 || index == segments.Count - 1)
        {
            return Result.Refused("There is no line after this one.");
        }

        var first = segments[index];
        var second = segments[index + 1];
        if (!string.Equals(first.Track, second.Track, StringComparison.Ordinal))
        {
            return Result.Refused("The next line is on another track, so the two cannot be joined.");
        }

        var joined = first with
        {
            Start = Math.Min(first.Start, second.Start),
            End = Math.Max(first.End, second.End),
            Text = $"{first.Text.Trim()} {second.Text.Trim()}".Trim(),
            Confidence = Math.Min(first.Confidence, second.Confidence),
            Words = first.Words.Count > 0 && second.Words.Count > 0 ? [.. first.Words, .. second.Words] : [],
            Edited = EditOf(first, second, now),
        };
        var result = new List<TranscriptSegment>(segments.Count - 1);
        for (var i = 0; i < segments.Count; i++)
        {
            if (i == index)
            {
                result.Add(joined);
            }
            else if (i != index + 1)
            {
                result.Add(segments[i]);
            }
        }

        return new Result(result, joined, second.Id, null);
    }

    private static TranscriptEdit? EditOf(TranscriptSegment first, TranscriptSegment second, DateTimeOffset now)
    {
        if (first.Edited is null && second.Edited is null)
        {
            return null;
        }

        var a = first.Edited?.Original ?? first.Text;
        var b = second.Edited?.Original ?? second.Text;
        var original = first.Edited is not null && second.Edited is not null && string.Equals(a, b, StringComparison.Ordinal) ? a : $"{a} {b}";
        return new TranscriptEdit(now, original);
    }
}
