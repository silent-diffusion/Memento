using Memento.Core.Bridge.Contracts;

namespace Memento.Generation.Generation;

/// <summary>What the pipeline knows from the recording's data, without AI: the title, the purpose from the details, the
/// people an owner may be, the agenda with the coverage the user marked, and the highlighted segments.</summary>
public sealed record GenerationFacts(
    string? Title,
    string? Purpose,
    IReadOnlyList<string> People,
    IReadOnlyList<AgendaItem> Agenda,
    IReadOnlyList<string> QuoteCandidates)
{
    /// <summary>The agenda was part of the payload, so its coverage is checked against the transcript.</summary>
    public bool AgendaChecked { get; init; } = true;
}
