namespace Memento.Core.Audio;

/// <summary>The speech level of a whole WAV file (<see cref="SpeechLevel"/>), read block by block from disk.</summary>
public static class WavSpeechLevel
{
    private const int BlockFrames = 48_000;

    public static SpeechLevel Measure(string path, CancellationToken cancellationToken)
    {
        using var reader = new WavReader(path);
        var channels = reader.Format.Channels;
        var meter = new SpeechLevelMeter(reader.Format.SampleRate);
        var block = new float[BlockFrames * channels];
        int frames;
        while ((frames = reader.ReadFrames(block)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            meter.AddInterleaved(block.AsSpan(0, frames * channels), channels);
        }

        return meter.Result();
    }
}
