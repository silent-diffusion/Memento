namespace Memento.Core.Audio;

/// <summary>One track to mix: its WAV file, where it starts on the session timeline, and its gain ("Even out source levels").</summary>
public sealed record MixInput(string WavPath, long StartOffsetMs, float Gain = 1f);
