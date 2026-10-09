using Memento.Core.Bridge.Contracts;

namespace Memento.Core.Transcripts;

/// <summary>
/// Review's "Reduce to {n} speakers" (<c>transcript.reduceSpeakers</c>): merges speakers, the two whose voices sound most
/// alike first, until <c>n</c> are left. A speaker's voice is the speech-weighted mix of the voices (<c>voices.json</c>)
/// of the lines it has now, so renames, merges and moved lines since the pass count. When no voices were kept (or none of
/// the remaining pairs has two), the speaker with the least talk time goes into the one with the most. Two speakers the
/// user named are never merged with each other, and a named speaker always keeps its name: an unnamed one goes into it.
/// Otherwise the speaker with less talk time goes into the one with more. Nothing is renumbered or renamed.
/// </summary>
public static class SpeakerReducer
{
    public const string BasisVoices = "voices";
    public const string BasisTalkTime = "talkTime";

    /// <param name="Basis"><see cref="BasisVoices"/> when at least one merge was chosen by voice, else <see cref="BasisTalkTime"/>.</param>
    public sealed record Result(IReadOnlyList<TranscriptSegment> Segments, IReadOnlyList<Speaker> Speakers, IReadOnlyList<SpeakerMerge> Merges, int SegmentsChanged, string Basis);

    public static Result Reduce(IReadOnlyList<TranscriptSegment> segments, IReadOnlyList<Speaker> speakers, VoicesDocument? voices, int target)
    {
        ArgumentNullException.ThrowIfNull(segments);
        ArgumentNullException.ThrowIfNull(speakers);
        ArgumentOutOfRangeException.ThrowIfLessThan(target, 1);

        var assigned = segments.ToDictionary(s => s.Id, s => s.Speaker, StringComparer.Ordinal);
        var clusterOf = VoiceOfLines(voices);
        var people = speakers.Select(s => new Person(s, Voice(s.Id, segments, clusterOf))).ToList();
        var merges = new List<SpeakerMerge>();
        var byVoice = false;
        while (people.Count > target)
        {
            var talk = Talk(segments, assigned);
            var first = FirstAppearance(segments, assigned);
            // An unnamed speaker none of whose lines has a voice goes first (least speech first), then the most alike
            // voices, then (no voices left to compare) least speech first again.
            var pair = LeastSpeech(people, talk, p => p.Voice is null) is { } voiceless ? (voiceless.From, voiceless.Into, Voice: false)
                : MostAlike(people) is { } alike ? (alike.From, alike.Into, Voice: true)
                : LeastSpeech(people, talk, _ => true) is { } least ? (least.From, least.Into, Voice: false)
                : default;
            if (pair.From is null || pair.Into is null)
            {
                break; // Only named speakers are left: they are different people.
            }

            var (from, into) = Direction(pair.From, pair.Into, talk, first);
            byVoice |= pair.Voice;
            var lines = segments.Where(s => assigned[s.Id] == from.Speaker.Id).Select(s => s.Id).ToList();
            merges.Add(new SpeakerMerge(new SpeakerRestore(from.Speaker.Id, from.Speaker.Name, from.Speaker.Color, from.Speaker.Renamed), into.Speaker.Id, lines));
            foreach (var line in lines)
            {
                assigned[line] = into.Speaker.Id;
            }

            into.Voice = into.Voice is null ? from.Voice : from.Voice is null ? into.Voice : into.Voice.Combine(from.Voice);
            people.Remove(from);
        }

        var moved = merges.Sum(m => m.SegmentIds.Count);
        var result = segments.Select(s => assigned[s.Id] == s.Speaker ? s : s with { Speaker = assigned[s.Id] }).ToList();
        var kept = people.Select(p => p.Speaker.Id).ToHashSet(StringComparer.Ordinal);
        var left = TranscriptSpeakers.WithTalkTime(speakers.Where(s => kept.Contains(s.Id)).ToList(), result);
        return new Result(result, left, merges, moved, byVoice ? BasisVoices : BasisTalkTime);
    }

    /// <summary>The voice each line was given by the last speaker pass (empty without <c>voices.json</c>).</summary>
    private static Dictionary<string, VoicePrint> VoiceOfLines(VoicesDocument? voices)
    {
        var voiceOf = new Dictionary<string, VoicePrint>(StringComparer.Ordinal);
        foreach (var cluster in voices?.Clusters ?? [])
        {
            if (VoicePrint.From(cluster.Embedding ?? [], cluster.Seconds) is { } print)
            {
                foreach (var id in cluster.SegmentIds ?? [])
                {
                    voiceOf[id] = print;
                }
            }
        }

        return voiceOf;
    }

    /// <summary>The speech-weighted mix of the voices of the lines this speaker has, or <c>null</c> when none of them has one.</summary>
    private static VoicePrint? Voice(string speakerId, IReadOnlyList<TranscriptSegment> segments, Dictionary<string, VoicePrint> voiceOf)
    {
        VoicePrint? mix = null;
        foreach (var segment in segments)
        {
            if (segment.Speaker == speakerId && voiceOf.TryGetValue(segment.Id, out var print))
            {
                var weighted = print with { Seconds = Math.Max(0.1, segment.End - segment.Start) };
                mix = mix is null ? weighted : mix.Combine(weighted) ?? mix;
            }
        }

        return mix;
    }

    /// <summary>The two speakers (not both named) whose voices are most alike, or <c>null</c> when no such pair has voices.</summary>
    private static (Person From, Person Into)? MostAlike(List<Person> people)
    {
        (Person, Person)? best = null;
        var bestSimilarity = double.NegativeInfinity;
        for (var i = 0; i < people.Count; i++)
        {
            for (var j = i + 1; j < people.Count; j++)
            {
                var (a, b) = (people[i], people[j]);
                if (a.Speaker.Renamed && b.Speaker.Renamed || a.Voice is null || b.Voice is null)
                {
                    continue;
                }

                var similarity = a.Voice.Similarity(b.Voice);
                if (similarity > bestSimilarity)
                {
                    bestSimilarity = similarity;
                    best = (a, b);
                }
            }
        }

        return best;
    }

    /// <summary>The unnamed speaker with the least talk time and the other speaker with the most.</summary>
    private static (Person From, Person Into)? LeastSpeech(List<Person> people, Dictionary<string, double> talk, Func<Person, bool> candidate)
    {
        var from = people.Where(p => !p.Speaker.Renamed && candidate(p)).OrderBy(p => talk.GetValueOrDefault(p.Speaker.Id)).ThenByDescending(p => people.IndexOf(p)).FirstOrDefault();
        if (from is null)
        {
            return null;
        }

        var into = people.Where(p => p != from).OrderByDescending(p => talk.GetValueOrDefault(p.Speaker.Id)).ThenBy(p => people.IndexOf(p)).FirstOrDefault();
        return into is null ? null : (from, into);
    }

    /// <summary>A named speaker keeps its name; otherwise more talk time wins, then the earlier first line.</summary>
    private static (Person From, Person Into) Direction(Person a, Person b, Dictionary<string, double> talk, Dictionary<string, double> first)
    {
        if (a.Speaker.Renamed != b.Speaker.Renamed)
        {
            return a.Speaker.Renamed ? (b, a) : (a, b);
        }

        var ta = talk.GetValueOrDefault(a.Speaker.Id);
        var tb = talk.GetValueOrDefault(b.Speaker.Id);
        if (Math.Abs(ta - tb) > 1e-9)
        {
            return ta > tb ? (b, a) : (a, b);
        }

        return first.GetValueOrDefault(a.Speaker.Id, double.MaxValue) <= first.GetValueOrDefault(b.Speaker.Id, double.MaxValue) ? (b, a) : (a, b);
    }

    private static Dictionary<string, double> Talk(IReadOnlyList<TranscriptSegment> segments, Dictionary<string, string?> assigned)
    {
        var talk = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var segment in segments)
        {
            if (assigned[segment.Id] is { } id)
            {
                talk[id] = talk.GetValueOrDefault(id) + Math.Max(0, segment.End - segment.Start);
            }
        }

        return talk;
    }

    private static Dictionary<string, double> FirstAppearance(IReadOnlyList<TranscriptSegment> segments, Dictionary<string, string?> assigned)
    {
        var first = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var segment in segments)
        {
            if (assigned[segment.Id] is { } id && (!first.TryGetValue(id, out var at) || segment.Start < at))
            {
                first[id] = segment.Start;
            }
        }

        return first;
    }

    private sealed class Person(Speaker speaker, VoicePrint? voice)
    {
        public Speaker Speaker { get; } = speaker;

        public VoicePrint? Voice { get; set; } = voice;
    }
}
