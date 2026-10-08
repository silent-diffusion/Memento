using Memento.AI.Local;

namespace Memento.Generation.Tests.Support;

/// <summary>
/// A local engine (and its factory) for the real <see cref="LocalAiProvider"/> in this process: answers every prompt with
/// <see cref="Answer"/>, decoded word by word as llama.cpp hands out pieces.
/// </summary>
internal sealed class ScriptedLocalEngine : ILocalLlmEngineFactory, ILocalLlmEngine
{
    public Func<LocalLlmPrompt, string> Answer { get; set; } = p => "{\"answer\": \"" + p.Purpose + "\"}";

    public LocalLlmDeviceInfo Device { get; } = new("cpu", "Test processor", 0, 8192, 4);

    public double LoadMs => 10;

    public double WarmUpMs => 2;

    public long? DedicatedVramBytes => null;

    public long? SharedVramGrowthBytes => null;

    public Task<ILocalLlmEngine> LoadAsync(LocalLlmJob job, Action<LocalLlmProgress>? progress, CancellationToken cancellationToken) =>
        Task.FromResult<ILocalLlmEngine>(this);

    public int CountTokens(string text) => text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

    public Task<LocalLlmOutput> GenerateAsync(int index, LocalLlmPrompt prompt, Action<string>? onDelta, CancellationToken cancellationToken)
    {
        var text = Answer(prompt);
        var words = text.Split(' ');
        for (var i = 0; i < words.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            onDelta?.Invoke(i == 0 ? words[i] : " " + words[i]);
        }

        var promptTokens = CountTokens(prompt.System) + prompt.Messages.Sum(m => CountTokens(m.Content));
        return Task.FromResult(new LocalLlmOutput(index, text, LocalLlmStopReasons.EndOfGeneration, promptTokens, words.Length, 4, 40));
    }

    public void Dispose()
    {
    }
}
