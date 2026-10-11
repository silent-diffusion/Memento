namespace Memento.Core.Bridge.Contracts;

/// <summary>Parameters of <c>recording.start</c>. An empty title becomes "Untitled {type} · {date}" (2.1, Settings › General › Date format).</summary>
public sealed record RecordingStartParams
{
    public required string Title { get; init; }

    public required string Type { get; init; }

    public required IReadOnlyList<string> SourceIds { get; init; }
}
