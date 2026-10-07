using Memento.AI.Payload;
using Memento.Core.Bridge.Contracts;

namespace Memento.Generation.Generation;

/// <summary>
/// Agenda coverage that does not hang on one yes/no answer per chunk (the local model sometimes answered "not
/// discussed" for a topic the chair had just announced): each agenda item is matched in code against the transcript
/// lines and against the statements the other map passes cited (their text and quote), and every line that names
/// most of the item's words becomes a candidate claim for the item. Candidates go through the same verifier as the
/// model's own answer, so a keyword match alone never marks an item discussed.
/// </summary>
public static class AgendaMatcher
{
    /// <summary>Candidate lines kept per agenda item: the first match (where the topic starts) and the best ones after it.</summary>
    public const int PerItem = 3;

    /// <param name="agenda">The agenda, in order (claims number items from 1).</param>
    /// <param name="lines">The transcript lines with their chunk.</param>
    /// <param name="cited">Claims of the other map passes (summary points, decisions, actions…).</param>
    public static IReadOnlyList<Claim> Candidates(IReadOnlyList<AgendaItem> agenda, IReadOnlyList<(TranscriptLine Line, int Chunk)> lines, IReadOnlyList<Claim> cited)
    {
        ArgumentNullException.ThrowIfNull(agenda);
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(cited);
        var byId = lines.ToDictionary(l => l.Line.ShortId);
        var result = new List<Claim>();
        for (var i = 0; i < agenda.Count; i++)
        {
            var words = TextMatch.Words(agenda[i].Text);
            if (words.Count == 0)
            {
                continue;
            }

            var scored = new Dictionary<int, int>();
            foreach (var (line, _) in lines)
            {
                Score(scored, line.ShortId, words, TextMatch.Words(line.Text));
            }

            foreach (var claim in cited)
            {
                if (claim.Line is { } id && byId.ContainsKey(id) && claim.Kind != ClaimKinds.Agenda)
                {
                    var claimWords = TextMatch.Words(claim.Text);
                    claimWords.UnionWith(TextMatch.Words(claim.Quote));
                    Score(scored, id, words, claimWords);
                }
            }

            var needed = Needed(words.Count);
            var matches = scored.Where(s => s.Value >= needed).OrderBy(s => s.Key).ToList();
            if (matches.Count == 0)
            {
                continue;
            }

            var picked = new List<int> { matches[0].Key };
            picked.AddRange(matches.Skip(1).OrderByDescending(m => m.Value).ThenBy(m => m.Key).Take(PerItem - 1).Select(m => m.Key));
            foreach (var id in picked.Order())
            {
                var (line, chunk) = byId[id];
                var claim = new Claim
                {
                    Family = ModuleTask.AgendaCoverage,
                    Kind = ClaimKinds.Agenda,
                    Text = string.Empty,
                    AgendaItem = i + 1,
                    Line = id,
                    Quote = line.Text,
                    Chunk = chunk,
                };
                claim.Notes.Add("found by matching the agenda item's words");
                result.Add(claim);
            }
        }

        return result;
    }

    /// <summary>Words of the item a line must name: all of one or two, two of three, three of four or more… (half, at least two).</summary>
    public static int Needed(int itemWords) => itemWords <= 2 ? itemWords : Math.Max(2, (itemWords + 1) / 2);

    private static void Score(Dictionary<int, int> scored, int id, HashSet<string> item, HashSet<string> text)
    {
        var shared = item.Count(text.Contains);
        if (shared > 0 && (!scored.TryGetValue(id, out var best) || shared > best))
        {
            scored[id] = shared;
        }
    }
}
