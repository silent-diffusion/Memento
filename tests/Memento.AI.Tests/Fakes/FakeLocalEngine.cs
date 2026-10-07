using Memento.AI.Local;

namespace Memento.AI.Tests.Fakes;

/// <summary>A scripted engine: answers each prompt with <see cref="Answer"/>, word by word; can block until cancelled.</summary>
internal sealed class FakeLocalEngine(LocalLlmJob job, FakeLocalEngineFactory owner) : ILocalLlmEngine
{
    public LocalLlmDeviceInfo Device { get; } = new(job.Device == LocalLlmDevices.Cpu ? "cpu" : "vulkan", "Fake GPU", 33, job.ContextTokens > 0 ? job.ContextTokens : 4096, 8);

    public double LoadMs => 12;

    public double WarmUpMs => 3;

    public long? DedicatedVramBytes => 3L << 30;

    public long? SharedVramGrowthBytes => 1L << 20;

    public int CountTokens(string text) => text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

    public async Task<LocalLlmOutput> GenerateAsync(int index, LocalLlmPrompt prompt, Action<string>? onDelta, CancellationToken cancellationToken)
    {
        owner.Prompts.Add(prompt);
        if (owner.BlockUntilCancelled)
        {
            owner.Started.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        var promptTokens = CountTokens(prompt.System) + prompt.Messages.Sum(m => CountTokens(m.Content));
        if (promptTokens + prompt.MaxTokens > Device.ContextTokens)
        {
            return new LocalLlmOutput(index, string.Empty, LocalLlmStopReasons.ContextFull, promptTokens, 0, 1, 0);
        }

        var (text, stop) = owner.Answer(prompt);
        var words = text.Split(' ');
        for (var i = 0; i < words.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            onDelta?.Invoke(i == 0 ? words[i] : " " + words[i]);
        }

        return new LocalLlmOutput(index, text, stop, promptTokens, words.Length, 5, 20);
    }

    public void Dispose() => owner.Disposed++;
}
