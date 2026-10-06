namespace Memento.Audio.Writing;

/// <summary>
/// A pause excluded from a track: <paramref name="At"/> is the position in the written track where the
/// pause happened, <paramref name="Duration"/> is how long the pause lasted on the clock.
/// </summary>
public sealed record TrackGap(long AtFrame, TimeSpan At, TimeSpan Duration, long PausedAtQpc, long ResumedAtQpc);
