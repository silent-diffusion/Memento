using Memento.Core.Bridge.Contracts;

namespace Memento.Core.Transcripts;

/// <summary>
/// Cuts one transcript line in two where the user asks (<c>transcript.splitSegment</c>, 2.6.0), so the rest of a line
/// that holds two people's words can be given to someone else. The cut (a character offset into the line's text) is
/// moved to the nearest word boundary, and both parts keep at least one word. The first part keeps the line's id and
/// start, the second gets <see cref="TranscriptSegmentIds.Child"/> and the line's end. When the line's word timestamps
/// still match its words (they do after the engine wrote it and after an edit, which re-aligns them), the words go to
/// their parts and set the times at the cut: the first part ends with its last word, the second starts with its first.
/// Without usable words the times are split in proportion to the characters on each side. A part's confidence is the
/// lowest of its words (the line's own when there are none); speaker, speaker confidence and <c>speakerByUser</c> are
/// copied to both; an edited line's <c>edited</c> (its time and the whole original wording) goes to both parts, since
/// each came from that edit.
/// </summary>
public static class SegmentSplitter
{
    /// <param name="First">The part that keeps the line's id; <c>null</c> with a <see cref="Refusal"/>.</param>
    /// <param name="Second">The new line; <c>null</c> with a <see cref="Refusal"/>.</param>
    /// <param name="Refusal">Why the line was not cut (DESIGN.md §17), or <c>null</c>.</param>
    public sealed record Result(TranscriptSegment? First, TranscriptSegment? Second, string? Refusal)
    {
        public static Result Refused(string why) => new(null, null, why);
    }

    /// <param name="at">A character offset into <c>segment.Text</c>; the cut snaps to the nearest word boundary.</param>
    /// <param name="taken">Every id the transcript has; the second part's id is added to it.</param>
    public static Result Cut(TranscriptSegment segment, int at, ISet<string> taken)
    {
        ArgumentNullException.ThrowIfNull(segment);
        ArgumentNullException.ThrowIfNull(taken);
        var text = segment.Text;
        if (at <= 0 || at >= text.Length)
        {
            return Result.Refused("Choose a place inside the line's words.");
        }

        var tokens = Tokens(text);
        if (tokens.Count < 2)
        {
            return Result.Refused("Both parts of a split line need at least one word.");
        }

        // The cut lands on the start of the word nearest to it (never the first word: both sides keep a word).
        var cut = 1;
        for (var k = 2; k < tokens.Count; k++)
        {
            if (Math.Abs(tokens[k].Start - at) < Math.Abs(tokens[cut].Start - at))
            {
                cut = k;
            }
        }

        var firstText = text[..tokens[cut - 1].End].TrimEnd();
        var secondText = text[tokens[cut].Start..].TrimStart();
        var words = WordsMatch(segment.Words, tokens, text) ? segment.Words : null;
        var firstWords = words is null ? [] : words.Take(cut).ToList();
        var secondWords = words is null ? [] : words.Skip(cut).ToList();

        double firstEnd;
        double secondStart;
        if (words is not null)
        {
            firstEnd = Math.Clamp(firstWords[^1].E, segment.Start, segment.End);
            secondStart = Math.Clamp(secondWords[0].S, firstEnd, segment.End);
        }
        else
        {
            // No word times to go by: the time at the cut follows the share of the characters before it.
            var boundary = Math.Round(segment.Start + ((segment.End - segment.Start) * tokens[cut].Start / text.Length), 3);
            firstEnd = Math.Clamp(boundary, segment.Start, segment.End);
            secondStart = firstEnd;
        }

        var first = segment with
        {
            End = firstEnd,
            Text = firstText,
            Confidence = firstWords.Count == 0 ? segment.Confidence : firstWords.Min(w => w.C),
            Words = firstWords,
        };
        var second = segment with
        {
            Id = TranscriptSegmentIds.Child(segment.Id, 2, taken),
            Start = secondStart,
            Text = secondText,
            Confidence = secondWords.Count == 0 ? segment.Confidence : secondWords.Min(w => w.C),
            Words = secondWords,
        };
        return new Result(first, second, null);
    }

    /// <summary>The character span of every whitespace-separated word of the text, in order.</summary>
    private static List<(int Start, int End)> Tokens(string text)
    {
        var tokens = new List<(int Start, int End)>();
        var i = 0;
        while (i < text.Length)
        {
            while (i < text.Length && char.IsWhiteSpace(text[i]))
            {
                i++;
            }

            var start = i;
            while (i < text.Length && !char.IsWhiteSpace(text[i]))
            {
                i++;
            }

            if (i > start)
            {
                tokens.Add((start, i));
            }
        }

        return tokens;
    }

    /// <summary>The word timestamps still describe the text: one word each, the same words in the same order.</summary>
    private static bool WordsMatch(IReadOnlyList<TranscriptWord> words, List<(int Start, int End)> tokens, string text)
    {
        if (words.Count != tokens.Count)
        {
            return false;
        }

        for (var k = 0; k < tokens.Count; k++)
        {
            if (!string.Equals(words[k].W, text[tokens[k].Start..tokens[k].End], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}
