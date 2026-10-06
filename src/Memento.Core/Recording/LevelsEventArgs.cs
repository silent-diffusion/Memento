using Memento.Core.Bridge.Contracts;

namespace Memento.Core.Recording;

/// <summary>One meter reading per open track.</summary>
public sealed class LevelsEventArgs(IReadOnlyList<SourceLevel> levels) : EventArgs
{
    public IReadOnlyList<SourceLevel> Levels { get; } = levels;
}
