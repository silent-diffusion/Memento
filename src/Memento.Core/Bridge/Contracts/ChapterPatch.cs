namespace Memento.Core.Bridge.Contracts;

/// <summary><c>Partial&lt;Chapter&gt;</c>. Add ignores <see cref="Id"/> (the host assigns it); update requires it.</summary>
public sealed record ChapterPatch
{
    public string? Id { get; init; }

    public long? AtMs { get; init; }

    public string? Title { get; init; }

    public string? Origin { get; init; }
}
