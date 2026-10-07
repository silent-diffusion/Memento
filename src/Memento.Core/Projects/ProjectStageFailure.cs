using Memento.Core.Bridge.Contracts;

namespace Memento.Core.Projects;

/// <summary>Why a stage last failed, kept in <c>project.json</c> until it is retried or succeeds.</summary>
/// <param name="Cause">Machine-readable cause for automatic retries: <c>noModel</c>, <c>crashed</c>, <c>cancelled</c>, <c>engine</c>, <c>noWorker</c>.</param>
public sealed record ProjectStageFailure(string Stage, string Message, string Kept, IReadOnlyList<Remedy> Remedies, string Cause, DateTimeOffset At)
{
    public const string CauseNoModel = "noModel";
    public const string CauseCrashed = "crashed";
    public const string CauseCancelled = "cancelled";
    public const string CauseEngine = "engine";
    public const string CauseNoWorker = "noWorker";
    public const string CauseNoTranscript = "noTranscript";

    public StageFailure ToContract() => new(Stage, Message, Kept, Remedies);
}
