namespace Memento.Core.Import;

/// <summary>The WAV files <see cref="IMediaDecoder.DecodeToWavAsync"/> wrote, first part first.</summary>
public sealed record DecodedWav(IReadOnlyList<string> Parts, int SampleRate, int Channels, long Frames)
{
    public long DurationMs => SampleRate == 0 ? 0 : Frames * 1000 / SampleRate;
}
