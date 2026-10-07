using System.Globalization;
using System.Text.RegularExpressions;
using Memento.Core.Bridge.Contracts;

namespace Memento.Core.Transcripts;

/// <summary>
/// Local topics (no AI): up to eight keywords and two-word phrases scored TF-IDF style. The transcript is cut into
/// one-minute chunks; a term scores by how often it is said times how few chunks it spreads over, so words said all
/// the time ("know", "going") lose to words that mark a subject. Stop words and fillers never count.
/// </summary>
public static partial class TopicExtractor
{
    public const int MaxTopics = 8;
    private const double ChunkSeconds = 60;

    private static readonly HashSet<string> StopWords = new(
        """
        a about above after again against all almost also although always am among an and another any anybody anyone anything anyway
        are aren't around as ask asked at away back be became because become been before began being below best better between big
        both but by came can can't cannot come could couldn't day days did didn't do does doesn't doing don't done down during each
        either else enough even ever every everybody everyone everything few find first for found from full further gave get gets
        getting give given go goes going gone gonna good got gotta great had hadn't half has hasn't have haven't having he he'd he'll
        he's her here here's hers herself him himself his how how's however i i'd i'll i'm i've if in into is isn't it it's its itself
        just keep kind know known last later least less let let's like likely little long look looked looking lot lots made make makes
        making many may maybe me mean means might mine more most mostly much must my myself need needs never new next no nobody none
        nor not nothing now of off often oh ok okay old on once one ones only onto or other others our ours ourselves out over own part
        perhaps put quite rather really right said same saw say saying says see seem seemed seems seen several shall she she'd she'll
        she's should shouldn't show since so some somebody someone something sometimes somewhat soon sort still such sure take taken
        tell than thank thanks that that's the their theirs them themselves then there there's these they they'd they'll they're
        they've thing things think thinking this those though thought three through thus time times to today together told too took
        toward towards two um uh under until up upon us use used using very want wanted wants was wasn't way ways we we'd we'll we're
        we've well went were weren't what what's whatever when where where's whether which while who who's whole whom whose why will
        with within without won't work would wouldn't yeah yes yet you you'd you'll you're you've your yours yourself yourselves
        actually basically literally probably definitely course anyway hmm mm mhm hey hi hello bye alright gonna wanna kinda guess
        pretty ago quite rather soon already around maybe enough else instead anyone stuff bit lot okay sorry please oh
        """.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries),
        StringComparer.Ordinal);

    /// <summary>The topics in score order, each with the first letter capitalised.</summary>
    public static IReadOnlyList<string> Extract(IReadOnlyList<TranscriptSegment> segments, int max = MaxTopics)
    {
        ArgumentNullException.ThrowIfNull(segments);
        if (segments.Count == 0)
        {
            return [];
        }

        var chunks = segments.GroupBy(s => (int)(s.Start / ChunkSeconds)).Select(g => g.ToList()).ToList();
        var termCount = new Dictionary<string, int>(StringComparer.Ordinal);
        var chunkCount = new Dictionary<string, int>(StringComparer.Ordinal);
        var surface = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        foreach (var chunk in chunks)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var segment in chunk)
            {
                string? previous = null;
                foreach (Match match in WordPattern().Matches(segment.Text))
                {
                    var raw = match.Value.TrimEnd('\'', '-');
                    var word = Normalize(raw);
                    if (word.Length < 3 || StopWords.Contains(word) || word.All(char.IsDigit))
                    {
                        previous = null;
                        continue;
                    }

                    Count(word, raw);
                    if (previous is not null)
                    {
                        Count(previous + " " + word, null);
                    }

                    previous = word;
                }
            }

            void Count(string term, string? raw)
            {
                termCount[term] = termCount.GetValueOrDefault(term) + 1;
                if (seen.Add(term))
                {
                    chunkCount[term] = chunkCount.GetValueOrDefault(term) + 1;
                }

                if (raw is not null)
                {
                    if (!surface.TryGetValue(term, out var forms))
                    {
                        surface[term] = forms = new Dictionary<string, int>(StringComparer.Ordinal);
                    }

                    forms[raw] = forms.GetValueOrDefault(raw) + 1;
                }
            }
        }

        var minCount = segments.Count < 20 ? 2 : 3;
        var n = chunks.Count;
        var scored = termCount
            .Where(t => t.Value >= (t.Key.Contains(' ', StringComparison.Ordinal) ? Math.Max(2, minCount - 1) : minCount))
            .Select(t =>
            {
                var idf = Math.Log((n + 1.0) / (chunkCount[t.Key] + 0.5)) + 0.1;
                var weight = t.Key.Contains(' ', StringComparison.Ordinal) ? 1.6 : 1.0;
                return (Term: t.Key, Score: t.Value * idf * weight);
            })
            .OrderByDescending(t => t.Score)
            .ThenBy(t => t.Term, StringComparer.Ordinal)
            .ToList();

        var chosen = new List<string>();
        foreach (var (term, _) in scored)
        {
            if (chosen.Count >= max)
            {
                break;
            }

            var parts = term.Split(' ');
            // A word already inside a chosen phrase (or a phrase around a chosen word) adds nothing.
            if (chosen.Any(c => c.Split(' ').Intersect(parts, StringComparer.Ordinal).Any()))
            {
                continue;
            }

            chosen.Add(term);
        }

        return chosen.Select(term => Label(term, surface)).ToList();
    }

    private static string Normalize(string word)
    {
        var lower = word.ToLowerInvariant();
        return lower.EndsWith("'s", StringComparison.Ordinal) ? lower[..^2] : lower;
    }

    private static string Label(string term, Dictionary<string, Dictionary<string, int>> surface)
    {
        var words = term.Split(' ').Select(w =>
        {
            // Keep a proper noun's or acronym's own spelling ("Berlin", "API"); otherwise lower case.
            if (surface.TryGetValue(w, out var forms))
            {
                var best = forms.OrderByDescending(f => f.Value).First().Key;
                if (best.Length > 1 && char.IsUpper(best[0]) && forms.Where(f => char.IsUpper(f.Key[0])).Sum(f => f.Value) * 2 > forms.Sum(f => f.Value))
                {
                    return best.EndsWith("'s", StringComparison.OrdinalIgnoreCase) ? best[..^2] : best;
                }
            }

            return w;
        }).ToList();
        var label = string.Join(' ', words);
        return char.ToUpper(label[0], CultureInfo.InvariantCulture) + label[1..];
    }

    [GeneratedRegex(@"\p{L}[\p{L}\p{N}'\-]*", RegexOptions.CultureInvariant)]
    private static partial Regex WordPattern();
}
