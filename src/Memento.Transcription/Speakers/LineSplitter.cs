using Memento.Core.Bridge.Contracts;
using Memento.Core.Transcripts;
using Memento.Core.Workers;

namespace Memento.Transcription.Speakers;

/// <summary>
/// Cuts a fresh transcript's lines where the diarizer hears the speaker change, so a line that holds two people's words
/// ("Yeah. — So what I think is…") can get a speaker per part instead of going whole to whoever talked longest in it
/// (ROADMAP 2.6.0 item 4). Each word goes to the speaker whose turns (on the line's own track) overlap it most; a word no
/// turn touches joins the word before it (the first words, the first turn heard). Consecutive words of one speaker form a
/// run, and the line is cut between runs; a run shorter than <see cref="MinWords"/> words or <see cref="MinSeconds"/> of
/// speech is too little to be sure of and folds into the neighbouring run with more speech, so every part of a split line
/// is at least that long. The first part keeps the line's id (a highlight made while the pass ran still lands on its first
/// words) and its start; the later parts are <c>{id}-2</c>, <c>{id}-3</c>… (skipping any id the transcript already has)
/// and the last keeps the line's end; the cuts fall between the words. A part's text is its words joined by a space, its
/// confidence the lowest of its words, and its speaker is left for <see cref="SpeakerAssigner"/>. Lines without word
/// timestamps, lines the user edited and lines on a track the diarizer did not hear are never cut.
/// </summary>
public static class LineSplitter
{
    /// <summary>Every part of a split line has at least this many words; fewer is a tail too short to place.</summary>
    public const int MinWords = 2;

    /// <summary>Every part of a split line has at least this much speech (first word's start to last word's end), seconds.</summary>
    public const double MinSeconds = 0.5;

    /// <param name="Segments">The lines in transcript order, the cut ones replaced by their parts.</param>
    /// <param name="Split">How many lines were cut.</param>
    public sealed record Result(IReadOnlyList<TranscriptSegment> Segments, int Split);

    public static Result Split(IReadOnlyList<TranscriptSegment> segments, IReadOnlyList<DiarizedTrack> tracks)
    {
        ArgumentNullException.ThrowIfNull(segments);
        ArgumentNullException.ThrowIfNull(tracks);
        var turnsByTrack = tracks.ToDictionary(t => t.TrackId, t => t.Turns, StringComparer.Ordinal);
        var ids = segments.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        var result = new List<TranscriptSegment>(segments.Count);
        var split = 0;
        foreach (var segment in segments)
        {
            var parts = Parts(segment, turnsByTrack, ids);
            if (parts is null)
            {
                result.Add(segment);
                continue;
            }

            split++;
            result.AddRange(parts);
        }

        return new Result(result, split);
    }

    /// <summary>The line's parts, or <c>null</c> when it stays one line.</summary>
    private static List<TranscriptSegment>? Parts(TranscriptSegment segment, Dictionary<string, IReadOnlyList<SpeakerTurn>> turnsByTrack, HashSet<string> ids)
    {
        if (segment.Edited is not null || segment.Words.Count < 2 * MinWords || segment.Track is not { } track || !turnsByTrack.TryGetValue(track, out var turns))
        {
            return null;
        }

        // Only the turns within the line matter to its words (a track has thousands; a line a few).
        var runs = Runs(segment.Words, turns.Where(t => t.End > segment.Start && t.Start < segment.End).ToList());
        if (runs is null || runs.Count < 2)
        {
            return null;
        }

        var parts = new List<TranscriptSegment>(runs.Count);
        for (var i = 0; i < runs.Count; i++)
        {
            var words = runs[i].Words;
            var first = i == 0;
            var last = i == runs.Count - 1;
            var start = first ? segment.Start : Math.Clamp(words[0].S, segment.Start, segment.End);
            var end = last ? segment.End : Math.Clamp(words[^1].E, start, segment.End);
            parts.Add(segment with
            {
                Id = first ? segment.Id : TranscriptSegmentIds.Child(segment.Id, i + 1, ids),
                Start = start,
                End = end,
                Speaker = null,
                SpeakerConfidence = null,
                Text = string.Join(' ', words.Select(w => w.W)),
                Confidence = words.Min(w => w.C),
                Words = words,
                Edited = null,
            });
        }

        return parts;
    }

    /// <summary>
    /// The words grouped into runs of one speaker, each long enough to stand as a line; <c>null</c> when no turn touches any
    /// word.
    /// </summary>
    private static List<Run>? Runs(IReadOnlyList<TranscriptWord> words, IReadOnlyList<SpeakerTurn> turns)
    {
        var speakers = new int[words.Count];
        var known = false;
        for (var i = 0; i < words.Count; i++)
        {
            speakers[i] = SpeakerOf(words[i], turns);
            known |= speakers[i] >= 0;
        }

        if (!known)
        {
            return null;
        }

        // A word no turn touches joins the word before it; the first words join the first speaker heard.
        var firstKnown = Array.FindIndex(speakers, s => s >= 0);
        for (var i = 0; i < speakers.Length; i++)
        {
            if (speakers[i] < 0)
            {
                speakers[i] = i == 0 ? speakers[firstKnown] : speakers[i - 1];
            }
        }

        var runs = new List<Run>();
        for (var i = 0; i < words.Count; i++)
        {
            if (runs.Count > 0 && runs[^1].Speaker == speakers[i])
            {
                runs[^1].Words.Add(words[i]);
            }
            else
            {
                runs.Add(new Run(speakers[i], [words[i]]));
            }
        }

        // Runs too short to be sure of fold into the neighbour with more speech (the one before on a tie), the shortest
        // first, and runs that then share a speaker join, until every run can stand as a line.
        while (runs.Count > 1)
        {
            var shortest = runs.Where(r => !r.Stands).OrderBy(r => r.Words.Count).ThenBy(r => r.Seconds).FirstOrDefault();
            if (shortest is null)
            {
                break;
            }

            var at = runs.FindIndex(r => ReferenceEquals(r, shortest));
            var before = at > 0 ? runs[at - 1] : null;
            var after = at < runs.Count - 1 ? runs[at + 1] : null;
            var into = before is null ? after! : after is null ? before : after.Seconds > before.Seconds ? after : before;
            if (ReferenceEquals(into, before))
            {
                into.Words.AddRange(shortest.Words);
            }
            else
            {
                into.Words.InsertRange(0, shortest.Words);
            }

            runs.RemoveAt(at);
            for (var i = runs.Count - 1; i > 0; i--)
            {
                if (runs[i].Speaker == runs[i - 1].Speaker)
                {
                    runs[i - 1].Words.AddRange(runs[i].Words);
                    runs.RemoveAt(i);
                }
            }
        }

        return runs;
    }

    /// <summary>The speaker whose turns overlap the word most (the lower number on a tie), or −1 when none touches it.</summary>
    private static int SpeakerOf(TranscriptWord word, IReadOnlyList<SpeakerTurn> turns)
    {
        var best = -1;
        var bestOverlap = 0.0;
        foreach (var group in turns.GroupBy(t => t.Speaker).OrderBy(g => g.Key))
        {
            var overlap = group.Sum(t => Math.Max(0, Math.Min(t.End, word.E) - Math.Max(t.Start, word.S)));
            if (overlap > bestOverlap)
            {
                best = group.Key;
                bestOverlap = overlap;
            }
        }

        return best;
    }

    /// <summary>Consecutive words of one speaker (a class, not a record: two runs are never "equal", only the same one).</summary>
    private sealed class Run(int speaker, List<TranscriptWord> words)
    {
        public int Speaker { get; } = speaker;

        public List<TranscriptWord> Words { get; } = words;

        public double Seconds => Words[^1].E - Words[0].S;

        /// <summary>Long enough to be a line of its own.</summary>
        public bool Stands => Words.Count >= MinWords && Seconds >= MinSeconds;
    }
}
