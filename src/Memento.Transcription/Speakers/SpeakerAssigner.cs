using Memento.Core.Bridge.Contracts;
using Memento.Core.Transcripts;
using Memento.Core.Workers;

namespace Memento.Transcription.Speakers;

/// <summary>
/// Gives each transcript segment the speaker whose turns (in the same track) overlap it most, then decides who is the
/// same person. Every (track, cluster) the diarizer found is a voice with its embedding. Little voices (less speech than
/// <c>minSpeechSeconds</c>, or <see cref="MinSpeechShare"/> of all speech if that is less: a laugh, a word, two people at
/// once) are set aside so the main voices are compared with each other and not with noise (ENGINE-NOTES.md §L). With an
/// expected count (the recording's own, else Settings'), the main voices of all tracks are clustered together until that
/// many are left (a main voice without an embedding goes into the voice with the most speech on its track first, then the
/// two that sound most alike, by cosine of the speech-weighted embeddings, are joined, again and again), and then every
/// little voice joins the main voice it sounds most like. Voices are only ever joined, never split, so fewer voices than
/// expected stay as they are. Without a count, voices of different tracks are different people (a microphone's speaker
/// and a meeting app's speaker are never merged automatically), main voices on one track at least
/// <c>joinSimilarity</c> alike are joined, and a little voice joins the main voice on its track it is at least
/// <c>foldSimilarity</c> alike to (any little voice with less than <c>ownSpeakerSeconds</c> of speech joins one), else stays
/// a speaker of its own. Ids <c>spk1…</c>, names "Speaker 1…" and colours 1–4
/// follow the order in which each first speaks.
/// <c>speakerConfidence</c> is the overlap-weighted, calibrated (<see cref="Calibrate"/>) confidence of the winning
/// speaker's turns times its share of the overlapping speech. A segment no turn touches keeps no speaker.
/// Lines the user placed (<see cref="SpeakerPin"/>, 2.6.0) pin the voice that won them to their person: voices pinned to
/// one label are joined before anything else is compared, no join ever puts two labels together (a count below the number
/// of labels is not reached), an unpinned voice may still join a pinned one, and a pinned line keeps its label's person
/// whatever its voice did, with confidence 1 and <c>speakerByUser</c>; a label whose lines no voice won becomes a person of
/// its own after the others.
/// </summary>
public static class SpeakerAssigner
{
    /// <param name="Merged">How many voices were joined into others (to reach the count, because they sound alike, or because they were pinned to one person).</param>
    /// <param name="Voices">Every voice that won a line: its track, cluster, embedding and lines (<c>voices.json</c>).</param>
    /// <param name="Pinned">How many raw voices were pinned to a person by the lines the user placed.</param>
    /// <param name="Names">Speaker id → the name the user gave, for every person pinned by a named label (<see cref="SpeakerNamer"/> applies them first).</param>
    public sealed record Result(
        IReadOnlyList<TranscriptSegment> Segments,
        IReadOnlyList<Speaker> Speakers,
        int Merged = 0,
        IReadOnlyList<VoiceCluster>? Voices = null,
        int Pinned = 0,
        IReadOnlyDictionary<string, string>? Names = null);

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
    /// <param name="pins">Lines the user placed before, each with its person's label (2.6.0); <c>null</c> or empty pins nothing.</param>
    public static Result Assign(
        IReadOnlyList<TranscriptSegment> segments,
        IReadOnlyList<DiarizedTrack> tracks,
        int? expectedSpeakers = null,
        double? joinSimilarity = null,
        double minSpeechSeconds = 0,
        double foldSimilarity = double.PositiveInfinity,
        double ownSpeakerSeconds = 0,
        IReadOnlyList<SpeakerPin>? pins = null)
    {
        ArgumentNullException.ThrowIfNull(segments);
        ArgumentNullException.ThrowIfNull(tracks);
        var turnsByTrack = tracks.ToDictionary(t => t.TrackId, t => t.Turns, StringComparer.Ordinal);
        var pinned = new Dictionary<string, SpeakerPin>(StringComparer.Ordinal);
        foreach (var pin in pins ?? [])
        {
            pinned.TryAdd(pin.SegmentId, pin);
        }

        // Pass 1: the winning (track, speaker) of every segment, and the label talk of every voice the user's lines pin.
        var winners = new Dictionary<string, (string Track, int Speaker, double Confidence)>(StringComparer.Ordinal);
        var order = new List<(string Track, int Speaker)>();
        var lines = new Dictionary<(string Track, int Speaker), List<TranscriptSegment>>();
        var labelTalk = new Dictionary<(string Track, int Speaker), Dictionary<string, double>>();
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
            if (pinned.TryGetValue(segment.Id, out var pin))
            {
                if (!labelTalk.TryGetValue(key, out var talk))
                {
                    labelTalk[key] = talk = new Dictionary<string, double>(StringComparer.Ordinal);
                }

                talk[pin.Label] = talk.GetValueOrDefault(pin.Label) + Math.Max(0, segment.End - segment.Start);
            }
        }

        // A voice is pinned to the label most of its placed lines carry (the first label on a tie).
        var labelOf = labelTalk.ToDictionary(
            p => p.Key,
            p => p.Value.OrderByDescending(l => l.Value).ThenBy(l => l.Key, StringComparer.Ordinal).First().Key);

        // Pass 2: who is the same person (numbered in the order people first speak).
        var group = Group(order, lines, tracks, labelOf, expectedSpeakers, joinSimilarity, minSpeechSeconds, foldSimilarity, ownSpeakerSeconds, out var personLabels, out var merged);
        var speakers = personLabels
            .Select((_, n) => new Speaker(TranscriptSpeakers.IdFor(n + 1), TranscriptSpeakers.DefaultName(n + 1), false, TranscriptSpeakers.ColorFor(n), 0))
            .ToList();
        var personOf = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var n = 0; n < personLabels.Count; n++)
        {
            if (personLabels[n] is { } label)
            {
                personOf[label] = speakers[n].Id;
            }
        }

        // A label none of whose lines a voice won (or that lost its voice to another label) is still a person: the user
        // put those lines there. Such people come after the others, in the order their lines first speak.
        foreach (var label in segments
            .Where(s => pinned.ContainsKey(s.Id))
            .OrderBy(s => s.Start)
            .ThenBy(s => s.Id, StringComparer.Ordinal)
            .Select(s => pinned[s.Id].Label)
            .Distinct(StringComparer.Ordinal))
        {
            if (!personOf.ContainsKey(label))
            {
                var n = speakers.Count;
                speakers.Add(new Speaker(TranscriptSpeakers.IdFor(n + 1), TranscriptSpeakers.DefaultName(n + 1), false, TranscriptSpeakers.ColorFor(n), 0));
                personOf[label] = speakers[n].Id;
            }
        }

        // Pass 3: back in the transcript's own order. A placed line keeps its person whatever its voice did.
        var assigned = segments
            .Select(s => pinned.TryGetValue(s.Id, out var pin)
                ? s with { Speaker = personOf[pin.Label], SpeakerConfidence = 1.0, SpeakerByUser = true }
                : winners.TryGetValue(s.Id, out var win)
                    ? s with { Speaker = speakers[group[(win.Track, win.Speaker)]].Id, SpeakerConfidence = win.Confidence, SpeakerByUser = false }
                    : s with { Speaker = null, SpeakerConfidence = null, SpeakerByUser = false })
            .ToList();
        var voices = order
            .Select(key => VoiceOf(tracks, key) is { } voice
                ? new VoiceCluster(key.Track, key.Speaker, voice.Embedding, voice.Seconds, lines[key].Select(s => s.Id).ToList())
                : new VoiceCluster(key.Track, key.Speaker, [], 0, lines[key].Select(s => s.Id).ToList()))
            .ToList();
        var names = pinned.Values
            .Where(p => p.Named)
            .Select(p => p.Label)
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(label => personOf[label], label => label, StringComparer.Ordinal);
        return new Result(assigned, TranscriptSpeakers.WithTalkTime(speakers, assigned), merged, voices, labelOf.Count, names);
    }

    /// <summary>
    /// Person number per (track, speaker), numbered by the first appearance of any of their voices; <paramref name="personLabels"/>
    /// holds each person's pinned label, or <c>null</c>.
    /// </summary>
    private static Dictionary<(string Track, int Speaker), int> Group(
        List<(string Track, int Speaker)> order,
        Dictionary<(string Track, int Speaker), List<TranscriptSegment>> lines,
        IReadOnlyList<DiarizedTrack> tracks,
        Dictionary<(string Track, int Speaker), string> labelOf,
        int? expected,
        double? joinSimilarity,
        double minSpeechSeconds,
        double foldSimilarity,
        double ownSpeakerSeconds,
        out List<string?> personLabels,
        out int merged)
    {
        merged = 0;
        var clusters = order
            .Select(key => new Cluster([key], key.Track, VoiceOf(tracks, key) is { } v ? VoicePrint.From(v.Embedding, v.Seconds) : null, lines[key].Sum(s => Math.Max(0, s.End - s.Start)), labelOf.GetValueOrDefault(key)))
            .ToList();

        // Voices the user's lines pin to one person are that person before anything is compared, whatever their tracks.
        foreach (var label in labelOf.Values.Distinct(StringComparer.Ordinal).ToList())
        {
            while (true)
            {
                var members = clusters.Where(c => string.Equals(c.Label, label, StringComparison.Ordinal)).OrderByDescending(c => c.Seconds).ToList();
                if (members.Count < 2)
                {
                    break;
                }

                Join(clusters, members[0], members[1]);
                merged++;
            }
        }

        // Voices with little speech (a cough, a laugh, two people at once) are folded into the main voices last, so the
        // main voices are compared with each other and not with noise. With a count, the main voices are at least that
        // many: the ones with the most speech. A pinned voice is always main: it is a person the user pointed at.
        // "Little" is relative: a short recording's voices are all short.
        var little = Math.Min(minSpeechSeconds, MinSpeechShare * clusters.Sum(c => c.Seconds));
        var main = clusters.Where(c => c.Seconds >= little || c.Label is not null).ToList();
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
                ? Closest(main, voice, sameTrackOnly: true, atLeast: voice.Seconds < ownSpeakerSeconds ? double.NegativeInfinity : foldSimilarity)
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

        personLabels = numbered.Select(c => c.Label).ToList();
        return person;
    }

    /// <summary>Two voices may be joined unless the user pinned them to different people.</summary>
    private static bool CanJoin(Cluster a, Cluster b) =>
        a.Label is null || b.Label is null || string.Equals(a.Label, b.Label, StringComparison.Ordinal);

    /// <summary>
    /// The main voice a small one belongs to: the most alike (at least <paramref name="atLeast"/>), or for a small voice
    /// without an embedding (only when reaching a count) the one with the most speech on its track. <c>null</c> keeps it as
    /// a voice of its own.
    /// </summary>
    private static Cluster? Closest(List<Cluster> main, Cluster small, bool sameTrackOnly, double atLeast)
    {
        var candidates = main.Where(c => (!sameTrackOnly || c.Track == small.Track) && CanJoin(c, small)).ToList();
        if (small.Voice is null)
        {
            // Without an embedding it is only a guess: made to reach a count, never in Auto.
            return !double.IsNegativeInfinity(atLeast)
                ? null
                : candidates.OrderByDescending(c => c.Seconds).FirstOrDefault() ?? main.Where(c => CanJoin(c, small)).OrderByDescending(c => c.Seconds).FirstOrDefault();
        }

        var best = candidates
            .Where(c => c.Voice is not null)
            .Select(c => (Cluster: c, Similarity: c.Voice!.Similarity(small.Voice)))
            .OrderByDescending(c => c.Similarity)
            .FirstOrDefault();
        return best.Cluster is not null && best.Similarity >= atLeast ? best.Cluster : null;
    }

    /// <summary>
    /// A voiceless cluster (the one with the least speech that has somewhere to go) and the cluster with the most speech on
    /// its track (else anywhere) it may join.
    /// </summary>
    private static (Cluster Into, Cluster From)? Voiceless(List<Cluster> clusters)
    {
        foreach (var from in clusters.Where(c => c.Voice is null).OrderBy(c => c.Seconds).ThenByDescending(clusters.IndexOf))
        {
            var into = clusters.Where(c => c != from && c.Track == from.Track && CanJoin(c, from)).OrderByDescending(c => c.Seconds).FirstOrDefault()
                ?? clusters.Where(c => c != from && CanJoin(c, from)).OrderByDescending(c => c.Seconds).FirstOrDefault();
            if (into is not null)
            {
                return (into, from);
            }
        }

        return null;
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
                if (a.Voice is null || b.Voice is null || (sameTrackOnly && a.Track != b.Track) || !CanJoin(a, b))
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
        clusters[index] = new Cluster([.. into.Keys, .. from.Keys], into.Track, voice, into.Seconds + from.Seconds, into.Label ?? from.Label);
        clusters.Remove(from);
    }

    private static SpeakerVoice? VoiceOf(IReadOnlyList<DiarizedTrack> tracks, (string Track, int Speaker) key)
    {
        var voice = tracks.FirstOrDefault(t => t.TrackId == key.Track)?.Voices?.FirstOrDefault(v => v.Speaker == key.Speaker);
        return voice is null || voice.Embedding.Count == 0 ? null : voice;
    }

    /// <param name="Track">The track of the voice the cluster started from (joins across tracks keep the larger one's).</param>
    /// <param name="Seconds">Talk time of the lines its voices won.</param>
    /// <param name="Label">The person the user's lines pin this voice to, or <c>null</c>.</param>
    private sealed record Cluster(List<(string Track, int Speaker)> Keys, string Track, VoicePrint? Voice, double Seconds, string? Label);
}
