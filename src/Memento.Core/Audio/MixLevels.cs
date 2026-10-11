namespace Memento.Core.Audio;

/// <summary>
/// "Even out source levels" for the mix (ENGINE-NOTES.md §Q): each track's speech level is measured
/// (<see cref="SpeechLevel"/>, against its own noise floor) and a track whose speech is more than
/// <see cref="ToleranceDb"/> under the loudest track's is lifted to it, by at most <see cref="MaxLiftDb"/>. Nothing is
/// ever turned down, a track without speech keeps its level (its noise is not lifted), and the mixer's soft limiter
/// keeps the sum below full scale. Only the mix changes; the tracks are never touched.
/// </summary>
public static class MixLevels
{
    /// <summary>Tracks within this of the loudest are left as they are.</summary>
    public const double ToleranceDb = 3;

    /// <summary>The most a quiet track is lifted (its room noise rises as much).</summary>
    public const double MaxLiftDb = 24;

    /// <summary>The gain for each track, in the order given (0 dB when it is left as it is).</summary>
    public static IReadOnlyList<MixTrackGain> Even(IReadOnlyList<(string TrackId, SpeechLevel Level)> tracks)
    {
        ArgumentNullException.ThrowIfNull(tracks);
        var speaking = tracks.Where(t => t.Level.HasSpeech).ToList();
        var loudest = speaking.Count == 0 ? double.NegativeInfinity : speaking.Max(t => t.Level.ActiveDbfs);
        return tracks.Select(t =>
        {
            double? level = t.Level.HasSpeech ? Math.Round(t.Level.ActiveDbfs, 1) : null;
            var under = t.Level.HasSpeech ? loudest - t.Level.ActiveDbfs : 0;
            var gain = under > ToleranceDb ? Math.Min(under, MaxLiftDb) : 0;
            return new MixTrackGain(t.TrackId, level, Math.Round(gain, 1));
        }).ToList();
    }

    /// <summary>
    /// For History: "Microphone lifted 24.0 dB (speech at −53.9 dBFS) to match System audio (−29.8 dBFS)", or that every
    /// track was within 3 dB already.
    /// </summary>
    public static string Describe(IReadOnlyList<MixTrackGain> gains, Func<string, string> name)
    {
        ArgumentNullException.ThrowIfNull(gains);
        ArgumentNullException.ThrowIfNull(name);
        var loudest = gains.Where(g => g.LevelDbfs is not null && g.GainDb == 0).MaxBy(g => g.LevelDbfs);
        var lifted = gains.Where(g => g.GainDb > 0).ToList();
        if (lifted.Count == 0 || loudest is null)
        {
            return "levels evened: every track with speech was within 3 dB of the loudest, so none was lifted";
        }

        var parts = lifted.Select(g => string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{name(g.TrackId)} lifted {g.GainDb:0.0} dB (speech at {g.LevelDbfs:0.0} dBFS)"));
        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"levels evened: {string.Join(", ", parts)} to match {name(loudest.TrackId)} ({loudest.LevelDbfs:0.0} dBFS)");
    }

    /// <summary>The linear factor for a gain in dB.</summary>
    public static float Linear(double gainDb) => (float)SpeechLevel.FromDbfs(gainDb);
}
