namespace Memento.AI.Local;

/// <summary>Runs jobs on a <see cref="LocalLlmJobRunner"/> in this process (tests, tools, and inside the worker itself).</summary>
public sealed class InProcessLocalLlmJobClient(LocalLlmJobRunner runner) : ILocalLlmJobClient
{
    public Task<LocalLlmResult> RunAsync(LocalLlmJob job, IProgress<LocalLlmWorkerReply>? progress, CancellationToken cancellationToken) =>
        runner.RunAsync(job, progress is null ? null : progress.Report, cancellationToken);
}
