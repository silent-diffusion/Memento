using System.Text.Json;
using Memento.Core.Workers;

namespace Memento.AI.Local;

/// <summary>
/// One worker job that keeps the local model loaded (<see cref="LocalLlmJob.Session"/>): each <see cref="RunAsync"/>
/// sends a <c>prompts</c> line and waits for its <c>batch</c> line; disposing sends <c>end</c> and waits for the worker
/// to unload and exit. Batches run one at a time.
/// </summary>
internal sealed class WorkerLlmSession(string modelName) : ILocalLlmSession
{
    private readonly SemaphoreSlim _one = new(1, 1);
    private WorkerSession? _worker;
    private TaskCompletionSource<LocalLlmResult>? _pending;
    private IProgress<LocalLlmWorkerReply>? _progress;

    internal void Attach(WorkerSession worker) => _worker = worker;

    /// <summary>The worker's non-final lines: a batch's answers, or its progress.</summary>
    internal Task OnLineAsync(WorkerReply line)
    {
        if (line.Type == WorkerMessageTypes.Batch)
        {
            try
            {
                _pending?.TrySetResult(WorkerLocalLlmJobClient.ResultOf(line, modelName));
            }
            catch (LocalLlmException ex)
            {
                _pending?.TrySetException(ex);
            }
        }
        else if (WorkerLocalLlmJobClient.ToLocal(line) is { } local)
        {
            _progress?.Report(local);
        }

        return Task.CompletedTask;
    }

    public async Task<LocalLlmResult> RunAsync(IReadOnlyList<LocalLlmPrompt> prompts, IProgress<LocalLlmWorkerReply>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prompts);
        var worker = _worker ?? throw new InvalidOperationException("The session has not started.");
        await _one.WaitAsync(cancellationToken);
        try
        {
            var pending = new TaskCompletionSource<LocalLlmResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending = pending;
            _progress = progress;
            if (!worker.Completion.IsCompleted)
            {
                var body = JsonSerializer.SerializeToElement(new LocalLlmPromptBatch(prompts), LocalLlmJsonContext.Default.LocalLlmPromptBatch);
                try
                {
                    await worker.SendAsync(new WorkerCommand(WorkerMessageTypes.Prompts, Llm: body));
                }
                catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
                {
                    // The worker has gone; its completion says why.
                }
            }

            await using var registration = cancellationToken.Register(() => pending.TrySetCanceled(cancellationToken));
            var first = await Task.WhenAny(pending.Task, worker.Completion);
            if (first == pending.Task)
            {
                return await pending.Task;
            }

            try
            {
                await worker.Completion;
            }
            catch (Exception ex) when (WorkerLocalLlmJobClient.Map(ex, modelName) is { } mapped)
            {
                throw mapped;
            }

            // The job ended with a result before answering: it was never meant to.
            throw new LocalLlmException(AiErrors.WorkerCrashed(LocalAiProvider.ProviderName, modelName, "the worker ended without an answer"));
        }
        finally
        {
            _pending = null;
            _progress = null;
            _one.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_worker is not { } worker)
        {
            return;
        }

        if (!worker.Completion.IsCompleted)
        {
            try
            {
                await worker.SendAsync(new WorkerCommand(WorkerMessageTypes.End));
                await worker.Completion.WaitAsync(TimeSpan.FromSeconds(15));
            }
#pragma warning disable CA1031 // Ending a session never fails the generation that used it; the worker is stopped below.
            catch (Exception)
#pragma warning restore CA1031
            {
            }
        }

        await worker.DisposeAsync();
        _one.Dispose();
    }
}
