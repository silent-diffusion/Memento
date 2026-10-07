using Memento.Core.Bridge.Contracts;

namespace Memento.Core.Transcripts;

/// <summary>
/// Gives the words of an edited segment times by spreading the segment's span over them in proportion to their
/// length (a word's share counts its characters plus one for the space). Edited words get confidence 1.
/// </summary>
public static class WordAligner
{
    public static IReadOnlyList<TranscriptWord> Realign(string text, double start, double end)
    {
        ArgumentNullException.ThrowIfNull(text);
        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0)
        {
            return [];
        }

        var span = Math.Max(0, end - start);
        var weights = words.Select(w => (double)w.Length + 1).ToArray();
        var total = weights.Sum();
        var result = new List<TranscriptWord>(words.Length);
        var at = start;
        for (var i = 0; i < words.Length; i++)
        {
            var length = span * weights[i] / total;
            var wordEnd = i == words.Length - 1 ? Math.Max(start, end) : at + length;
            result.Add(new TranscriptWord(words[i], Round(at), Round(wordEnd), 1));
            at = wordEnd;
        }

        return result;
    }

    private static double Round(double seconds) => Math.Round(seconds, 3);
}
