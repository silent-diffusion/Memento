namespace Memento.Audio.Recording;

/// <summary>Levels of every active track, published at most 30 times per second.</summary>
public sealed class LevelsEventArgs(IReadOnlyList<TrackLevel> levels) : EventArgs
{
    public IReadOnlyList<TrackLevel> Levels { get; } = levels;
}
