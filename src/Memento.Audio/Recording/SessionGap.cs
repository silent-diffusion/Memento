namespace Memento.Audio.Recording;

/// <summary>A pause: at timeline position <paramref name="At"/>, <paramref name="Duration"/> of wall-clock time excluded from every track.</summary>
public sealed record SessionGap(TimeSpan At, TimeSpan Duration);
