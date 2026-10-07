namespace Memento.AI.Local;

/// <summary>A local model loaded once for several batches of prompts (a whole generation); disposing it unloads the model.</summary>
public interface ILocalLlmSession : IAsyncDisposable
{
    /// <summary>Runs the prompts in order on the loaded model; the first batch also waits for the model to load.</summary>
    /// <param name="progress">Receives this batch's <c>device</c> and <c>progress</c> lines in order.</param>
    /// <exception cref="LocalLlmException">The job failed (the session is over).</exception>
    /// <exception cref="OperationCanceledException">Cancelled (the session is over).</exception>
    Task<LocalLlmResult> RunAsync(IReadOnlyList<LocalLlmPrompt> prompts, IProgress<LocalLlmWorkerReply>? progress, CancellationToken cancellationToken);
}
