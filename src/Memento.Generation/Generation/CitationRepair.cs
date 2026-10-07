using System.Globalization;
using Memento.AI.Payload;

namespace Memento.Generation.Generation;

/// <summary>
/// Citation repair in code (ARCHITECTURE.md §8 step 3, ENGINE-NOTES.md §H): small models often quote the right words
/// but name the wrong line. A citation whose quote is not in the cited line moves to the line where the quote occurs
/// (in the same chunk first, nearest to the cited line); a quote that is not in the transcript at all, even nearly,
/// loses its citation, so the grounding validator drops the claim.
/// </summary>
public static class CitationRepair
{
    /// <summary>Share of a quote's words that must occur in a line for a near-verbatim quote to cite it.</summary>
    public const double NearQuote = 0.8;

    public static void Repair(Claim claim, TranscriptIndex transcript, TranscriptChunk? chunk)
    {
        ArgumentNullException.ThrowIfNull(claim);
        ArgumentNullException.ThrowIfNull(transcript);
        var cited = transcript.Find(claim.Line);
        if (string.IsNullOrWhiteSpace(claim.Quote))
        {
            if (cited is null && claim.Line is not null)
            {
                claim.Notes.Add(string.Create(CultureInfo.InvariantCulture, $"line {claim.Line} is not in the transcript"));
                claim.Line = null;
            }

            return;
        }

        // In the cited line, or running on from it into the next one (but not wholly inside the next one).
        var next = cited is null ? null : transcript.Find(cited.ShortId + 1);
        if (cited is not null
            && (TextMatch.QuoteIn(claim.Quote, cited.Text)
                || (TextMatch.QuoteIn(claim.Quote, cited.Text + " " + next?.Text) && !TextMatch.QuoteIn(claim.Quote, next?.Text))))
        {
            return;
        }

        var inChunk = chunk is null ? transcript.Ids : chunk.Lines.Select(l => l.ShortId).Distinct().ToList();
        var target = Nearest(claim, transcript, inChunk, e => TextMatch.QuoteIn(claim.Quote, e.Text))
            ?? Nearest(claim, transcript, transcript.Ids, e => TextMatch.QuoteIn(claim.Quote, e.Text));
        var near = false;
        if (target is null)
        {
            var best = inChunk.Select(id => transcript.Find(id)).OfType<TranscriptIndex.Entry>()
                .Select(e => (Entry: e, Coverage: TextMatch.QuoteCoverage(claim.Quote, e.Text)))
                .Where(x => x.Coverage >= NearQuote)
                .OrderByDescending(x => x.Coverage)
                .ThenBy(x => Math.Abs(x.Entry.ShortId - (claim.Line ?? x.Entry.ShortId)))
                .FirstOrDefault();
            target = best.Entry;
            near = target is not null;
        }

        if (target is null)
        {
            claim.Notes.Add("the quoted words are not in the transcript, so the citation was removed");
            claim.Line = null;
            return;
        }

        if (target.ShortId != claim.Line)
        {
            claim.Notes.Add(claim.Line is { } from
                ? string.Create(CultureInfo.InvariantCulture, $"citation moved from line {from} to line {target.ShortId}, where the quote is")
                : string.Create(CultureInfo.InvariantCulture, $"citation set to line {target.ShortId}, where the quote is"));
            claim.Line = target.ShortId;
        }

        if (near)
        {
            claim.Notes.Add("the quote is close to, but not exactly, the transcript's words");
            claim.Quote = null;
        }
    }

    private static TranscriptIndex.Entry? Nearest(Claim claim, TranscriptIndex transcript, IEnumerable<int> ids, Func<TranscriptIndex.Entry, bool> matches) =>
        ids.Select(id => transcript.Find(id)).OfType<TranscriptIndex.Entry>().Where(matches).OrderBy(e => Math.Abs(e.ShortId - (claim.Line ?? e.ShortId))).ThenBy(e => e.ShortId).FirstOrDefault();
}
