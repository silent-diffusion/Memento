namespace Memento.Core.Bridge.Contracts;

/// <summary>
/// One Library row (DESIGN.md §4): type icon, title, meta line (type · people · when), status pills, duration.
/// </summary>
/// <param name="Id">Project folder id, e.g. <c>20261006-100000-k3f9ab</c>.</param>
/// <param name="Type">Recording type: <c>meeting</c>, <c>interview</c>, <c>lecture</c>, <c>presentation</c>, <c>dictation</c>, <c>research</c>, <c>general</c> or a custom type name.</param>
/// <param name="CreatedAt">Start of the recording, ISO 8601 with offset.</param>
/// <param name="ParticipantCount">People listed for the recording; 0 is a solo recording ("Just me"), 1 is "1 speaker".</param>
/// <param name="Stages">
/// Processing stages in pipeline order; empty when no processing has run ("Audio only").
/// A finished <c>stored</c> or <c>optimize</c> stage is not listed here (it would turn every row into a pill); while it runs or if it failed, it is.
/// </param>
/// <param name="People">Participant names (and, from M2, renamed speakers) for search and the meta line.</param>
/// <param name="IsProcessing">Any stage is active or queued.</param>
/// <param name="State"><c>recording</c>, <c>finalizing</c>, <c>ready</c>, <c>recovered</c> or <c>failed</c>.</param>
/// <param name="SizeBytes">Size of the project folder on disk, kept in the index and refreshed on every project write.</param>
public sealed record RecordingSummary(
    string Id,
    string Title,
    string Type,
    DateTimeOffset CreatedAt,
    long DurationMs,
    int ParticipantCount,
    bool HasVideo,
    IReadOnlyList<StageStatus> Stages,
    IReadOnlyList<string> People,
    bool IsProcessing,
    string State,
    long SizeBytes);
