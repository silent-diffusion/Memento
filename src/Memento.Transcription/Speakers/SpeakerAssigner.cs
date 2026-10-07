using Memento.Core.Bridge.Contracts;
using Memento.Core.Transcripts;
using Memento.Core.Workers;

namespace Memento.Transcription.Speakers;

/// <summary>
/// Gives each transcript segment the speaker whose turns (in the same track) overlap it most. Speakers of different
/// tracks are different people (a microphone's speaker and a meeting app's speaker are never merged automatically),
/// unless Settings › Speakers names how many people there are: then speakers are grouped by how their voices sound
/// (the voice model's embeddings, most alike first) until that many are left, so a voice the microphone also picked up
/// from the loudspeakers counts once. Ids <c>spk1…</c>, names "Speaker 1…" and colours 1–4 follow the order in which
/// each first speaks. <c>speakerConfidence</c> is the overlap-weighted, calibrated (<see cref="Calibrate"/>) confidence
/// of the winning speaker's turns times its share of the overlapping speech. A segment no turn touches keeps no speaker.
/// </summary>
public static class SpeakerAssigner
{
    /// <param name="MergedAcrossTracks">How many speakers were folded into others to reach the expected count.</param>
    public sealed record Result(IReadOnlyList<TranscriptSegment> Segments, IReadOnlyList<Speaker> Speakers, int MergedAcrossTracks = 0);

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

    /// <param name="expectedSpeakers">Settings › Speakers' count, or <c>null</c> for Auto.</param>
    public static Result Assign(IReadOnlyList<TranscriptSegment> segments, IReadOnlyList<DiarizedTrack> tracks, int? expectedSpeakers = null)
    {
        ArgumentNullException.ThrowIfNull(segments);
        ArgumentNullException.ThrowIfNull(tracks);
        var turnsByTrack = tracks.ToDictionary(t => t.TrackId, t => t.Turns, StringComparer.Ordinal);

        // Pass 1: the winning (track, speaker) of every segment.
        var winners = new Dictionary<string, (string Track, int Speaker, double Confidence)>(StringComparer.Ordinal);
        var order = new List<(string Track, int Speaker)>();
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
            winners[segment.Id] = (track, winner.Speaker, Math.Round(Math.Clamp(Calibrate(winner.Confidence) * share, 0, 1), 3));
            if (!order.Contains((track, winner.Speaker)))
            {
                order.Add((track, winner.Speaker));
            }
        }

        // Pass 2: who is the same person (numbered in the order people first speak). Each (track, speaker) is its own
        // person unless a count was given.
        var group = Group(order, tracks, expectedSpeakers, out var people, out var merged);
        var speakers = Enumerable.Range(0, people)
            .Select(n => new Speaker(TranscriptSpeakers.IdFor(n + 1), TranscriptSpeakers.DefaultName(n + 1), false, TranscriptSpeakers.ColorFor(n), 0))
            .ToList();

        // Pass 3: back in the transcript's own order.
        var assigned = segments
            .Select(s => winners.TryGetValue(s.Id, out var win)
                ? s with { Speaker = speakers[group[(win.Track, win.Speaker)]].Id, SpeakerConfidence = win.Confidence }
                : s with { Speaker = null, SpeakerConfidence = null })
            .ToList();
        return new Result(assigned, TranscriptSpeakers.WithTalkTime(speakers, assigned), merged);
    }

    /// <summary>
    /// Person number per (track, speaker), numbered by first appearance. With <paramref name="expected"/> set and more
    /// speakers than that over several tracks, the two whose voices are most alike (cosine similarity of the voice
    /// embeddings, weighted by speech) are joined, again and again, until <paramref name="expected"/> are left. A speaker
    /// without an embedding is never joined.
    /// </summary>
    private static Dictionary<(string Track, int Speaker), int> Group(List<(string Track, int Speaker)> order, IReadOnlyList<DiarizedTrack> tracks, int? expected, out int people, out int merged)
    {
        merged = 0;
        var clusters = order.Select(key => new Cluster([key], Voice(tracks, key))).ToList();
        var spokenTracks = order.Select(k => k.Track).Distinct(StringComparer.Ordinal).Count();
        if (expected is { } target && target > 0 && spokenTracks > 1)
        {
            while (clusters.Count > target)
            {
                var best = (I: -1, J: -1, Similarity: double.NegativeInfinity);
                for (var i = 0; i < clusters.Count; i++)
                {
                    for (var j = i + 1; j < clusters.Count; j++)
                    {
                        if (clusters[i].Voice is { } a && clusters[j].Voice is { } b && Dot(a.Direction, b.Direction) is var similarity && similarity > best.Similarity)
                        {
                            best = (i, j, similarity);
                        }
                    }
                }

                if (best.I < 0)
                {
                    break;
                }

                var first = clusters[best.I];
                var second = clusters[best.J];
                clusters[best.I] = new Cluster([.. first.Keys, .. second.Keys], Combine(first.Voice!, second.Voice!));
                clusters.RemoveAt(best.J);
                merged++;
            }
        }

        // Numbered by the first appearance of any of their keys.
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

    private static VoicePrint? Voice(IReadOnlyList<DiarizedTrack> tracks, (string Track, int Speaker) key)
    {
        var voice = tracks.FirstOrDefault(t => t.TrackId == key.Track)?.Voices?.FirstOrDefault(v => v.Speaker == key.Speaker);
        if (voice is null || voice.Embedding.Count == 0)
        {
            return null;
        }

        var length = Math.Sqrt(voice.Embedding.Sum(x => (double)x * x));
        return length <= 0 ? null : new VoicePrint(voice.Embedding.Select(x => x / length).ToArray(), Math.Max(voice.Seconds, 0.1));
    }

    private static VoicePrint? Combine(VoicePrint a, VoicePrint b)
    {
        if (a.Direction.Length != b.Direction.Length)
        {
            return null;
        }

        var sum = a.Direction.Select((x, i) => (x * a.Seconds) + (b.Direction[i] * b.Seconds)).ToArray();
        var length = Math.Sqrt(sum.Sum(x => x * x));
        return length <= 0 ? null : new VoicePrint(sum.Select(x => x / length).ToArray(), a.Seconds + b.Seconds);
    }

    private static double Dot(double[] a, double[] b) => a.Length != b.Length ? double.NegativeInfinity : a.Select((x, i) => x * b[i]).Sum();

    private sealed record VoicePrint(double[] Direction, double Seconds);

    private sealed record Cluster(List<(string Track, int Speaker)> Keys, VoicePrint? Voice);
}
