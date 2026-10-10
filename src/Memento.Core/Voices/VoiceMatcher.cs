using Memento.Core.Bridge.Contracts;
using Memento.Core.Transcripts;

namespace Memento.Core.Voices;

/// <summary>
/// Which unnamed speakers of a recording sound like a known voice (DESIGN.md §19, match prompt). A speaker's voice is
/// the mix of the voices of its lines (<see cref="SpeakerVoices"/>), from at least <see cref="MinSeconds"/> of speech;
/// it is compared by cosine with every known voice of the same voice model. A suggestion needs the best known voice
/// to be at least <see cref="Similarity"/> alike (<see cref="PreferredSimilarity"/> when its name is one of the
/// recording's Who spoke names or participants), at least <see cref="Margin"/> more alike than any other known voice,
/// and the best for that voice among the speakers (one speaker per voice, one voice per speaker). Voices the user muted
/// ("Suggest this voice" off), declined in this recording ("Not {name}") or already gave to a named speaker here are
/// never suggested, but still count as the second best. Nothing is ever applied: the user accepts or declines.
/// Thresholds: ENGINE-NOTES.md §N.
/// </summary>
public static class VoiceMatcher
{
    /// <summary>The cosine a known voice must reach for a suggestion.</summary>
    public const double Similarity = 0.60;

    /// <summary>The cosine for a known voice named in Who spoke or the participants, which the recording expects.</summary>
    public const double PreferredSimilarity = 0.50;

    /// <summary>How much more alike than any other known voice the best one must be.</summary>
    public const double Margin = 0.10;

    /// <summary>A speaker with less voiced speech than this gets no suggestion, and is not remembered.</summary>
    public const double MinSeconds = 10;

    /// <param name="Similarity">Cosine of the speaker's voice and the known voice's signature.</param>
    /// <param name="Margin">How much more alike than the next known voice (1 when there is none).</param>
    /// <param name="Preferred">The name is one of the recording's Who spoke names or participants.</param>
    public sealed record Suggestion(string SpeakerId, KnownVoice Voice, double Similarity, double Margin, bool Preferred);

    public static IReadOnlyList<Suggestion> Match(
        IReadOnlyList<TranscriptSegment> segments,
        IReadOnlyList<Speaker> speakers,
        VoicesDocument? voices,
        IReadOnlyList<KnownVoice> known,
        string recordingId,
        IReadOnlyCollection<string> preferredNames)
    {
        ArgumentNullException.ThrowIfNull(segments);
        ArgumentNullException.ThrowIfNull(speakers);
        ArgumentNullException.ThrowIfNull(known);
        ArgumentNullException.ThrowIfNull(preferredNames);
        if (voices is null || known.Count == 0)
        {
            return [];
        }

        var voiceOf = SpeakerVoices.OfLines(voices);
        var named = speakers.Where(s => s.Renamed).Select(s => Key(s.Name)).ToHashSet(StringComparer.Ordinal);
        var preferred = preferredNames.Select(Key).ToHashSet(StringComparer.Ordinal);
        var comparable = known
            .Where(k => string.Equals(k.EmbeddingModelId, voices.EmbeddingModelId, StringComparison.Ordinal))
            .Select(k => (Voice: k, Signature: k.Signature()))
            .Where(k => k.Signature is not null)
            .ToList();
        var candidates = new List<Suggestion>();
        foreach (var speaker in speakers.Where(s => !s.Renamed))
        {
            if (SpeakerVoices.Of(speaker.Id, segments, voiceOf) is not { } voice || voice.Seconds < MinSeconds)
            {
                continue;
            }

            var scored = comparable.Select(k => (k.Voice, Similarity: voice.Similarity(k.Signature!))).OrderByDescending(k => k.Similarity).ToList();
            if (scored.Count == 0)
            {
                continue;
            }

            var (best, similarity) = scored[0];
            var margin = scored.Count > 1 ? similarity - scored[1].Similarity : 1.0;
            var isPreferred = preferred.Contains(Key(best.Name));
            var eligible = best.Suggest && !best.DeclinedIn.Contains(recordingId, StringComparer.Ordinal) && !named.Contains(Key(best.Name));
            if (eligible && similarity >= (isPreferred ? PreferredSimilarity : Similarity) && margin >= Margin)
            {
                candidates.Add(new Suggestion(speaker.Id, best, similarity, margin, isPreferred));
            }
        }

        // One speaker per voice: the most alike speaker gets it.
        var chosen = new List<Suggestion>();
        foreach (var candidate in candidates.OrderByDescending(c => c.Similarity))
        {
            if (chosen.All(c => c.Voice.Id != candidate.Voice.Id && c.SpeakerId != candidate.SpeakerId))
            {
                chosen.Add(candidate);
            }
        }

        return chosen;
    }

    /// <summary>Names compare ignoring case and the spaces around them.</summary>
    public static string Key(string name) => (name ?? string.Empty).Trim().ToUpperInvariant();
}
