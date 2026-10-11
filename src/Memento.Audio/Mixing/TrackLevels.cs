using Memento.Audio.Codecs;
using Memento.Core.Audio;

namespace Memento.Audio.Mixing;

/// <summary>
/// The speech level of a whole track (<see cref="SpeechLevel"/>) for "Even out source levels": decoded block by block
/// at its own rate from the files on disk (capture WAVs after the recording stopped, or stored tracks), never from a
/// capture thread.
/// </summary>
public static class TrackLevels
{
    public static Task<SpeechLevel> MeasureAsync(Func<DecodedAudio> open, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(open);
        return Task.Run(
            () =>
            {
                using var audio = open();
                var meter = new SpeechLevelMeter(audio.SampleRate);
                var buffer = new float[audio.SampleRate * audio.Channels];
                int n;
                while ((n = audio.Read(buffer, 0, buffer.Length)) > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    meter.AddInterleaved(buffer.AsSpan(0, n - (n % audio.Channels)), audio.Channels);
                }

                return meter.Result();
            },
            cancellationToken);
    }

    /// <summary>Measures every track and returns the gains that even them out (<see cref="MixLevels.Even"/>).</summary>
    public static async Task<IReadOnlyList<MixTrackGain>> EvenAsync(IReadOnlyList<(string TrackId, Func<DecodedAudio> Open)> tracks, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tracks);
        var levels = new List<(string, SpeechLevel)>();
        foreach (var (id, open) in tracks)
        {
            levels.Add((id, await MeasureAsync(open, cancellationToken).ConfigureAwait(false)));
        }

        return MixLevels.Even(levels);
    }
}
