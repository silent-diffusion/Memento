using System.Globalization;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Workers;

namespace Memento.Transcription.Windows;

/// <summary>
/// De-duplicates at window seams: keeps the words of a window that start inside its keep range (segments straddling a
/// cut are trimmed to their kept words), and drops a first word that repeats the previous window's last word at the
/// same moment. Segments without words are kept or dropped by their start time.
/// </summary>
public static class SeamJoiner
{
    private const double SameMomentSeconds = 0.6;

    /// <param name="segments">The window's segments, on the same timeline as <paramref name="keepFrom"/>/<paramref name="keepTo"/>.</param>
    /// <param name="previousLastWord">The last word kept from the previous window, or <c>null</c>.</param>
    public static IReadOnlyList<WorkerSegment> Keep(IReadOnlyList<WorkerSegment> segments, double keepFrom, double keepTo, TranscriptWord? previousLastWord)
    {
        ArgumentNullException.ThrowIfNull(segments);
        var kept = new List<WorkerSegment>();
        var first = true;
        foreach (var segment in segments)
        {
            if (segment.Words.Count == 0)
            {
                if (segment.Start >= keepFrom && segment.Start < keepTo)
                {
                    kept.Add(segment);
                    first = false;
                }

                continue;
            }

            var words = segment.Words.Where(w => w.S >= keepFrom && w.S < keepTo).ToList();
            if (first && previousLastWord is not null && words.Count > 0 && Repeats(previousLastWord, words[0]))
            {
                words.RemoveAt(0);
            }

            if (words.Count == 0)
            {
                continue;
            }

            first = false;
            kept.Add(words.Count == segment.Words.Count
                ? segment
                : new WorkerSegment(
                    words[0].S,
                    Math.Max(words[^1].E, words[0].S),
                    string.Join(' ', words.Select(w => w.W)),
                    words.Min(w => w.C),
                    words));
        }

        return kept;
    }

    private static bool Repeats(TranscriptWord previous, TranscriptWord next) =>
        Math.Abs(previous.S - next.S) < SameMomentSeconds
        && string.Equals(Normalize(previous.W), Normalize(next.W), StringComparison.Ordinal);

    private static string Normalize(string word) =>
        new string(word.Where(char.IsLetterOrDigit).ToArray()).ToLower(CultureInfo.InvariantCulture);
}
