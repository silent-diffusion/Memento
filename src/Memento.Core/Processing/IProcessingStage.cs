using Memento.Core.Bridge.Contracts;
using Memento.Core.Settings;

namespace Memento.Core.Processing;

/// <summary>
/// A processing stage the orchestrator runs after <c>stored</c> (ARCHITECTURE.md §6). A stage keeps its own state in
/// the manifest (active with progress, done, failed with a <c>ProjectStageFailure</c>), writes partial results, and is
/// restartable: running it again continues or redoes the work. Feature projects register their stages; Core has
/// <see cref="OptimizeStage"/>.
/// </summary>
public interface IProcessingStage
{
    /// <summary>One of <c>StageNames</c>.</summary>
    string Name { get; }

    /// <summary>Pipeline position; lower runs first (<c>optimize</c> is always last).</summary>
    int Order { get; }

    /// <summary>Uses the GPU or most of the processor, so it waits while the processing gate is closed.</summary>
    bool IsHeavy { get; }

    /// <summary>
    /// Whether this stage would run on the graphics card for <paramref name="recordingId"/> now. A GPU pass is not
    /// paused for a busy processor (<see cref="Engines.ProcessingGate.SetHeavyOnGpu"/>). Stages on the processor keep
    /// the default.
    /// </summary>
    Task<bool> UsesGpuAsync(string recordingId, CancellationToken cancellationToken) => Task.FromResult(false);

    /// <summary>Whether a recording stored now gets this stage with these settings.</summary>
    bool AppliesTo(AppSettings settings);

    /// <summary>
    /// Runs the stage. Records its own failures (and returns); throws <see cref="OperationCanceledException"/> when its
    /// token is cancelled, after reading <see cref="StageRun.StopReason"/> to record a user cancel itself if it wants.
    /// </summary>
    Task RunAsync(StageRun run, CancellationToken cancellationToken);
}
