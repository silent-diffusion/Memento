using System.Globalization;
using Memento.Core.Bridge.Contracts;

namespace Memento.Core.Transcripts;

/// <summary>Speaker bookkeeping shared by the speakers stage and the transcript edits.</summary>
public static class TranscriptSpeakers
{
    public const int Colors = 4;

    /// <summary>Colour 1–4 for the speaker at 0-based position <paramref name="index"/> of first appearance.</summary>
    public static int ColorFor(int index) => (index % Colors) + 1;

    public static string IdFor(int number) => string.Create(CultureInfo.InvariantCulture, $"spk{number}");

    public static string DefaultName(int number) => string.Create(CultureInfo.InvariantCulture, $"Speaker {number}");

    /// <summary>The next free <c>spkN</c> number.</summary>
    public static int NextNumber(IReadOnlyList<Speaker> speakers)
    {
        ArgumentNullException.ThrowIfNull(speakers);
        var max = 0;
        foreach (var speaker in speakers)
        {
            if (speaker.Id.StartsWith("spk", StringComparison.Ordinal)
                && int.TryParse(speaker.Id.AsSpan(3), NumberStyles.None, CultureInfo.InvariantCulture, out var n))
            {
                max = Math.Max(max, n);
            }
        }

        return max + 1;
    }

    /// <summary>Recomputes every speaker's talk time from the segments assigned to it.</summary>
    public static IReadOnlyList<Speaker> WithTalkTime(IReadOnlyList<Speaker> speakers, IReadOnlyList<TranscriptSegment> segments)
    {
        ArgumentNullException.ThrowIfNull(speakers);
        ArgumentNullException.ThrowIfNull(segments);
        var talk = segments
            .Where(s => s.Speaker is not null)
            .GroupBy(s => s.Speaker!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (long)Math.Round(g.Sum(s => Math.Max(0, s.End - s.Start)) * 1000), StringComparer.Ordinal);
        return speakers.Select(s => s with { TalkTimeMs = talk.GetValueOrDefault(s.Id) }).ToList();
    }
}
