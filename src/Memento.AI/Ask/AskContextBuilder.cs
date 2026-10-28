using System.Globalization;
using Memento.AI.Payload;

namespace Memento.AI.Ask;

/// <summary>
/// Fits a recording into an Ask question's budget (2.9.0). When the whole payload fits it goes as it is. Otherwise the
/// transcript is cut into sections (<see cref="TranscriptChunker"/>), ranked against the question with
/// <see cref="AskSearch"/>, and the best sections are taken with their neighbours (the lines just before and after
/// usually carry the rest of the exchange) until the budget is full; they are sent in transcript order, each tagged
/// with its place and times. The other inputs (details, agenda, participants, outline, notes) go along while they take
/// at most a quarter of the budget. When no section shares a word with the question, the opening sections are used and
/// the UI says so. Nothing is reworded: every line sent is the payload's line unchanged.
/// </summary>
public static class AskContextBuilder
{
    /// <summary>Tokens kept for each section's tag line.</summary>
    public const int TagTokens = 48;

    /// <summary>The smallest section the transcript is cut into, in tokens.</summary>
    public const int MinSectionTokens = 160;

    /// <summary>The largest section, in tokens.</summary>
    public const int MaxSectionTokens = 1500;

    /// <param name="payload">The composed payload (the transcript and the inputs allowed for this provider).</param>
    /// <param name="chapters">The recording's chapters, to name the sections.</param>
    /// <param name="query">The question, with the previous question for a follow-up.</param>
    /// <param name="counter">The provider's token counter.</param>
    /// <param name="budgetTokens">The most tokens the material may take in the request.</param>
    public static AskContext Build(ComposedPayload payload, IReadOnlyList<PayloadMarker> chapters, string query, ITokenCounter counter, int budgetTokens)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(chapters);
        ArgumentNullException.ThrowIfNull(counter);
        var lines = payload.TranscriptLines;
        var whole = counter.Count(payload.Text);
        if (whole <= budgetTokens)
        {
            return new AskContext(true, payload.Text, whole, [], 0, lines.Count, payload.Sections.Any(s => s.Kind != PayloadSectionKind.Transcript), lines.Count > 0);
        }

        var context = payload.ContextText;
        var contextTokens = context.Length == 0 ? 0 : counter.Count(context);
        var withContext = contextTokens > 0 && contextTokens <= budgetTokens / 4;
        var room = budgetTokens - (withContext ? contextTokens + 2 : 0);
        var sectionTokens = Math.Clamp(room / 6, MinSectionTokens, MaxSectionTokens);
        if (lines.Count == 0 || room < MinSectionTokens + TagTokens)
        {
            var text = withContext ? context : string.Empty;
            return new AskContext(false, text, withContext ? contextTokens : 0, [], 0, lines.Count, withContext, false);
        }

        var chunks = TranscriptChunker.Chunk(lines, new ChunkOptions(sectionTokens, counter), chapters);
        var search = new AskSearch(chunks.Select(c => c.Text).ToList());
        var ranked = search.Rank(query);
        var matched = ranked.ToHashSet();

        // The best sections first, each followed by its neighbours; with no match, the opening.
        var order = new List<int>();
        void Offer(int index)
        {
            if (index >= 0 && index < chunks.Count && !order.Contains(index))
            {
                order.Add(index);
            }
        }

        if (ranked.Count == 0)
        {
            for (var i = 0; i < chunks.Count; i++)
            {
                Offer(i);
            }
        }
        else
        {
            foreach (var index in ranked)
            {
                Offer(index);
                Offer(index - 1);
                Offer(index + 1);
            }

            // Then the rest by distance from the best section, so the budget is used.
            var best = ranked[0];
            foreach (var index in Enumerable.Range(0, chunks.Count).OrderBy(i => Math.Abs(i - best)).ThenBy(i => i))
            {
                Offer(index);
            }
        }

        var chosen = new List<TranscriptChunk>();
        var used = 0;
        foreach (var index in order)
        {
            var cost = chunks[index].Tokens + TagTokens;
            if (used + cost > room)
            {
                continue;
            }

            chosen.Add(chunks[index]);
            used += cost;
        }

        chosen.Sort((a, b) => a.Index.CompareTo(b.Index));
        var parts = new List<string>();
        if (withContext)
        {
            parts.Add(context);
        }

        parts.AddRange(chosen.Select(c => string.Create(
            CultureInfo.InvariantCulture,
            $"<transcript section=\"{c.Index + 1} of {chunks.Count}\" from=\"{PayloadComposer.Clock(c.Start)}\" to=\"{PayloadComposer.Clock(c.End)}\">\n{c.Text}\n</transcript>")));
        var excerpts = chosen.Select(c => new AskExcerpt(c.Index, c.FirstShortId, c.LastShortId, c.Start, c.End, ChapterAt(chapters, c.Start), matched.Contains(c.Index))).ToList();
        var material = string.Join("\n\n", parts);
        return new AskContext(false, material, counter.Count(material), excerpts, chunks.Count, lines.Count, withContext, ranked.Count > 0);
    }

    private static string? ChapterAt(IReadOnlyList<PayloadMarker> chapters, double start) =>
        chapters.Where(c => c.Start <= start + 0.001).OrderBy(c => c.Start).LastOrDefault()?.Title is { Length: > 0 } title ? PayloadComposer.OneLine(title) : null;
}
