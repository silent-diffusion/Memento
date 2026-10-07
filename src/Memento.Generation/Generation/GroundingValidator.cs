using System.Text.RegularExpressions;
using Memento.AI.Payload;

namespace Memento.Generation.Generation;

/// <summary>
/// The deterministic final gate for every provider (ARCHITECTURE.md §8, PRODUCT-SPEC "Grounded Generation"): every claim
/// cites a real transcript segment; nothing the verifier did not support survives; a decision whose cited line parks or
/// postpones it is not a decision; an action item is kept only with its commitment cited; an owner or a due date is kept
/// only when the verifier supported it and the cited span states it, and an owner only as a known person's name (a
/// participant or a transcript speaker, written as they are written); a quote must be the transcript's words;
/// participants come only from the details and the speakers. It changes claims only by dropping them, removing an owner
/// or date it cannot trace, or writing an owner as the known person's name.
/// </summary>
public static partial class GroundingValidator
{
    public const string NoCitation = "it cites no transcript moment";
    public const string NotSupported = "the cited moment does not support it";
    public const string NotChecked = "it could not be checked against the transcript";
    public const string Deferred = "the cited moment parks or postpones it, so it is not a decision";
    public const string NotVerbatim = "the quote is not the transcript's words";
    public const string OwnerNotStated = "no owner is named at the cited moment";
    public const string DueNotStated = "no date is stated at the cited moment";

    /// <summary>Longer than any name: an owner this long is the model writing something else.</summary>
    public const int MaxOwnerLength = 80;

    /// <summary>Longer than any spoken deadline ("by the end of next week").</summary>
    public const int MaxDueLength = 80;

    private static readonly HashSet<string> DueStopWords = new(StringComparer.Ordinal)
    {
        "by", "the", "on", "at", "before", "until", "till", "end", "of", "next", "this", "latest", "in", "a", "an", "to", "due",
    };

    /// <summary>Decides whether <paramref name="claim"/> is kept; normalises or removes its owner and due date.</summary>
    /// <param name="people">Names an owner may be: the participants and named speakers, exactly as written.</param>
    public static void Validate(Claim claim, TranscriptIndex transcript, IReadOnlyList<string> people)
    {
        ArgumentNullException.ThrowIfNull(claim);
        ArgumentNullException.ThrowIfNull(transcript);
        ArgumentNullException.ThrowIfNull(people);
        claim.Kept = false;
        var cited = transcript.Find(claim.Line);
        if (cited is null)
        {
            claim.DropReason = NoCitation;
            return;
        }

        if (claim.Kind == ClaimKinds.Quote)
        {
            if (!TextMatch.QuoteIn(claim.Quote, cited.Text))
            {
                claim.DropReason = NotVerbatim;
                return;
            }

            claim.Kept = true;
            return;
        }

        if (claim.Verdict != Verdicts.Supported)
        {
            claim.DropReason = claim.Verdict == Verdicts.Unsupported ? NotSupported : NotChecked;
            return;
        }

        if (claim.Kind == ClaimKinds.Decision && DeferralMarker().IsMatch(cited.Text))
        {
            claim.DropReason = Deferred;
            return;
        }

        if (claim.Kind == ClaimKinds.Action)
        {
            var span = transcript.Span(cited.ShortId);
            var spanText = string.Join(' ', span.Select(e => e.Text));
            if (claim.Owner is not null)
            {
                // Only a verified owner who is a known person stays, written as the details or the transcript write
                // them: the model's own words ("Luis, send the files to …") never reach the document.
                var owner = NormalizeOwner(claim.Owner, people.Concat(transcript.Speakers).ToList());
                if (claim.OwnerVerdict != Verdicts.Supported || owner is null || !OwnerInSpan(owner, span, cited.ShortId))
                {
                    claim.Notes.Add(OwnerNotStated);
                    claim.Owner = null;
                }
                else
                {
                    claim.Owner = owner;
                }
            }

            if (claim.Due is not null && (claim.DueVerdict != Verdicts.Supported || claim.Due.Length > MaxDueLength || !DueInSpan(claim.Due, spanText)))
            {
                claim.Notes.Add(DueNotStated);
                claim.Due = null;
            }
        }

        claim.Kept = true;
    }

    /// <summary>The participants a document may list: only names from the details and the speakers.</summary>
    public static IReadOnlyList<string> Participants(IEnumerable<string> proposed, IReadOnlyList<string> people)
    {
        ArgumentNullException.ThrowIfNull(proposed);
        ArgumentNullException.ThrowIfNull(people);
        return proposed.Where(p => people.Contains(p, StringComparer.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// The owner as the transcript or the details write the person: the known person whose whole name it is (case,
    /// punctuation and spacing ignored), or the one known person whose first name it is ("Luis" → "Luis Brandt");
    /// otherwise <c>null</c>. Anything more than a name ("Luis, send the files to …") is no known person.
    /// </summary>
    public static string? NormalizeOwner(string owner, IReadOnlyList<string> people)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(people);
        var value = TextMatch.Normalize(owner);
        if (value.Length == 0 || owner.Length > MaxOwnerLength)
        {
            return null;
        }

        var known = people.Where(p => !string.IsNullOrWhiteSpace(p) && p != PayloadComposer.UnknownSpeaker)
            .Select(p => p.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var exact = known.FirstOrDefault(p => TextMatch.Normalize(p) == value);
        if (exact is not null)
        {
            return exact;
        }

        var byFirst = known.Where(p => TextMatch.Normalize(p).Split(' ')[0] == value).ToList();
        return byFirst.Count == 1 ? byFirst[0] : null;
    }

    /// <summary>The owner is named in the span (in full or by first name), or speaks the cited line or the next one.</summary>
    public static bool OwnerInSpan(string owner, IReadOnlyList<TranscriptIndex.Entry> span, int citedLine)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(span);
        var name = TextMatch.Normalize(owner);
        if (name.Length == 0)
        {
            return false;
        }

        // Named anywhere in the span, or speaking the cited line or the one after it (taking the task on: "I'll do it",
        // or "Yes" to a request). Another speaker elsewhere in the span is not the owner.
        var first = name.Split(' ')[0];
        bool Speaks(TranscriptIndex.Entry e) => TextMatch.Normalize(e.Speaker) == name || TextMatch.Normalize(e.Speaker).Split(' ')[0] == first;
        return span.Any(e => TextMatch.ContainsPhrase(e.Text, owner) || (first.Length > 2 && TextMatch.ContainsPhrase(e.Text, first)))
            || span.Any(e => (e.ShortId == citedLine || e.ShortId == citedLine + 1) && Speaks(e));
    }

    /// <summary>The due date's words ("by Friday" → friday) occur in the span.</summary>
    public static bool DueInSpan(string due, string spanText)
    {
        var words = TextMatch.Normalize(due).Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => !DueStopWords.Contains(w)).ToList();
        if (words.Count == 0)
        {
            return TextMatch.ContainsPhrase(spanText, due);
        }

        var haystack = " " + TextMatch.Normalize(spanText) + " ";
        return words.All(w => haystack.Contains(" " + w + " ", StringComparison.Ordinal) || haystack.Contains(" " + w, StringComparison.Ordinal));
    }

    [GeneratedRegex(@"\b(park(ed|ing)?|parks|not (yet )?decid\w*|undecided|revisit\w*|postpon\w*|hold off|table (it|this|that)|no decision|not (yet )?settled|leave (it|that|this) open|left open|defer(red|ring)?)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DeferralMarker();
}
