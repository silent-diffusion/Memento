namespace Memento.Core.Recording;

/// <summary>A track ended because its device went away.</summary>
public sealed class SourceLostEventArgs(SessionTrack track, long atMs, IReadOnlyList<SessionTrack> remaining) : EventArgs
{
    public SessionTrack Track { get; } = track;

    public long AtMs { get; } = atMs;

    /// <summary>Tracks still recording.</summary>
    public IReadOnlyList<SessionTrack> Remaining { get; } = remaining;
}
