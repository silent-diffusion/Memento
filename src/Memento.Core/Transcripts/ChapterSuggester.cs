using System.Globalization;
using Memento.Core.Bridge.Contracts;

namespace Memento.Core.Transcripts;

/// <summary>
/// Suggested chapters (2.0, DESIGN.md §19), found on this PC with no model: where the words said change. The transcript
/// is cut into 30-second blocks of subject words (<see cref="TopicExtractor.Words"/>; the words of the recording's
/// topics count twice), and at each block edge the two minutes before are compared with the two minutes after (cosine
/// of the word counts, as in TextTiling). A deep dip, one well below the similarity on both sides, is a subject change;
/// the deeper than average are kept, deepest first, about one per ten minutes at most and never closer than three minutes (or
/// a twentieth of the recording) to another or to either end, and each is moved to the line within 45 s that starts
/// after the longest pause or with a new speaker.
/// The first line opens the first chapter. Each chapter is titled with the phrase or word said most in it and least
/// elsewhere (a topic's own label when that topic is what it is about), never a title an earlier chapter has. The same transcript
/// and topics always give the same suggestions. Measurements: ENGINE-NOTES.md §N.
/// </summary>
public static class ChapterSuggester
{
    public const double BlockSeconds = 30;

    /// <summary>Blocks compared on each side of an edge (two minutes).</summary>
    public const int WindowBlocks = 4;

    /// <summary>Shorter recordings get no suggestions.</summary>
    public const double MinRecordingSeconds = 360;

    /// <summary>About one chapter start per this many seconds at most (rounded; at least one).</summary>
    public const double SecondsPerChapter = 600;

    public const int MaxChapters = 12;

    /// <summary>Chapters are at least this far apart (or a twentieth of the recording, when that is more).</summary>
    public const double MinSpacingSeconds = 180;

    /// <summary>A dip shallower than this is no subject change, however the others compare.</summary>
    public const double MinDepth = 0.12;

    /// <summary>A chapter start moves to the best line within this many seconds of the block edge.</summary>
    public const double SnapSeconds = 45;

    public const int MaxTitleLength = 60;

    /// <summary>Words that never title a chapter, though the topics may count them: fillers, greetings, sounds.</summary>
    private static readonly HashSet<string> TitleStopWords = new(
        ["blah", "bye", "bye-bye", "goodbye", "yep", "nope", "huh", "wow", "cool", "nice", "question", "questions", "point", "stuff", "guys", "everybody", "somebody"],
        StringComparer.Ordinal);

    public sealed record Suggestion(long AtMs, string Title, string Basis);

    public static IReadOnlyList<Suggestion> Suggest(IReadOnlyList<TranscriptSegment> segments, IReadOnlyList<string> topics)
    {
        ArgumentNullException.ThrowIfNull(segments);
        ArgumentNullException.ThrowIfNull(topics);
        var lines = segments.Where(s => !string.IsNullOrWhiteSpace(s.Text)).OrderBy(s => s.Start).ThenBy(s => s.Id, StringComparer.Ordinal).ToList();
        if (lines.Count < 20)
        {
            return [];
        }

        var t0 = lines[0].Start;
        var span = lines.Max(s => s.End) - t0;
        if (span < MinRecordingSeconds)
        {
            return [];
        }

        var topicWords = topics.SelectMany(t => TopicExtractor.Words(t).OfType<(string Word, string Raw)>().Select(w => w.Word)).ToHashSet(StringComparer.Ordinal);
        var blockCount = (int)Math.Ceiling(span / BlockSeconds) + 1;
        var blocks = Enumerable.Range(0, blockCount).Select(_ => new Dictionary<string, double>(StringComparer.Ordinal)).ToList();
        foreach (var line in lines)
        {
            var block = blocks[Math.Clamp((int)((line.Start - t0) / BlockSeconds), 0, blockCount - 1)];
            foreach (var word in TopicExtractor.Words(line.Text).OfType<(string Word, string Raw)>())
            {
                block[word.Word] = block.GetValueOrDefault(word.Word) + (topicWords.Contains(word.Word) ? 2 : 1);
            }
        }

        // Similarity across each block edge g (between blocks g-1 and g), from two blocks in on each side.
        var similarity = new double?[blockCount];
        for (var g = 2; g <= blockCount - 2; g++)
        {
            similarity[g] = Cosine(Sum(blocks, Math.Max(0, g - WindowBlocks), g), Sum(blocks, g, Math.Min(blockCount, g + WindowBlocks)));
        }

        var dips = new List<(int Edge, double Depth)>();
        for (var g = 2; g <= blockCount - 2; g++)
        {
            var here = similarity[g]!.Value;
            if ((similarity[g - 1] is { } before && before < here) || (g + 1 < blockCount && similarity[g + 1] is { } after && after < here))
            {
                continue; // Not the bottom of a dip.
            }

            dips.Add((g, Peak(similarity, g, -1) - here + (Peak(similarity, g, 1) - here)));
        }

        if (dips.Count == 0)
        {
            return [];
        }

        var mean = dips.Average(d => d.Depth);
        var cutoff = Math.Max(MinDepth, mean);
        var spacing = Math.Max(MinSpacingSeconds, span / 20);
        var most = Math.Min(MaxChapters - 1, Math.Max(1, (int)Math.Round(span / SecondsPerChapter)));
        var starts = new List<(TranscriptSegment Line, string Basis)>();
        foreach (var (edge, _) in dips.Where(d => d.Depth >= cutoff).OrderByDescending(d => d.Depth).ThenBy(d => d.Edge))
        {
            if (starts.Count >= most)
            {
                break;
            }

            var (line, basis) = Snap(lines, t0 + (edge * BlockSeconds));
            if (line.Start - t0 >= spacing / 2 && t0 + span - line.Start >= spacing / 2 && starts.All(s => Math.Abs(s.Line.Start - line.Start) >= spacing))
            {
                starts.Add((line, basis));
            }
        }

        if (starts.Count == 0)
        {
            return [];
        }

        starts.Add((lines[0], "Start of the recording"));
        starts.Sort((a, b) => a.Line.Start.CompareTo(b.Line.Start));
        var titles = Titles(lines, starts.Select(s => s.Line.Start).ToList(), topics, topicWords);
        return starts.Select((s, i) => new Suggestion((long)Math.Round(s.Line.Start * 1000), titles[i], s.Basis)).ToList();
    }

    private static Dictionary<string, double> Sum(List<Dictionary<string, double>> blocks, int from, int to)
    {
        var sum = new Dictionary<string, double>(StringComparer.Ordinal);
        for (var i = from; i < to; i++)
        {
            foreach (var (word, count) in blocks[i])
            {
                sum[word] = sum.GetValueOrDefault(word) + count;
            }
        }

        return sum;
    }

    private static double Cosine(Dictionary<string, double> a, Dictionary<string, double> b)
    {
        var dot = a.Sum(p => p.Value * b.GetValueOrDefault(p.Key));
        var norm = Math.Sqrt(a.Values.Sum(v => v * v)) * Math.Sqrt(b.Values.Sum(v => v * v));
        return norm <= 0 ? 0 : dot / norm;
    }

    /// <summary>The highest similarity reached climbing away from edge <paramref name="g"/> in one direction.</summary>
    private static double Peak(double?[] similarity, int g, int step)
    {
        var peak = similarity[g]!.Value;
        for (var i = g + step; i >= 0 && i < similarity.Length && similarity[i] is { } value && value >= peak; i += step)
        {
            peak = value;
        }

        return peak;
    }

    /// <summary>The line near <paramref name="at"/> that starts after the longest pause, or with a new speaker.</summary>
    private static (TranscriptSegment Line, string Basis) Snap(List<TranscriptSegment> lines, double at)
    {
        var best = (Index: -1, Score: double.NegativeInfinity, Pause: 0.0, NewSpeaker: false);
        for (var i = 1; i < lines.Count; i++)
        {
            var line = lines[i];
            if (Math.Abs(line.Start - at) > SnapSeconds)
            {
                continue;
            }

            var pause = Math.Max(0, line.Start - lines[i - 1].End);
            var newSpeaker = line.Speaker is not null && lines[i - 1].Speaker is not null && line.Speaker != lines[i - 1].Speaker;
            var score = (Math.Min(pause, 5) / 5) + (newSpeaker ? 0.5 : 0) - (0.3 * Math.Abs(line.Start - at) / (2 * SnapSeconds));
            if (score > best.Score)
            {
                best = (i, score, pause, newSpeaker);
            }
        }

        if (best.Index < 0)
        {
            // No line starts near the edge (a long silence): the first line after it.
            var next = lines.FindIndex(l => l.Start >= at);
            best = (next < 0 ? lines.Count - 1 : next, 0, 0, false);
        }

        var parts = new List<string> { "The subject changes" };
        if (best.Pause >= 1.5)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"{Math.Round(best.Pause):0} s pause"));
        }

        if (best.NewSpeaker)
        {
            parts.Add("new speaker");
        }

        return (lines[best.Index], string.Join(" · ", parts));
    }

    /// <summary>One title per chapter: what is said most in it and least in the others.</summary>
    private static List<string> Titles(List<TranscriptSegment> lines, List<double> starts, IReadOnlyList<string> topics, HashSet<string> topicWords)
    {
        var chapters = starts.Count;
        var counts = Enumerable.Range(0, chapters).Select(_ => new Dictionary<string, int>(StringComparer.Ordinal)).ToList();
        var surface = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        var chapter = 0;
        foreach (var line in lines)
        {
            while (chapter + 1 < chapters && line.Start >= starts[chapter + 1])
            {
                chapter++;
            }

            string? previous = null;
            foreach (var word in TopicExtractor.Words(line.Text))
            {
                if (word is not { } w)
                {
                    previous = null;
                    continue;
                }

                Count(counts[chapter], w.Word);
                if (!surface.TryGetValue(w.Word, out var forms))
                {
                    surface[w.Word] = forms = new Dictionary<string, int>(StringComparer.Ordinal);
                }

                forms[w.Raw] = forms.GetValueOrDefault(w.Raw) + 1;
                if (previous is not null)
                {
                    Count(counts[chapter], previous + " " + w.Word);
                }

                previous = w.Word;
            }
        }

        var spread = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var term in counts.SelectMany(c => c.Keys))
        {
            spread[term] = spread.GetValueOrDefault(term) + 1;
        }

        var topicTerms = topics
            .Select(t => (Label: t, Words: TopicExtractor.Words(t).OfType<(string Word, string Raw)>().Select(w => w.Word).ToList()))
            .Where(t => t.Words.Count > 0)
            .ToList();
        var titles = new List<string>();
        for (var i = 0; i < chapters; i++)
        {
            var scored = counts[i]
                .Where(p => p.Value >= (p.Key.Contains(' ', StringComparison.Ordinal) ? 2 : 3) && !p.Key.Split(' ').Any(TitleStopWords.Contains))
                .Select(p =>
                {
                    var phrase = p.Key.Contains(' ', StringComparison.Ordinal);
                    var idf = Math.Log((chapters + 1.0) / (spread[p.Key] + 0.5)) + 0.1;
                    var topical = p.Key.Split(' ').Any(topicWords.Contains);
                    // A verb form ("figuring") names a chapter worse than a thing; a topic's words name it best.
                    var verbish = !topical && p.Key.Split(' ').All(w => w.EndsWith("ing", StringComparison.Ordinal));
                    return (Term: p.Key, Score: p.Value * idf * (phrase ? 2.0 : 1.0) * (topical ? 2.5 : 1.0) * (verbish ? 0.4 : 1.0));
                })
                .OrderByDescending(p => p.Score)
                .ThenBy(p => p.Term, StringComparer.Ordinal)
                .Select(p => Title(p.Term, counts[i], topicTerms, surface))
                .Where(t => titles.All(previous => !Same(t, previous)));
            titles.Add(scored.FirstOrDefault() ?? FirstWords(lines.First(l => l.Start >= starts[i])));
        }

        return titles;
    }

    /// <summary>Two titles are the same ignoring case and a plural "s" ("Share", "Shares").</summary>
    private static bool Same(string a, string b)
    {
        static string Stem(string t) => t.ToUpperInvariant().TrimEnd('S');
        return string.Equals(Stem(a), Stem(b), StringComparison.Ordinal);
    }

    private static void Count(Dictionary<string, int> counts, string term) => counts[term] = counts.GetValueOrDefault(term) + 1;

    /// <summary>A topic's own label when the term is one of its words and the chapter says all of them; else the term.</summary>
    private static string Title(string term, Dictionary<string, int> counts, List<(string Label, List<string> Words)> topics, Dictionary<string, Dictionary<string, int>> surface)
    {
        var parts = term.Split(' ');
        foreach (var (label, words) in topics)
        {
            if (parts.Any(words.Contains) && words.All(counts.ContainsKey))
            {
                return Cut(label);
            }
        }

        return Cut(TopicExtractor.LabelOf(term, surface));
    }

    /// <summary>The first words of the line, when nothing is said often enough to name the chapter.</summary>
    private static string FirstWords(TranscriptSegment line)
    {
        var words = line.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var text = string.Join(' ', words.Take(6)).TrimEnd(',', '.', ';', ':', '!', '?');
        return Cut(words.Length > 6 ? text + "…" : text);
    }

    private static string Cut(string title)
    {
        var trimmed = title.Trim();
        return trimmed.Length <= MaxTitleLength ? trimmed : trimmed[..(MaxTitleLength - 1)].TrimEnd() + "…";
    }
}
