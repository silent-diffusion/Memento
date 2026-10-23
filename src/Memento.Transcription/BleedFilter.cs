using System.Globalization;
using System.Text;
using Memento.Core.Bridge.Contracts;

namespace Memento.Transcription;

/// <summary>
/// Keeps speech the microphone heard from the speakers once (ROADMAP 2.6.0 item 11, ENGINE-NOTES.md §W). When a
/// recording has a microphone track and a system-audio (or application) track, the microphone also picks up what the
/// speakers play, so the same words land on both tracks and the speakers pass gives the copy a speaker of its own
/// (different tracks are never joined in Auto). A <b>microphone</b> line is left out when a <b>system</b> line says the
/// same words at the same time: the two overlap by at least <see cref="MinOverlapShare"/> of the shorter one, the shorter
/// has at least <see cref="MinWords"/> words or <see cref="MinSeconds"/> of speech (two people both saying "yes" are not
/// bleed), and at least <see cref="MinSimilarity"/> of the shorter line's words (lower case, punctuation stripped, accents
/// folded) are in the longer one. System lines are never dropped: system audio is the clean capture, the room microphone
/// hears it plus the room. A line the user edited is never dropped, and a recording with only one kind of track changes
/// nothing. Runs once, on a fresh pass, behind Settings › Transcription › Keep speech heard twice once.
/// </summary>
public static class BleedFilter
{
    /// <summary>The two lines must overlap in time by at least this share of the shorter one.</summary>
    public const double MinOverlapShare = 0.6;

    /// <summary>The shorter line has at least this many words, or <see cref="MinSeconds"/> of speech, to count as bleed.</summary>
    public const int MinWords = 4;

    /// <summary>The shorter line lasts at least this long, or has <see cref="MinWords"/> words, to count as bleed.</summary>
    public const double MinSeconds = 1.5;

    /// <summary>At least this share of the shorter line's words must be in the longer line.</summary>
    public const double MinSimilarity = 0.6;

    /// <summary>The track kinds (BRIDGE.md <c>AudioSource.kind</c>, the manifest's <c>sourceKind</c>) the filter tells apart.</summary>
    public const string MicrophoneKind = "microphone";

    /// <summary>Endpoint loopback: everything the PC plays.</summary>
    public const string SystemKind = "system";

    /// <summary>Process loopback: one program's audio, as clean as the system track.</summary>
    public const string ApplicationKind = "application";

    /// <summary>One microphone line left out, and the system line that says the same.</summary>
    public sealed record Dropped(TranscriptSegment Line, TranscriptSegment KeptBy);

    /// <param name="Segments">The lines kept, in the input order.</param>
    /// <param name="Dropped">The microphone lines left out, earliest first.</param>
    public sealed record Result(IReadOnlyList<TranscriptSegment> Segments, IReadOnlyList<Dropped> Dropped)
    {
        public int DroppedCount => Dropped.Count;

        /// <summary>Where the first dropped line starts (seconds), or <c>null</c> when nothing was dropped.</summary>
        public double? Start => Dropped.Count == 0 ? null : Dropped.Min(d => d.Line.Start);

        /// <summary>Where the last dropped line ends (seconds), or <c>null</c> when nothing was dropped.</summary>
        public double? End => Dropped.Count == 0 ? null : Dropped.Max(d => d.Line.End);
    }

    /// <param name="segments">Lines of a pass, any order; the result keeps the input order.</param>
    /// <param name="kindOfTrack">Each track id's source kind (<see cref="MicrophoneKind"/>, <see cref="SystemKind"/>, …); a track not listed is left alone.</param>
    public static Result Apply(IReadOnlyList<TranscriptSegment> segments, IReadOnlyDictionary<string, string> kindOfTrack)
    {
        ArgumentNullException.ThrowIfNull(segments);
        ArgumentNullException.ThrowIfNull(kindOfTrack);
        var clean = segments
            .Where(s => s.Track is { } track && kindOfTrack.GetValueOrDefault(track) is SystemKind or ApplicationKind)
            .OrderBy(s => s.Start)
            .Select(s => (Line: s, Words: Words(s.Text)))
            .ToList();
        if (clean.Count == 0)
        {
            return new Result(segments, []);
        }

        var drop = new HashSet<TranscriptSegment>(ReferenceEqualityComparer.Instance);
        var dropped = new List<Dropped>();
        foreach (var line in segments)
        {
            if (line.Edited is not null || line.Track is not { } track || kindOfTrack.GetValueOrDefault(track) != MicrophoneKind)
            {
                continue;
            }

            var words = Words(line.Text);
            if (words.Count == 0)
            {
                continue;
            }

            foreach (var (other, otherWords) in clean)
            {
                if (other.Start >= line.End)
                {
                    break;
                }

                if (other.End > line.Start && SaysTheSame(line, words, other, otherWords))
                {
                    drop.Add(line);
                    dropped.Add(new Dropped(line, other));
                    break;
                }
            }
        }

        return drop.Count == 0
            ? new Result(segments, [])
            : new Result(segments.Where(s => !drop.Contains(s)).ToList(), dropped.OrderBy(d => d.Line.Start).ToList());
    }

    /// <summary>The time, length and word rules for one pair; the microphone line is <paramref name="line"/>.</summary>
    private static bool SaysTheSame(TranscriptSegment line, IReadOnlyList<string> words, TranscriptSegment other, IReadOnlyList<string> otherWords)
    {
        var shorterSeconds = Math.Min(line.End - line.Start, other.End - other.Start);
        var overlap = Math.Min(line.End, other.End) - Math.Max(line.Start, other.Start);
        if (shorterSeconds <= 0 || overlap < MinOverlapShare * shorterSeconds)
        {
            return false;
        }

        var (shorter, longer) = words.Count <= otherWords.Count ? (words, otherWords) : (otherWords, words);
        if (shorter.Count == 0 || (shorter.Count < MinWords && shorterSeconds < MinSeconds))
        {
            return false;
        }

        var pool = new HashSet<string>(longer, StringComparer.Ordinal);
        var found = shorter.Count(w => pool.Contains(w));
        return found >= MinSimilarity * shorter.Count;
    }

    /// <summary>The words of a line: lower case, accents folded, punctuation stripped, empty ones left out.</summary>
    internal static IReadOnlyList<string> Words(string text)
    {
        var words = new List<string>();
        foreach (var raw in RepeatFilter.Normalize(text).Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var folded = new StringBuilder(raw.Length);
            // Decomposed, an accent is a combining mark of its own, so keeping letters, digits and the apostrophe
            // ("don't" stays one word) folds the accents and drops the rest of the punctuation.
            foreach (var c in raw.Normalize(NormalizationForm.FormD))
            {
                if (char.IsLetterOrDigit(c) || c == '\'')
                {
                    folded.Append(c);
                }
            }

            if (folded.Length > 0)
            {
                words.Add(folded.ToString());
            }
        }

        return words;
    }
}
