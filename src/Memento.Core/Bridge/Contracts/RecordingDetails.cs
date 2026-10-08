namespace Memento.Core.Bridge.Contracts;

/// <summary>The Details sheet (DESIGN.md §14). <see cref="WhoSpoke"/> is the recording's own speaker count and names.</summary>
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
    Agenda Agenda,
    WhoSpoke WhoSpoke);
