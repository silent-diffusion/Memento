namespace Memento.Generation.Generation;

/// <summary>
/// Reduce in code, not with the model (ARCHITECTURE.md §8 step 3; the spike's LLM reduce added duplicates, disorder and
/// a quarter of the runtime): candidates from all chunks are merged by citation proximity and word similarity, the
/// earliest citation is kept, owner and due date are filled from any copy, and the result is in time order.
/// </summary>
public static class ClaimReducer
{
    /// <summary>Word similarity at which two claims of the same kind are the same claim wherever they are cited.</summary>
    public const double SameAnywhere = 0.34;

    /// <summary>Word similarity at which two claims cited on the same or neighbouring lines are the same claim.</summary>
    public const double SameNearby = 0.2;

    public static IReadOnlyList<Claim> Reduce(IEnumerable<Claim> claims)
    {
        ArgumentNullException.ThrowIfNull(claims);
        var kept = new List<(Claim Claim, HashSet<string> Words)>();
        foreach (var claim in claims.OrderBy(c => c.Line ?? int.MaxValue).ThenBy(c => c.Chunk))
        {
            var words = TextMatch.Words(claim.Text);
            var match = kept.FirstOrDefault(k => Same(k.Claim, k.Words, claim, words));
            if (match.Claim is null)
            {
                kept.Add((claim, words));
                continue;
            }

            match.Claim.Owner ??= claim.Owner;
            match.Claim.Due ??= claim.Due;
            if (match.Claim.Line is null && claim.Line is not null)
            {
                match.Claim.Line = claim.Line;
                match.Claim.Quote = claim.Quote;
            }

            if (claim.Line is not null && !string.Equals(TextMatch.Normalize(claim.Text), TextMatch.Normalize(match.Claim.Text), StringComparison.Ordinal))
            {
                match.Claim.Alternates.Add(claim);
            }
        }

        return kept.Select(k => k.Claim).OrderBy(c => c.Line ?? int.MaxValue).ToList();
    }

    private static bool Same(Claim a, HashSet<string> aWords, Claim b, HashSet<string> bWords)
    {
        if (!string.Equals(a.Kind, b.Kind, StringComparison.Ordinal) || !string.Equals(a.Family, b.Family, StringComparison.Ordinal))
        {
            return false;
        }

        if (a.Kind == ClaimKinds.Agenda)
        {
            return a.AgendaItem == b.AgendaItem;
        }

        if (a.Kind == ClaimKinds.Quote)
        {
            return a.Line is not null && a.Line == b.Line;
        }

        var similarity = TextMatch.Jaccard(aWords, bWords);
        var nearby = a.Line is { } x && b.Line is { } y && Math.Abs(x - y) <= 1;
        return similarity >= SameAnywhere || (nearby && similarity >= SameNearby);
    }
}
