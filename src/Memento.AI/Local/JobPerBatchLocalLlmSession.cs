namespace Memento.AI.Local;

/// <summary>A session for clients that cannot keep a model loaded: every batch is its own job.</summary>
internal sealed class JobPerBatchLocalLlmSession(ILocalLlmJobClient client, LocalLlmJob job) : ILocalLlmSession
{
    public Task<LocalLlmResult> RunAsync(IReadOnlyList<LocalLlmPrompt> prompts, IProgress<LocalLlmWorkerReply>? progress, CancellationToken cancellationToken) =>
        client.RunAsync(job with { Prompts = prompts, Session = false }, progress, cancellationToken);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
