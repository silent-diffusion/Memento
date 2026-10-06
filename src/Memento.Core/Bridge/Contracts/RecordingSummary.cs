namespace Memento.Core.Bridge.Contracts;

/// <summary>
/// One Library row (DESIGN.md §4): type icon, title, meta line (type · people · when), status pills, duration.
/// </summary>
/// <param name="Id">Project folder id, e.g. <c>20261006-100000-k3f9ab</c>.</param>
/// <param name="Type">Recording type: <c>meeting</c>, <c>interview</c>, <c>lecture</c>, <c>presentation</c>, <c>dictation</c>, <c>research</c> or a custom type name.</param>
/// <param name="CreatedAt">Start of the recording, ISO 8601 with offset.</param>
/// <param name="ParticipantCount">Number of people; the UI words it ("5 people", "1 speaker", "Just me").</param>
/// <param name="Stages">Processing stages in pipeline order; empty when no processing has run ("Audio only").</param>
public sealed record RecordingSummary(
    string Id,
    string Title,
    string Type,
    DateTimeOffset CreatedAt,
    long DurationMs,
    int ParticipantCount,
    bool HasVideo,
    IReadOnlyList<StageStatus> Stages);
