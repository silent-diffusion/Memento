using Memento.Core.Bridge.Contracts;
using Memento.Core.Processing;
using Memento.Core.Projects;
using Memento.Core.Settings;

namespace Memento.Core.Tests.Fakes;

/// <summary>A processing stage for orchestrator tests: records its runs and does what <see cref="Behaviour"/> says.</summary>
internal sealed class FakeStage(string name, int order, bool heavy, StageStatusWriter status) : IProcessingStage
{
    private readonly object _gate = new();
    private readonly List<string> _runs = [];

    public string Name { get; } = name;

    public int Order { get; } = order;

    public bool IsHeavy { get; } = heavy;

    public bool Enabled { get; set; } = true;

    /// <summary>Called for each run; the default marks the stage done.</summary>
    public Func<StageRun, CancellationToken, Task>? Behaviour { get; set; }

    public List<string> Runs
    {
        get
        {
            lock (_gate)
            {
                return [.. _runs];
            }
        }
    }

    /// <summary>Every run across stages, in order, shared by the stages of one test.</summary>
    public List<string>? Log { get; set; }

    /// <summary>What <see cref="UsesGpuAsync"/> answers: the stage runs on the graphics card.</summary>
    public bool OnGpu { get; set; }

    public Task<bool> UsesGpuAsync(string recordingId, CancellationToken cancellationToken) => Task.FromResult(OnGpu);

    public bool AppliesTo(AppSettings settings) => Enabled;

    public async Task RunAsync(StageRun run, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _runs.Add(run.RecordingId);
        }

        if (Log is { } log)
        {
            lock (log)
            {
                log.Add(Name);
            }
        }

        await status.SetAsync(run.RecordingId, new StageStatus(Name, StageStates.Active, 0, "0%"), cancellationToken);
        if (Behaviour is { } behaviour)
        {
            await behaviour(run, cancellationToken);
            return;
        }

        await status.SetAsync(run.RecordingId, new StageStatus(Name, StageStates.Done, null, "Done"), cancellationToken);
    }

    public Task FailAsync(StageRun run, string message, params Remedy[] remedies) =>
        status.FailAsync(
            run.RecordingId,
            new ProjectStageFailure(Name, message, "Kept everything.", remedies, ProjectStageFailure.CauseEngine, DateTimeOffset.Now),
            "Failed",
            CancellationToken.None);
}
