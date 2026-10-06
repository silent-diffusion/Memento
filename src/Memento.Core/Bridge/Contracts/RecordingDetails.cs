namespace Memento.Core.Bridge.Contracts;

/// <summary>The Details sheet (DESIGN.md §14).</summary>
public sealed record RecordingDetails(
    string Title,
    string Type,
    IReadOnlyList<string> Participants,
    string Purpose,
    string Platform,
    string Organization,
    string Location,
    string Notes,
    IReadOnlyList<string> Tags,
    Agenda Agenda);
