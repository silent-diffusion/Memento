using System.Globalization;
using System.Text;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Transcripts;

namespace Memento.Core.Export;

/// <summary>
/// SubRip subtitles from a transcript: at most two lines of <see cref="MaxLineLength"/> characters per cue, a long
/// segment split into several cues (timed from its word timestamps when it has them, otherwise in proportion to
/// the text), the speaker's name before the first cue of each segment, cues never overlapping.
/// </summary>
public static class SrtWriter
{
    public const int MaxLineLength = 42;
    public const int MaxLines = 2;

    /// <summary>Longer speaker names are shortened in the cue prefix so the words still fit.</summary>
    public const int MaxSpeakerLength = 24;

    /// <summary>A cue lasts at least this long, so a very short segment can still be read.</summary>
    private const double MinCueSeconds = 0.5;

    public static string Write(TranscriptDocument transcript)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        var names = TranscriptText.SpeakerNames(transcript);
        var text = new StringBuilder();
        var index = 0;
        var previousEnd = 0.0;
        foreach (var segment in transcript.Segments)
        {
            var speaker = segment.Speaker is { } id ? names.GetValueOrDefault(id, id) : null;
            foreach (var cue in Cues(segment, speaker))
            {
                var start = Math.Max(cue.Start, previousEnd);
                var end = Math.Max(cue.End, start + MinCueSeconds);
                previousEnd = end;
                index++;
                text.Append(index.ToString(CultureInfo.InvariantCulture)).Append(TranscriptText.NewLine);
                text.Append(Time(start)).Append(" --> ").Append(Time(end)).Append(TranscriptText.NewLine);
                foreach (var line in cue.Lines)
                {
                    text.Append(line).Append(TranscriptText.NewLine);
                }

                text.Append(TranscriptText.NewLine);
            }
        }

        return text.ToString();
    }

    /// <summary><c>hh:mm:ss,mmm</c>.</summary>
    public static string Time(double seconds)
    {
        var milliseconds = (long)Math.Round(Math.Max(0, seconds) * 1000, MidpointRounding.AwayFromZero);
        var time = TimeSpan.FromMilliseconds(milliseconds);
        return string.Create(CultureInfo.InvariantCulture, $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00},{time.Milliseconds:000}");
    }

    internal static List<Cue> Cues(TranscriptSegment segment, string? speaker)
    {
        var words = Words(segment);
        var cues = new List<Cue>();
        if (words.Count == 0)
        {
            return cues;
        }

        var name = speaker is null ? null : TranscriptText.Clean(speaker);
        if (name is { Length: > MaxSpeakerLength })
        {
            name = name[..(MaxSpeakerLength - 1)].TrimEnd() + "…";
        }

        var lines = new List<string>(MaxLines);
        var current = string.IsNullOrEmpty(name) ? string.Empty : name + ": ";
        var hasContent = false;
        var first = 0;
        var last = 0;

        void Emit()
        {
            var start = first == 0 ? segment.Start : words[first].Start;
            var end = last == words.Count - 1 ? segment.End : words[last].End;
            cues.Add(new Cue(start, Math.Max(start, end), lines.ToList()));
            lines.Clear();
        }

        for (var i = 0; i < words.Count; i++)
        {
            foreach (var piece in Pieces(words[i].Text, MaxLineLength))
            {
                var candidate = hasContent ? current + " " + piece : current + piece;
                if (candidate.Length <= MaxLineLength)
                {
                    current = candidate;
                    hasContent = true;
                    last = i;
                    continue;
                }

                if (current.Length > 0)
                {
                    lines.Add(current.TrimEnd());
                }

                if (lines.Count == MaxLines)
                {
                    Emit();
                    first = i;
                }

                current = piece;
                hasContent = true;
                last = i;
            }
        }

        if (current.Length > 0)
        {
            lines.Add(current.TrimEnd());
        }

        if (lines.Count > 0)
        {
            last = words.Count - 1;
            Emit();
        }

        return cues;
    }

    /// <summary>The segment's words with times: its word timestamps when they spell its text, otherwise spread over the segment by length.</summary>
    internal static List<TimedWord> Words(TranscriptSegment segment)
    {
        var text = TranscriptText.Clean(segment.Text);
        if (text.Length == 0)
        {
            return [];
        }

        var timed = segment.Words
            .Select(w => new TimedWord(TranscriptText.Clean(w.W), w.S, w.E))
            .Where(w => w.Text.Length > 0)
            .ToList();
        if (timed.Count > 0 && string.Equals(string.Join(' ', timed.Select(w => w.Text)), text, StringComparison.Ordinal))
        {
            return timed;
        }

        var tokens = text.Split(' ');
        var total = Math.Max(1, text.Length);
        var duration = Math.Max(0, segment.End - segment.Start);
        var words = new List<TimedWord>(tokens.Length);
        var offset = 0;
        foreach (var token in tokens)
        {
            var start = segment.Start + (duration * offset / total);
            offset += token.Length + 1;
            var end = segment.Start + (duration * Math.Min(total, offset - 1) / total);
            words.Add(new TimedWord(token, start, end));
        }

        return words;
    }

    private static IEnumerable<string> Pieces(string word, int max)
    {
        for (var i = 0; i < word.Length; i += max)
        {
            yield return word.Substring(i, Math.Min(max, word.Length - i));
        }
    }

    internal sealed record Cue(double Start, double End, IReadOnlyList<string> Lines);

    internal sealed record TimedWord(string Text, double Start, double End);
}
