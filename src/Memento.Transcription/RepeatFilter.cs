using System.Globalization;
using System.Text;
using Memento.Core.Bridge.Contracts;

namespace Memento.Transcription;

/// <summary>
/// Drops a known whisper.cpp failure: the engine gets stuck and writes the same line again and again ("Thank you."
/// ×12 over silence or noise). Within each track, a run of <see cref="MinRun"/> or more consecutive segments with the
/// same text (ignoring case, spacing and end punctuation) keeps its first segment and drops the rest. Two identical
/// lines in a row are left alone: people do say "Yes. Yes."
/// </summary>
public static class RepeatFilter
{
    /// <summary>The same line this many times in a row (or more) is treated as the engine looping.</summary>
    public const int MinRun = 3;

    /// <summary>One run that was cut down to its first segment.</summary>
    /// <param name="Kept">The segment that stays.</param>
    /// <param name="Dropped">How many repeats after it were removed.</param>
    /// <param name="End">Where the last removed repeat ended (seconds).</param>
    public sealed record Run(TranscriptSegment Kept, int Dropped, double End);

    public sealed record Result(IReadOnlyList<TranscriptSegment> Segments, IReadOnlyList<Run> Runs)
    {
        public int DroppedCount => Runs.Sum(r => r.Dropped);
    }

    /// <param name="segments">Segments of a pass, any order; the result keeps the input order.</param>
    public static Result Apply(IReadOnlyList<TranscriptSegment> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);
        var drop = new HashSet<TranscriptSegment>(ReferenceEqualityComparer.Instance);
        var runs = new List<Run>();
        foreach (var track in segments.GroupBy(s => s.Track ?? string.Empty, StringComparer.Ordinal))
        {
            var ordered = track.OrderBy(s => s.Start).ThenBy(s => s.End).ToList();
            var i = 0;
            while (i < ordered.Count)
            {
                var key = Normalize(ordered[i].Text);
                var j = i + 1;
                while (j < ordered.Count && key.Length > 0 && Normalize(ordered[j].Text) == key)
                {
                    j++;
                }

                if (j - i >= MinRun)
                {
                    for (var k = i + 1; k < j; k++)
                    {
                        drop.Add(ordered[k]);
                    }

                    runs.Add(new Run(ordered[i], j - i - 1, ordered[j - 1].End));
                }

                i = j;
            }
        }

        return drop.Count == 0
            ? new Result(segments, [])
            : new Result(segments.Where(s => !drop.Contains(s)).ToList(), runs.OrderBy(r => r.Kept.Start).ToList());
    }

    /// <summary>Lower case, single spaces, without the punctuation and spaces at either end.</summary>
    internal static string Normalize(string text)
    {
        var builder = new StringBuilder(text.Length);
        var space = false;
        foreach (var c in text.Trim())
        {
            if (char.IsWhiteSpace(c))
            {
                space = builder.Length > 0;
                continue;
            }

            if (space)
            {
                builder.Append(' ');
                space = false;
            }

            builder.Append(char.ToLower(c, CultureInfo.InvariantCulture));
        }

        return builder.ToString().Trim('.', ',', '!', '?', ';', ':', '…', '-', '"', '\'', ' ');
    }
}
