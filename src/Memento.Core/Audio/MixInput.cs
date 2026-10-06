namespace Memento.Core.Audio;

/// <summary>One track to mix: its WAV file and where it starts on the session timeline.</summary>
public sealed record MixInput(string WavPath, long StartOffsetMs);
