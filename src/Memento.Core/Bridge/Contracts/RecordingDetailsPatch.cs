namespace Memento.Core.Bridge.Contracts;

/// <summary><c>Partial&lt;RecordingDetails&gt;</c>: omitted or <c>null</c> fields keep their value. Lists and the agenda replace whole.</summary>
public sealed record RecordingDetailsPatch
{
    public string? Title { get; init; }

    public string? Type { get; init; }

    public IReadOnlyList<string>? Participants { get; init; }

    public string? Purpose { get; init; }

    public string? Platform { get; init; }

    public string? Organization { get; init; }

    public string? Location { get; init; }

    public string? Notes { get; init; }

    public IReadOnlyList<string>? Tags { get; init; }

    public Agenda? Agenda { get; init; }
}
