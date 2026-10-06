namespace Memento.Core.Audio;

/// <summary>What <see cref="IAudioFileVerifier"/> decoded: format and the duration of every sample read.</summary>
public sealed record AudioFileCheck(int SampleRate, int Channels, long DurationMs);
