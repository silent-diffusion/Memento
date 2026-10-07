namespace Memento.AI.Local;

/// <summary>
/// Runs a local job somewhere: in Memento.Worker (the integration wraps Core's <c>WorkerClient</c>), over any
/// JSON-lines channel (<see cref="JsonLinesLocalLlmJobClient"/>), or in-process for tests
/// (<see cref="InProcessLocalLlmJobClient"/>).
/// </summary>
public interface ILocalLlmJobClient
{
    /// <param name="progress">Receives <c>device</c> and <c>progress</c> lines in order.</param>
    /// <exception cref="LocalLlmException">The job failed; <see cref="AiErrorCodes.WorkerCrashed"/> when the worker ended without an answer.</exception>
    /// <exception cref="OperationCanceledException">Cancelled.</exception>
    Task<LocalLlmResult> RunAsync(LocalLlmJob job, IProgress<LocalLlmWorkerReply>? progress, CancellationToken cancellationToken);

    /// <summary>
    /// Opens a session that keeps the model of <paramref name="job"/> loaded for several batches of prompts (its own
    /// prompts are ignored). Clients without a session run each batch as its own job, which loads the model each time.
    /// </summary>
    Task<ILocalLlmSession> OpenAsync(LocalLlmJob job, CancellationToken cancellationToken) =>
        Task.FromResult<ILocalLlmSession>(new JobPerBatchLocalLlmSession(this, job));
}
