using Memento.Core.Bridge.Contracts;
using Memento.Core.Transcripts;
using Memento.Core.Workers;

namespace Memento.Transcription.Speakers;

/// <summary>
/// Gives each transcript segment the speaker whose turns (in the same track) overlap it most. Speakers of different
/// tracks are different people (a microphone's speaker and a meeting app's speaker are never merged automatically);
/// ids <c>spk1…</c>, names "Speaker 1…" and colours 1–4 follow the order in which each first speaks.
/// <c>speakerConfidence</c> is the overlap-weighted confidence of the winning speaker's turns times its share of the
/// overlapping speech. A segment no turn touches keeps no speaker.
/// </summary>
public static class SpeakerAssigner
{
    public sealed record Result(IReadOnlyList<TranscriptSegment> Segments, IReadOnlyList<Speaker> Speakers);

    public static Result Assign(IReadOnlyList<TranscriptSegment> segments, IReadOnlyList<DiarizedTrack> tracks)
    {
        ArgumentNullException.ThrowIfNull(segments);
        ArgumentNullException.ThrowIfNull(tracks);
        var turnsByTrack = tracks.ToDictionary(t => t.TrackId, t => t.Turns, StringComparer.Ordinal);
        var ids = new Dictionary<(string Track, int Speaker), string>();
        var speakers = new List<Speaker>();
        var assigned = new List<TranscriptSegment>(segments.Count);
        foreach (var segment in segments.OrderBy(s => s.Start).ThenBy(s => s.Id, StringComparer.Ordinal))
        {
            if (segment.Track is not { } track || !turnsByTrack.TryGetValue(track, out var turns))
            {
                assigned.Add(segment with { Speaker = null, SpeakerConfidence = null });
                continue;
            }

            var overlaps = turns
                .Select(t => (Turn: t, Overlap: Math.Min(t.End, segment.End) - Math.Max(t.Start, segment.Start)))
                .Where(o => o.Overlap > 0)
                .ToList();
            if (overlaps.Count == 0)
            {
                assigned.Add(segment with { Speaker = null, SpeakerConfidence = null });
                continue;
            }

            var bySpeaker = overlaps.GroupBy(o => o.Turn.Speaker)
                .Select(g => (Speaker: g.Key, Overlap: g.Sum(o => o.Overlap), Confidence: g.Sum(o => o.Overlap * o.Turn.Confidence) / g.Sum(o => o.Overlap)))
                .OrderByDescending(g => g.Overlap)
                .ThenBy(g => g.Speaker)
                .ToList();
            var winner = bySpeaker[0];
            var share = winner.Overlap / bySpeaker.Sum(g => g.Overlap);
            if (!ids.TryGetValue((track, winner.Speaker), out var id))
            {
                var number = speakers.Count + 1;
                id = TranscriptSpeakers.IdFor(number);
                ids[(track, winner.Speaker)] = id;
                speakers.Add(new Speaker(id, TranscriptSpeakers.DefaultName(number), false, TranscriptSpeakers.ColorFor(speakers.Count), 0));
            }

            assigned.Add(segment with { Speaker = id, SpeakerConfidence = Math.Round(Math.Clamp(winner.Confidence * share, 0, 1), 3) });
        }

        // Back in the transcript's own order.
        var order = segments.Select((s, i) => (s.Id, i)).ToDictionary(x => x.Id, x => x.i, StringComparer.Ordinal);
        var result = assigned.OrderBy(s => order[s.Id]).ToList();
        return new Result(result, TranscriptSpeakers.WithTalkTime(speakers, result));
    }
}
