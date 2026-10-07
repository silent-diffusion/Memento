using Memento.Core.Bridge.Contracts;
using Memento.Core.Workers;

namespace Memento.Transcription.Words;

/// <summary>
/// Builds words from Whisper tokens (ENGINE-NOTES.md §D): special tokens are dropped; a token whose text begins with
/// a space starts a new word, any other token (a sub-word or punctuation) continues the current one. A word spans its
/// first token's start to its last token's end, clamped to the segment; its confidence is the lowest token
/// probability. A segment's confidence is its lowest word confidence.
/// </summary>
public static class WordBuilder
{
    /// <summary>The words of one segment, on the recording timeline (<paramref name="offsetSeconds"/> added).</summary>
    public static IReadOnlyList<TranscriptWord> Build(RawSegment segment, double offsetSeconds)
    {
        ArgumentNullException.ThrowIfNull(segment);
        var words = new List<TranscriptWord>();
        string? text = null;
        double start = 0, end = 0, confidence = 1;
        foreach (var token in segment.Tokens)
        {
            if (token.IsSpecial || token.Text.Length == 0)
            {
                continue;
            }

            var beginsWord = token.Text[0] == ' ' || text is null;
            if (beginsWord && text is not null)
            {
                Flush();
            }

            if (beginsWord)
            {
                text = token.Text;
                start = token.Start;
                end = token.End;
                confidence = token.Probability;
            }
            else
            {
                text += token.Text;
                end = Math.Max(end, token.End);
                confidence = Math.Min(confidence, token.Probability);
            }
        }

        if (text is not null)
        {
            Flush();
        }

        return words;

        void Flush()
        {
            var w = text!.Trim();
            if (w.Length == 0)
            {
                return;
            }

            var s = Math.Clamp(start, segment.Start, Math.Max(segment.Start, segment.End));
            var e = Math.Clamp(Math.Max(end, s), s, Math.Max(s, segment.End));
            words.Add(new TranscriptWord(w, Round(s + offsetSeconds), Round(e + offsetSeconds), Round(Math.Clamp(confidence, 0, 1))));
        }
    }

    /// <summary>A finished segment on the recording timeline, with words (dropped when <paramref name="keepWords"/> is off; they still set the confidence).</summary>
    public static WorkerSegment ToSegment(RawSegment segment, double offsetSeconds, bool keepWords)
    {
        ArgumentNullException.ThrowIfNull(segment);
        var words = Build(segment, offsetSeconds);
        var confidence = words.Count > 0 ? words.Min(w => w.C) : Round(Math.Clamp(segment.MinProbability, 0, 1));
        return new WorkerSegment(
            Round(segment.Start + offsetSeconds),
            Round(Math.Max(segment.Start, segment.End) + offsetSeconds),
            segment.Text.Trim(),
            confidence,
            keepWords ? words : []);
    }

    private static double Round(double value) => Math.Round(value, 3);
}
