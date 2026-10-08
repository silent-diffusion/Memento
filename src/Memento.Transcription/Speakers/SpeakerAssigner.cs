using Memento.Core.Bridge.Contracts;
using Memento.Core.Transcripts;
using Memento.Core.Workers;

namespace Memento.Transcription.Speakers;

/// <summary>
/// Gives each transcript segment the speaker whose turns (in the same track) overlap it most, then decides who is the
/// same person. Every (track, cluster) the diarizer found is a voice with its embedding. Little voices (less speech than
/// <c>minSpeechSeconds</c>, or <see cref="MinSpeechShare"/> of all speech if that is less: a laugh, a word, two people at
/// once) are set aside so the main voices are compared with each other and not with noise (ENGINE-NOTES.md §K). With an
/// expected count (the recording's own, else Settings'), the main voices of all tracks are clustered together until that
/// many are left (a main voice without an embedding goes into the voice with the most speech on its track first, then the
/// two that sound most alike, by cosine of the speech-weighted embeddings, are joined, again and again), and then every
/// little voice joins the main voice it sounds most like. Voices are only ever joined, never split, so fewer voices than
/// expected stay as they are. Without a count, voices of different tracks are different people (a microphone's speaker
/// and a meeting app's speaker are never merged automatically), main voices on one track at least
/// <c>joinSimilarity</c> alike are joined, and a little voice joins the main voice on its track it is at least
/// <c>foldSimilarity</c> alike to, else stays a speaker of its own. Ids <c>spk1…</c>, names "Speaker 1…" and colours 1–4
/// follow the order in which each first speaks.
/// <c>speakerConfidence</c> is the overlap-weighted, calibrated (<see cref="Calibrate"/>) confidence of the winning
/// speaker's turns times its share of the overlapping speech. A segment no turn touches keeps no speaker.
/// </summary>
public static class SpeakerAssigner
{
    /// <param name="Merged">How many voices were joined into others (to reach the count, or because they sound alike).</param>
    /// <param name="Voices">Every voice that won a line: its track, cluster, embedding and lines (<c>voices.json</c>).</param>
    public sealed record Result(IReadOnlyList<TranscriptSegment> Segments, IReadOnlyList<Speaker> Speakers, int Merged = 0, IReadOnlyList<VoiceCluster>? Voices = null);

    /// <summary>A voice is little (folded into a main voice last) below this share of all speech, or the given seconds if fewer.</summary>
    public const double MinSpeechShare = 0.05;

    /// <summary>A sherpa-onnx turn confidence of this or more counts as fully certain.</summary>
    public const double CertainScore = 0.6;

    /// <summary>A sherpa-onnx turn confidence of this or less counts as a guess.</summary>
    public const double GuessScore = 0.2;

    /// <summary>
    /// sherpa-onnx's turn confidence is a similarity score, not a probability: about 0.5–0.8 for correctly separated
    /// readers in the spike, lower when voices are close, and −2 when the track has a single cluster (nothing to
    /// confuse it with). It is mapped linearly so <see cref="GuessScore"/> is 0 and <see cref="CertainScore"/> is 1,
    /// and the single-cluster value is 1, so the UI's "uncertain below 0.7" marks only doubtful turns.
    /// </summary>
    public static double Calibrate(double score) =>
        score <= -1 ? 1 : Math.Clamp((score - GuessScore) / (CertainScore - GuessScore), 0, 1);

    /// <param name="expectedSpeakers">The recording's count (or Settings'), or <c>null</c> for Auto.</param>
    /// <param name="joinSimilarity">Auto only: voices on one track at least this alike are one person; <c>null</c> joins none.</param>
    public static Result Assign(
        IReadOnlyList<TranscriptSegment> segments,
        IReadOnlyList<DiarizedTrack> tracks,
        int? expectedSpeakers = null,
        double? joinSimilarity = null,
        double minSpeechSeconds = 0,
        double foldSimilarity = double.PositiveInfinity)
    {
        ArgumentNullException.ThrowIfNull(segments);
        ArgumentNullException.ThrowIfNull(tracks);
        var turnsByTrack = tracks.ToDictionary(t => t.TrackId, t => t.Turns, StringComparer.Ordinal);

        // Pass 1: the winning (track, speaker) of every segment.
        var winners = new Dictionary<string, (string Track, int Speaker, double Confidence)>(StringComparer.Ordinal);
        var order = new List<(string Track, int Speaker)>();
        var lines = new Dictionary<(string Track, int Speaker), List<TranscriptSegment>>();
        foreach (var segment in segments.OrderBy(s => s.Start).ThenBy(s => s.Id, StringComparer.Ordinal))
        {
            if (segment.Track is not { } track || !turnsByTrack.TryGetValue(track, out var turns))
            {
                continue;
            }

            var bySpeaker = turns
                .Select(t => (Turn: t, Overlap: Math.Min(t.End, segment.End) - Math.Max(t.Start, segment.Start)))
                .Where(o => o.Overlap > 0)
                .GroupBy(o => o.Turn.Speaker)
                .Select(g => (Speaker: g.Key, Overlap: g.Sum(o => o.Overlap), Confidence: g.Sum(o => o.Overlap * o.Turn.Confidence) / g.Sum(o => o.Overlap)))
                .OrderByDescending(g => g.Overlap)
                .ThenBy(g => g.Speaker)
                .ToList();
            if (bySpeaker.Count == 0)
            {
                continue;
            }

            var winner = bySpeaker[0];
            var share = winner.Overlap / bySpeaker.Sum(g => g.Overlap);
            var key = (track, winner.Speaker);
            winners[segment.Id] = (track, winner.Speaker, Math.Round(Math.Clamp(Calibrate(winner.Confidence) * share, 0, 1), 3));
            if (!lines.TryGetValue(key, out var list))
            {
                order.Add(key);
                lines[key] = list = [];
            }

            list.Add(segment);
        }

        // Pass 2: who is the same person (numbered in the order people first speak).
        var group = Group(order, lines, tracks, expectedSpeakers, joinSimilarity, minSpeechSeconds, foldSimilarity, out var people, out var merged);
        var speakers = Enumerable.Range(0, people)
            .Select(n => new Speaker(TranscriptSpeakers.IdFor(n + 1), TranscriptSpeakers.DefaultName(n + 1), false, TranscriptSpeakers.ColorFor(n), 0))
            .ToList();

        // Pass 3: back in the transcript's own order.
        var assigned = segments
            .Select(s => winners.TryGetValue(s.Id, out var win)
                ? s with { Speaker = speakers[group[(win.Track, win.Speaker)]].Id, SpeakerConfidence = win.Confidence }
                : s with { Speaker = null, SpeakerConfidence = null })
            .ToList();
        var voices = order
            .Select(key => VoiceOf(tracks, key) is { } voice
                ? new VoiceCluster(key.Track, key.Speaker, voice.Embedding, voice.Seconds, lines[key].Select(s => s.Id).ToList())
                : new VoiceCluster(key.Track, key.Speaker, [], 0, lines[key].Select(s => s.Id).ToList()))
            .ToList();
        return new Result(assigned, TranscriptSpeakers.WithTalkTime(speakers, assigned), merged, voices);
    }

    /// <summary>Person number per (track, speaker), numbered by the first appearance of any of their voices.</summary>
    private static Dictionary<(string Track, int Speaker), int> Group(
        List<(string Track, int Speaker)> order,
        Dictionary<(string Track, int Speaker), List<TranscriptSegment>> lines,
        IReadOnlyList<DiarizedTrack> tracks,
        int? expected,
        double? joinSimilarity,
        double minSpeechSeconds,
        double foldSimilarity,
        out int people,
        out int merged)
    {
        merged = 0;
        var clusters = order
            .Select(key => new Cluster([key], key.Track, VoiceOf(tracks, key) is { } v ? VoicePrint.From(v.Embedding, v.Seconds) : null, lines[key].Sum(s => Math.Max(0, s.End - s.Start))))
            .ToList();

        // Voices with little speech (a cough, a laugh, two people at once) are folded into the main voices last, so the
        // main voices are compared with each other and not with noise. With a count, the main voices are at least that
        // many: the ones with the most speech.
        // "Little" is relative: a short recording's voices are all short.
        var little = Math.Min(minSpeechSeconds, MinSpeechShare * clusters.Sum(c => c.Seconds));
        var main = clusters.Where(c => c.Seconds >= little).ToList();
        foreach (var extra in clusters.Except(main).OrderByDescending(c => c.Seconds).Take(Math.Max(0, (expected ?? 1) - main.Count)))
        {
            main.Add(extra);
        }

        var small = clusters.Except(main).ToList();
        if (expected is { } target && target > 0)
        {
            while (main.Count > target)
            {
                if ((Voiceless(main) ?? MostAlike(main, sameTrackOnly: false, atLeast: double.NegativeInfinity)) is not { } join)
                {
                    break;
                }

                Join(main, join.Into, join.From);
                merged++;
            }
        }
        else if (joinSimilarity is { } threshold)
        {
            while (MostAlike(main, sameTrackOnly: true, atLeast: threshold) is { } join)
            {
                Join(main, join.Into, join.From);
                merged++;
            }
        }

        foreach (var voice in small.OrderByDescending(c => c.Seconds))
        {
            var into = expected is null
                ? Closest(main, voice, sameTrackOnly: true, atLeast: foldSimilarity)
                : Closest(main, voice, sameTrackOnly: false, atLeast: double.NegativeInfinity);
            if (into is null)
            {
                main.Add(voice);
                continue;
            }

            Join(main, into, voice);
            merged++;
        }

        clusters = main;
        var person = new Dictionary<(string Track, int Speaker), int>();
        var numbered = clusters.OrderBy(c => c.Keys.Min(k => order.IndexOf(k))).ToList();
        for (var n = 0; n < numbered.Count; n++)
        {
            foreach (var key in numbered[n].Keys)
            {
                person[key] = n;
            }
        }

        people = numbered.Count;
        return person;
    }

    /// <summary>
    /// The main voice a small one belongs to: the most alike (at least <paramref name="atLeast"/>), or for a small voice
    /// without an embedding (only when reaching a count) the one with the most speech on its track. <c>null</c> keeps it as
    /// a voice of its own.
    /// </summary>
    private static Cluster? Closest(List<Cluster> main, Cluster small, bool sameTrackOnly, double atLeast)
    {
        var candidates = main.Where(c => !sameTrackOnly || c.Track == small.Track).ToList();
        if (small.Voice is null)
        {
            // Without an embedding it is only a guess: made to reach a count, never in Auto.
            return !double.IsNegativeInfinity(atLeast) ? null : candidates.OrderByDescending(c => c.Seconds).FirstOrDefault() ?? main.OrderByDescending(c => c.Seconds).FirstOrDefault();
        }

        var best = candidates
            .Where(c => c.Voice is not null)
            .Select(c => (Cluster: c, Similarity: c.Voice!.Similarity(small.Voice)))
            .OrderByDescending(c => c.Similarity)
            .FirstOrDefault();
        return best.Cluster is not null && best.Similarity >= atLeast ? best.Cluster : null;
    }

    /// <summary>The voiceless cluster with the least speech and the cluster with the most speech on its track (else anywhere).</summary>
    private static (Cluster Into, Cluster From)? Voiceless(List<Cluster> clusters)
    {
        var from = clusters.Where(c => c.Voice is null).OrderBy(c => c.Seconds).ThenByDescending(clusters.IndexOf).FirstOrDefault();
        if (from is null)
        {
            return null;
        }

        var into = clusters.Where(c => c != from && c.Track == from.Track).OrderByDescending(c => c.Seconds).FirstOrDefault()
            ?? clusters.Where(c => c != from).OrderByDescending(c => c.Seconds).FirstOrDefault();
        return into is null ? null : (into, from);
    }

    /// <summary>The two clusters whose voices are most alike (and at least <paramref name="atLeast"/>), the one with more speech first.</summary>
    private static (Cluster Into, Cluster From)? MostAlike(List<Cluster> clusters, bool sameTrackOnly, double atLeast)
    {
        (Cluster, Cluster)? best = null;
        var bestSimilarity = double.NegativeInfinity;
        for (var i = 0; i < clusters.Count; i++)
        {
            for (var j = i + 1; j < clusters.Count; j++)
            {
                var (a, b) = (clusters[i], clusters[j]);
                if (a.Voice is null || b.Voice is null || (sameTrackOnly && a.Track != b.Track))
                {
                    continue;
                }

                var similarity = a.Voice.Similarity(b.Voice);
                if (similarity >= atLeast && similarity > bestSimilarity)
                {
                    bestSimilarity = similarity;
                    best = a.Seconds >= b.Seconds ? (a, b) : (b, a);
                }
            }
        }

        return best;
    }

    private static void Join(List<Cluster> clusters, Cluster into, Cluster from)
    {
        var index = clusters.IndexOf(into);
        var voice = into.Voice is null ? from.Voice : from.Voice is null ? into.Voice : into.Voice.Combine(from.Voice);
        clusters[index] = new Cluster([.. into.Keys, .. from.Keys], into.Track, voice, into.Seconds + from.Seconds);
        clusters.Remove(from);
    }

    private static SpeakerVoice? VoiceOf(IReadOnlyList<DiarizedTrack> tracks, (string Track, int Speaker) key)
    {
        var voice = tracks.FirstOrDefault(t => t.TrackId == key.Track)?.Voices?.FirstOrDefault(v => v.Speaker == key.Speaker);
        return voice is null || voice.Embedding.Count == 0 ? null : voice;
    }

    /// <param name="Track">The track of the voice the cluster started from (joins across tracks keep the larger one's).</param>
    /// <param name="Seconds">Talk time of the lines its voices won.</param>
    private sealed record Cluster(List<(string Track, int Speaker)> Keys, string Track, VoicePrint? Voice, double Seconds);
}
