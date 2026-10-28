namespace Memento.AI.Ask;

/// <summary>
/// A small local search over transcript sections (2.9.0 Ask): Okapi BM25 over <see cref="AskTerms"/>, built in memory
/// for one question and discarded. Nothing is sent or written; no index file exists.
/// </summary>
public sealed class AskSearch
{
    /// <summary>Term-frequency saturation.</summary>
    public const double K1 = 1.2;

    /// <summary>Length normalisation.</summary>
    public const double B = 0.75;

    private readonly List<Dictionary<string, int>> _frequencies;
    private readonly int[] _lengths;
    private readonly Dictionary<string, int> _documentFrequency = new(StringComparer.Ordinal);
    private readonly double _averageLength;

    public AskSearch(IReadOnlyList<string> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        _frequencies = new List<Dictionary<string, int>>(documents.Count);
        _lengths = new int[documents.Count];
        for (var i = 0; i < documents.Count; i++)
        {
            var terms = AskTerms.Of(documents[i]);
            _lengths[i] = terms.Count;
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var term in terms)
            {
                counts[term] = counts.GetValueOrDefault(term) + 1;
            }

            foreach (var term in counts.Keys)
            {
                _documentFrequency[term] = _documentFrequency.GetValueOrDefault(term) + 1;
            }

            _frequencies.Add(counts);
        }

        _averageLength = documents.Count == 0 ? 0 : Math.Max(1, _lengths.Average());
    }

    public int Count => _lengths.Length;

    /// <summary>The BM25 score of every section for <paramref name="query"/>, by section index (0 when no term matches).</summary>
    public double[] Scores(string query)
    {
        var scores = new double[Count];
        var terms = AskTerms.Of(query).Distinct(StringComparer.Ordinal).ToList();
        foreach (var term in terms)
        {
            if (!_documentFrequency.TryGetValue(term, out var df))
            {
                continue;
            }

            // The "plus one" form keeps the weight positive for a term in most sections.
            var idf = Math.Log(1 + ((Count - df + 0.5) / (df + 0.5)));
            for (var i = 0; i < Count; i++)
            {
                if (!_frequencies[i].TryGetValue(term, out var tf))
                {
                    continue;
                }

                var norm = K1 * (1 - B + (B * _lengths[i] / _averageLength));
                scores[i] += idf * (tf * (K1 + 1)) / (tf + norm);
            }
        }

        return scores;
    }

    /// <summary>Sections with a positive score, best first; ties keep transcript order.</summary>
    public IReadOnlyList<int> Rank(string query)
    {
        var scores = Scores(query);
        return Enumerable.Range(0, Count).Where(i => scores[i] > 0).OrderByDescending(i => scores[i]).ThenBy(i => i).ToList();
    }
}
