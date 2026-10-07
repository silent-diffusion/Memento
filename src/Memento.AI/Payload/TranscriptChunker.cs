using System.Text;
using System.Text.RegularExpressions;

namespace Memento.AI.Payload;

/// <summary>
/// Cuts transcript lines into chunks that each fit a token budget (ARCHITECTURE.md section 8, "Segment"): a new
/// chapter starts a new chunk; otherwise a chunk is filled towards the budget and ends at the strongest boundary
/// available once it is reasonably full (chapter, then topic, then speaker turn, then any segment). A single segment
/// longer than the budget is split at sentences (then words) into parts that keep its short id. Every line appears in
/// exactly one chunk, in order; nothing is dropped.
/// </summary>
public static partial class TranscriptChunker
{
    public static IReadOnlyList<TranscriptChunk> Chunk(
        IReadOnlyList<TranscriptLine> lines,
        ChunkOptions options,
        IReadOnlyList<PayloadMarker>? chapters = null,
        IReadOnlyList<PayloadMarker>? topics = null)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.BudgetTokens, 16, nameof(options));
        if (lines.Count == 0)
        {
            return [];
        }

        var counter = options.Counter;
        var budget = options.BudgetTokens;
        var fitted = new List<TranscriptLine>(lines.Count);
        foreach (var line in lines)
        {
            if (Cost(counter, line) <= budget)
            {
                fitted.Add(line);
            }
            else
            {
                fitted.AddRange(Split(line, counter, budget));
            }
        }

        var costs = fitted.Select(l => Cost(counter, l)).ToArray();
        var boundaries = new ChunkBoundary[fitted.Count + 1];
        for (var i = 1; i < fitted.Count; i++)
        {
            boundaries[i] = BoundaryBefore(fitted[i - 1], fitted[i], chapters, topics);
        }

        boundaries[fitted.Count] = ChunkBoundary.End;

        var chunks = new List<TranscriptChunk>();
        var start = 0;
        while (start < fitted.Count)
        {
            // The furthest end that fits (at least one line: every line fits on its own after splitting).
            var end = start;
            var used = 0;
            while (end < fitted.Count && used + costs[end] <= budget)
            {
                used += costs[end];
                end++;
            }

            var cut = ChooseCut(start, end, costs, boundaries, budget, options);
            chunks.Add(Build(chunks.Count, fitted, start, cut, boundaries[cut], counter));
            start = cut;
        }

        return chunks;
    }

    private static int ChooseCut(int start, int end, int[] costs, ChunkBoundary[] boundaries, int budget, ChunkOptions options)
    {
        var fill = 0;
        var fills = new int[end - start + 1];
        for (var p = start + 1; p <= end; p++)
        {
            fill += costs[p - 1];
            fills[p - start] = fill;
        }

        // A chapter that starts inside the window starts a new chunk, unless the chunk so far would be a sliver.
        for (var p = start + 1; p <= end; p++)
        {
            if (boundaries[p] == ChunkBoundary.Chapter && fills[p - start] >= options.ChapterCutFill * budget)
            {
                return p;
            }
        }

        if (end == boundaries.Length - 1)
        {
            return end;
        }

        var best = end;
        var bestStrength = boundaries[end];
        for (var p = end - 1; p > start; p--)
        {
            if (fills[p - start] < options.MinFill * budget)
            {
                break;
            }

            if (boundaries[p] > bestStrength)
            {
                best = p;
                bestStrength = boundaries[p];
            }
        }

        return best;
    }

    private static TranscriptChunk Build(int index, List<TranscriptLine> lines, int start, int end, ChunkBoundary endsAt, ITokenCounter counter)
    {
        var slice = lines.GetRange(start, end - start);
        var text = string.Join('\n', slice.Select(l => l.Rendered));
        var speakers = slice.Select(l => l.SpeakerId).OfType<string>().Distinct(StringComparer.Ordinal).ToList();
        return new TranscriptChunk(index, slice, slice[0].Start, slice.Max(l => l.End), speakers, counter.Count(text), text, endsAt);
    }

    private static ChunkBoundary BoundaryBefore(TranscriptLine previous, TranscriptLine current, IReadOnlyList<PayloadMarker>? chapters, IReadOnlyList<PayloadMarker>? topics)
    {
        if (current.ShortId == previous.ShortId)
        {
            return ChunkBoundary.WithinSegment;
        }

        if (StartsBetween(chapters, previous.Start, current.Start))
        {
            return ChunkBoundary.Chapter;
        }

        if (StartsBetween(topics, previous.Start, current.Start))
        {
            return ChunkBoundary.Topic;
        }

        return string.Equals(previous.SpeakerId, current.SpeakerId, StringComparison.Ordinal) ? ChunkBoundary.Segment : ChunkBoundary.SpeakerTurn;
    }

    private static bool StartsBetween(IReadOnlyList<PayloadMarker>? markers, double after, double atOrBefore) =>
        markers is not null && markers.Any(m => m.Start > after && m.Start <= atOrBefore);

    /// <summary>The line's tokens plus one for the line break.</summary>
    private static int Cost(ITokenCounter counter, TranscriptLine line) => counter.Count(line.Rendered) + 1;

    private static List<TranscriptLine> Split(TranscriptLine line, ITokenCounter counter, int budget)
    {
        var pieces = new List<string>();
        foreach (var sentence in SentenceEnd().Split(line.Text.Trim()))
        {
            if (Cost(counter, line with { Text = sentence, Part = 1 }) <= budget)
            {
                pieces.Add(sentence);
            }
            else
            {
                pieces.AddRange(sentence.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            }
        }

        var parts = new List<TranscriptLine>();
        var current = new StringBuilder();
        foreach (var piece in pieces)
        {
            var candidate = current.Length == 0 ? piece : current + " " + piece;
            if (current.Length > 0 && Cost(counter, line with { Text = candidate, Part = 1 }) > budget)
            {
                parts.Add(line with { Text = current.ToString(), Part = parts.Count + 1 });
                current.Clear().Append(piece);
            }
            else
            {
                current.Clear().Append(candidate);
            }

            // A single word longer than the budget (a pasted blob): cut it by characters.
            while (Cost(counter, line with { Text = current.ToString(), Part = 1 }) > budget && current.Length > 1)
            {
                var keep = current.Length / 2;
                while (keep > 1 && Cost(counter, line with { Text = current.ToString(0, keep), Part = 1 }) > budget)
                {
                    keep /= 2;
                }

                parts.Add(line with { Text = current.ToString(0, keep), Part = parts.Count + 1 });
                current.Remove(0, keep);
            }
        }

        if (current.Length > 0)
        {
            parts.Add(line with { Text = current.ToString(), Part = parts.Count + 1 });
        }

        return parts;
    }

    [GeneratedRegex(@"(?<=[.!?…])\s+", RegexOptions.CultureInvariant)]
    private static partial Regex SentenceEnd();
}
