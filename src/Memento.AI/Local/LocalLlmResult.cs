namespace Memento.AI.Local;

/// <summary>The result of a <see cref="LocalLlmJob"/>: one output per prompt (or token counts), and how the load went.</summary>
/// <param name="DedicatedVramBytes">This process's dedicated GPU memory after loading, when it could be read.</param>
/// <param name="SharedVramGrowthBytes">The largest growth of this process's shared GPU memory seen during the job.</param>
public sealed record LocalLlmResult(
    IReadOnlyList<LocalLlmOutput> Outputs,
    LocalLlmDeviceInfo Device,
    double LoadMs,
    double WarmUpMs,
    long? DedicatedVramBytes = null,
    long? SharedVramGrowthBytes = null,
    IReadOnlyList<int>? TokenCounts = null);
