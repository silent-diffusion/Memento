using Memento.Core.Bridge.Contracts;

namespace Memento.Core.Transcripts;

/// <summary>
/// A speaker's voice as it is now: the speech-weighted mix of the voices (<c>voices.json</c>) of the lines the speaker
/// has, so renames, merges and moved lines since the speakers pass count (as in <see cref="SpeakerReducer"/>). Used by
/// known voices (2.0) to enrol a named speaker and to compare an unnamed one with the voices already known.
/// </summary>
public static class SpeakerVoices
{
    /// <summary>The voice each line was given by the last speaker pass (empty without <c>voices.json</c>).</summary>
    public static IReadOnlyDictionary<string, VoicePrint> OfLines(VoicesDocument? voices)
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

    /// <summary>
    /// The mix of the voices of <paramref name="speakerId"/>'s lines, each weighted by the line's length, or <c>null</c>
    /// when none of its lines has a voice. <see cref="VoicePrint.Seconds"/> is the speech it was mixed from.
    /// </summary>
    public static VoicePrint? Of(string speakerId, IReadOnlyList<TranscriptSegment> segments, IReadOnlyDictionary<string, VoicePrint> voiceOf)
    {
        ArgumentNullException.ThrowIfNull(segments);
        ArgumentNullException.ThrowIfNull(voiceOf);
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
}
