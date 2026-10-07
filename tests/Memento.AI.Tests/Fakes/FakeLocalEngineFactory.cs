using System.Collections.Concurrent;
using Memento.AI.Local;

namespace Memento.AI.Tests.Fakes;

/// <summary>Creates <see cref="FakeLocalEngine"/>s; can fail the load with a scripted error.</summary>
internal sealed class FakeLocalEngineFactory : ILocalLlmEngineFactory
{
    public Func<LocalLlmPrompt, (string Text, string Stop)> Answer { get; set; } = _ => ("{\"ok\": true}", LocalLlmStopReasons.EndOfGeneration);

    public AiError? FailLoad { get; set; }

    public bool BlockUntilCancelled { get; set; }

    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ConcurrentBag<LocalLlmPrompt> Prompts { get; } = [];

    public int Loads { get; private set; }

    public int Disposed { get; set; }

    public Task<ILocalLlmEngine> LoadAsync(LocalLlmJob job, Action<LocalLlmProgress>? progress, CancellationToken cancellationToken)
    {
        Loads++;
        if (FailLoad is { } error)
        {
            throw new LocalLlmException(error);
        }

        progress?.Invoke(new LocalLlmProgress(LocalLlmProgress.WarmingUp, -1, job.Prompts.Count));
        return Task.FromResult<ILocalLlmEngine>(new FakeLocalEngine(job, this));
    }
}
