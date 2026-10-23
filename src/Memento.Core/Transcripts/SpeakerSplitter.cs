using System.Globalization;
using Memento.Core.Bridge.Contracts;

namespace Memento.Core.Transcripts;

/// <summary>
/// Review's "{name} is two people" (<c>transcript.splitSpeaker</c>, 2.6.0), the inverse of a merge: the raw voices
/// (<c>voices.json</c> clusters with an embedding) behind a speaker's lines are grouped into two by sound, and the half with
/// less talk time becomes a new speaker. A voice counts when at least one of the speaker's lines is in it, weighted by the
/// talk time of those lines. The two voices least alike seed the groups; every other voice joins the seed mix it sounds
/// more like, the mixes are recomputed (speech-weighted, <see cref="VoicePrint.Combine"/>), and the round repeats until
/// nothing moves or <see cref="MaxRounds"/> is reached. Lines the user placed (<c>speakerByUser</c>) never move, and lines
/// in no voice stay. Nothing is split when the speaker has fewer than two voices, or when its two least alike voices are
/// still at least <see cref="AlikeSimilarity"/> alike (the sound does not separate them).
/// </summary>
public static class SpeakerSplitter
{
    /// <summary>Two seed voices at least this alike (cosine) are one voice to the model: nothing to split by sound.</summary>
    public const double AlikeSimilarity = 0.8;

    /// <summary>How many rounds of re-assigning voices to the nearer mix are tried before the grouping is taken as it is.</summary>
    public const int MaxRounds = 10;

    /// <param name="Created">The new speaker, or <c>null</c> when <see cref="Refusal"/> says why nothing was split.</param>
    /// <param name="SegmentIds">The lines moved to the new speaker, in transcript order.</param>
    /// <param name="Refusal">The sentence (DESIGN.md §17) saying why the speaker cannot be split, or <c>null</c>.</param>
    public sealed record Result(IReadOnlyList<TranscriptSegment> Segments, IReadOnlyList<Speaker> Speakers, Speaker? Created, IReadOnlyList<string> SegmentIds, string? Refusal);

    public static Result Split(IReadOnlyList<TranscriptSegment> segments, IReadOnlyList<Speaker> speakers, VoicesDocument? voices, string speakerId)
    {
        ArgumentNullException.ThrowIfNull(segments);
        ArgumentNullException.ThrowIfNull(speakers);
        ArgumentException.ThrowIfNullOrWhiteSpace(speakerId);
        var speaker = speakers.First(s => s.Id == speakerId);
        var name = speaker.Name;

        // The speaker's voices: each cluster with an embedding that holds one of its lines, weighted by those lines' talk.
        var voiceOf = new Dictionary<string, Voice>(StringComparer.Ordinal);
        var ownVoices = new List<Voice>();
        foreach (var cluster in voices?.Clusters ?? [])
        {
            if (VoicePrint.From(cluster.Embedding ?? [], cluster.Seconds) is not { } print)
            {
                continue;
            }

            var voice = new Voice(print);
            foreach (var id in cluster.SegmentIds ?? [])
            {
                voiceOf.TryAdd(id, voice);
            }
        }

        foreach (var segment in segments)
        {
            if (segment.Speaker == speakerId && voiceOf.TryGetValue(segment.Id, out var voice))
            {
                voice.Lines.Add(segment);
                voice.Seconds += Math.Max(0, segment.End - segment.Start);
                if (!ownVoices.Contains(voice))
                {
                    ownVoices.Add(voice);
                }
            }
        }

        if (ownVoices.Count < 2)
        {
            return Refused(segments, speakers, $"{name} was heard as one voice, so there is nothing to split by sound. Move lines with their menu instead.");
        }

        // The two voices least alike seed the groups; alike seeds mean the model hears one person.
        var (a, b, seedSimilarity) = LeastAlike(ownVoices);
        if (seedSimilarity >= AlikeSimilarity)
        {
            return Refused(segments, speakers, string.Create(CultureInfo.InvariantCulture, $"{name}'s voices sound alike ({seedSimilarity:0.00}), so Memento cannot split them by sound."));
        }

        var inB = new HashSet<Voice> { b };
        var mixA = a.Print;
        var mixB = b.Print;
        for (var round = 0; round < MaxRounds; round++)
        {
            var next = new HashSet<Voice>();
            foreach (var voice in ownVoices)
            {
                if (voice.Print.Similarity(mixB) > voice.Print.Similarity(mixA))
                {
                    next.Add(voice);
                }
            }

            if (next.Count == 0 || next.Count == ownVoices.Count)
            {
                return Refused(segments, speakers, string.Create(CultureInfo.InvariantCulture, $"{name}'s voices sound alike ({seedSimilarity:0.00}), so Memento cannot split them by sound."));
            }

            var same = next.SetEquals(inB);
            inB = next;
            mixA = Mix(ownVoices.Where(v => !inB.Contains(v))) ?? mixA;
            mixB = Mix(ownVoices.Where(inB.Contains)) ?? mixB;
            if (same)
            {
                break;
            }
        }

        // The half with more talk time keeps the speaker; the other half's lines move, except the ones the user placed.
        var secondsB = ownVoices.Where(inB.Contains).Sum(v => v.Seconds);
        var secondsA = ownVoices.Sum(v => v.Seconds) - secondsB;
        var leaving = ownVoices.Where(v => secondsB > secondsA ? !inB.Contains(v) : inB.Contains(v)).ToList();
        var moving = leaving.SelectMany(v => v.Lines).Where(s => !s.SpeakerByUser).Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        if (moving.Count == 0)
        {
            return Refused(segments, speakers, $"{name}'s other voice holds only lines you placed there yourself, so nothing can move.");
        }

        var number = TranscriptSpeakers.NextNumber(speakers);
        var created = new Speaker(TranscriptSpeakers.IdFor(number), TranscriptSpeakers.DefaultName(number), false, TranscriptSpeakers.ColorFor(speakers.Count), 0);
        var result = segments.Select(s => moving.Contains(s.Id) ? s with { Speaker = created.Id } : s).ToList();
        var all = TranscriptSpeakers.WithTalkTime([.. speakers, created], result);
        return new Result(result, all, all[^1], result.Where(s => moving.Contains(s.Id)).Select(s => s.Id).ToList(), null);
    }

    private static Result Refused(IReadOnlyList<TranscriptSegment> segments, IReadOnlyList<Speaker> speakers, string why) =>
        new(segments, speakers, null, [], why);

    /// <summary>The two voices with the lowest cosine between them, and that cosine.</summary>
    private static (Voice A, Voice B, double Similarity) LeastAlike(List<Voice> voices)
    {
        var best = (A: voices[0], B: voices[1], Similarity: double.PositiveInfinity);
        for (var i = 0; i < voices.Count; i++)
        {
            for (var j = i + 1; j < voices.Count; j++)
            {
                var similarity = voices[i].Print.Similarity(voices[j].Print);
                if (similarity < best.Similarity)
                {
                    best = (voices[i], voices[j], similarity);
                }
            }
        }

        return best;
    }

    /// <summary>The talk-weighted mix of some voices, or <c>null</c> when there are none.</summary>
    private static VoicePrint? Mix(IEnumerable<Voice> voices)
    {
        VoicePrint? mix = null;
        foreach (var voice in voices)
        {
            var weighted = voice.Print with { Seconds = Math.Max(0.1, voice.Seconds) };
            mix = mix is null ? weighted : mix.Combine(weighted) ?? mix;
        }

        return mix;
    }

    /// <summary>One raw voice of the speaker: its print, the speaker's lines in it and their talk time (a class: identity, not value).</summary>
    private sealed class Voice(VoicePrint print)
    {
        public VoicePrint Print { get; } = print;

        public List<TranscriptSegment> Lines { get; } = [];

        public double Seconds { get; set; }
    }
}
