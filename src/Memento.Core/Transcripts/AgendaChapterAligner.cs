using Memento.Core.Bridge.Contracts;

namespace Memento.Core.Transcripts;

/// <summary>
/// Suggested chapters from the agenda (2.7.0): finds where each agenda item is talked about, keeping the agenda's order.
/// The transcript is cut into 30-second blocks; an item's evidence at a block is how often the subject words of its title
/// (<see cref="TopicExtractor.Words"/>) are said in the two minutes from there, less what its words would get spread evenly
/// over the recording, and the block must itself say one of them. Items in agenda order are then placed on blocks in
/// increasing time (each at least a minute after the one before) to get the most evidence in all, skipping items with too
/// little (fewer than <see cref="MinMentions"/> mentions, or no more than chance). Each placed item starts at the first line
/// in its block that says one of its words. The same transcript and agenda always give the same result.
/// </summary>
public static class AgendaChapterAligner
{
    /// <summary>An item is placed only where its words are said at least this often in the two minutes from its start.</summary>
    public const int MinMentions = 2;

    /// <summary>Placed items are at least this many blocks (a minute) apart.</summary>
    public const int MinGapBlocks = 2;

    /// <summary>At most this many agenda items are considered (the first ones).</summary>
    public const int MaxItems = 40;

    /// <param name="Index">The item's 0-based position in the agenda.</param>
    /// <param name="Mentions">How often its words are said in the two minutes from <paramref name="Line"/>.</param>
    public sealed record Anchor(int Index, string Title, TranscriptSegment Line, int Mentions);

    /// <summary>The agenda items found in <paramref name="lines"/> (non-blank, in time order), in agenda and time order.</summary>
    public static IReadOnlyList<Anchor> Align(IReadOnlyList<TranscriptSegment> lines, IReadOnlyList<string> items)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(items);
        if (lines.Count == 0 || items.Count == 0)
        {
            return [];
        }

        var agenda = items
            .Take(MaxItems)
            .Select((text, index) => (Index: index, Title: (text ?? string.Empty).Trim(), Words: WordsOf(text ?? string.Empty)))
            .Where(i => i.Title.Length > 0 && i.Words.Count > 0)
            .ToList();
        if (agenda.Count == 0)
        {
            return [];
        }

        var t0 = lines[0].Start;
        var span = Math.Max(0, lines.Max(s => s.End) - t0);
        var blockCount = (int)Math.Ceiling(span / ChapterSuggester.BlockSeconds) + 1;
        var lineWords = lines.Select(l => WordsOf(l.Text)).ToList();
        var blockOf = lines.Select(l => Math.Clamp((int)((l.Start - t0) / ChapterSuggester.BlockSeconds), 0, blockCount - 1)).ToList();

        // hits[i][b]: mentions of item i's words in block b.
        var hits = new int[agenda.Count][];
        for (var i = 0; i < agenda.Count; i++)
        {
            hits[i] = new int[blockCount];
            for (var l = 0; l < lines.Count; l++)
            {
                hits[i][blockOf[l]] += lineWords[l].Count(agenda[i].Words.Contains);
            }
        }

        // evidence[i][b], or null where item i cannot start at block b.
        var evidence = new double?[agenda.Count][];
        var window = ChapterSuggester.WindowBlocks;
        for (var i = 0; i < agenda.Count; i++)
        {
            evidence[i] = new double?[blockCount];
            var total = hits[i].Sum();
            var chance = total * Math.Min(window, blockCount) / (double)blockCount;
            for (var b = 0; b < blockCount; b++)
            {
                if (hits[i][b] == 0)
                {
                    continue;
                }

                var mentions = 0;
                for (var k = b; k < Math.Min(blockCount, b + window); k++)
                {
                    mentions += hits[i][k];
                }

                if (mentions >= MinMentions && mentions > chance)
                {
                    evidence[i][b] = mentions - chance;
                }
            }
        }

        // best[i][b]: the most evidence placing items 0..i in order with item i at block b (null: not possible).
        // prefix[c]: the best placement of earlier items ending at block c or before, and where it ends.
        var best = new double?[agenda.Count][];
        var from = new int[agenda.Count][];
        var prefix = new (double Value, int At)[blockCount];
        Array.Fill(prefix, (0.0, -1));
        for (var i = 0; i < agenda.Count; i++)
        {
            best[i] = new double?[blockCount];
            from[i] = new int[blockCount];
            for (var b = 0; b < blockCount; b++)
            {
                if (evidence[i][b] is not { } here)
                {
                    continue;
                }

                // Before it: nothing, or any earlier item placed at least MinGapBlocks before.
                var (before, previous) = b - MinGapBlocks >= 0 ? prefix[b - MinGapBlocks] : (0.0, -1);
                best[i][b] = before + here;
                from[i][b] = previous;
            }

            for (var c = 0; c < blockCount; c++)
            {
                var carried = c > 0 ? prefix[c - 1] : prefix[c];
                var current = prefix[c].Value >= carried.Value ? prefix[c] : carried;
                if (best[i][c] is { } value && value > current.Value)
                {
                    current = (value, (i * blockCount) + c);
                }

                prefix[c] = current;
            }
        }

        var end = -1;
        var top = 0.0;
        for (var i = 0; i < agenda.Count; i++)
        {
            for (var b = 0; b < blockCount; b++)
            {
                if (best[i][b] is { } value && value > top)
                {
                    top = value;
                    end = (i * blockCount) + b;
                }
            }
        }

        var placed = new List<(int Item, int Block)>();
        while (end >= 0)
        {
            var (i, b) = (end / blockCount, end % blockCount);
            placed.Add((i, b));
            end = from[i][b];
        }

        placed.Reverse();

        // Each item starts where the run of blocks mentioning it that holds its best window begins (not before the last).
        var anchors = new List<Anchor>();
        var floor = 0;
        foreach (var (i, found) in placed)
        {
            var b = found;
            while (b - 1 >= floor && hits[i][b - 1] > 0)
            {
                b--;
            }

            var line = Enumerable.Range(0, lines.Count).First(l => blockOf[l] == b && lineWords[l].Any(agenda[i].Words.Contains));
            var mentions = Enumerable.Range(b, Math.Min(window, blockCount - b)).Sum(k => hits[i][k]);
            anchors.Add(new Anchor(agenda[i].Index, agenda[i].Title, lines[line], mentions));
            floor = b + MinGapBlocks;
        }

        return anchors;
    }

    /// <summary>The agenda items' titles, in order (blank ones kept, so positions match the agenda).</summary>
    public static IReadOnlyList<string> Titles(Agenda? agenda) => (agenda?.Items ?? []).Select(i => i?.Text ?? string.Empty).ToList();

    private static HashSet<string> WordsOf(string text) =>
        TopicExtractor.Words(text).OfType<(string Word, string Raw)>().Select(w => w.Word).ToHashSet(StringComparer.Ordinal);
}
