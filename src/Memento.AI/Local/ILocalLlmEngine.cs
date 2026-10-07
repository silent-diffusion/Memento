namespace Memento.AI.Local;

/// <summary>
/// A loaded local model (weights plus one context). Calls are serialised by the engine (one context is not
/// thread-safe). Disposing unloads it: the context first, then the weights, so video memory is released.
/// </summary>
public interface ILocalLlmEngine : IDisposable
{
    LocalLlmDeviceInfo Device { get; }

    double LoadMs { get; }

    double WarmUpMs { get; }

    /// <summary>This process's dedicated GPU memory after loading, when it could be read.</summary>
    long? DedicatedVramBytes { get; }

    /// <summary>The largest growth of this process's shared GPU memory seen since before loading.</summary>
    long? SharedVramGrowthBytes { get; }

    /// <summary>Exact tokens of <paramref name="text"/> for this model's tokenizer.</summary>
    int CountTokens(string text);

    /// <summary>Generates one answer; reports each decoded piece through <paramref name="onDelta"/>.</summary>
    /// <exception cref="OperationCanceledException">Cancelled (prompt reading stops within about half a second).</exception>
    /// <exception cref="LocalLlmException">The spill watch or the engine stopped the generation.</exception>
    Task<LocalLlmOutput> GenerateAsync(int index, LocalLlmPrompt prompt, Action<string>? onDelta, CancellationToken cancellationToken);
}
