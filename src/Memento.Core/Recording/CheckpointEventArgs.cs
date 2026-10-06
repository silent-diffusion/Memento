namespace Memento.Core.Recording;

/// <summary>Every writer flushed and patched its header at <see cref="At"/>.</summary>
public sealed class CheckpointEventArgs(DateTimeOffset at, long elapsedMs, IReadOnlyList<SessionTrack> tracks) : EventArgs
{
    public DateTimeOffset At { get; } = at;

    public long ElapsedMs { get; } = elapsedMs;

    public IReadOnlyList<SessionTrack> Tracks { get; } = tracks;
}
