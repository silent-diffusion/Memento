using Memento.AI.Local;

namespace Memento.Generation.Ai;

/// <summary>
/// Whether a provider could run a generation now, worked out from Settings, the key store, the model manager and the free
/// video memory only (no provider is constructed and nothing is sent), and the budgets the pipeline uses with it.
/// </summary>
/// <param name="Code">Why not: <c>ai.disabled</c>, <c>ai.noKey</c>, <c>ai.modelNotInstalled</c>, <c>ai.notEnoughVram</c>.</param>
/// <param name="Reason">The short card text ("External AI is off", "No key saved", "Model not installed").</param>
/// <param name="Detail">The full sentence (DESIGN.md §17), or a note when ready.</param>
/// <param name="Model">The cloud model id, or the local catalog id.</param>
/// <param name="ModelLabel">"claude-opus-5-5", "Qwen3.5 4B · graphics card".</param>
/// <param name="Plan">Local only: how the model would be loaded now.</param>
public sealed record ProviderStatus(
    string Id,
    bool Ready,
    string? Code,
    string? Reason,
    string? Detail,
    string? Model,
    string? ModelLabel,
    LocalModelEntry? LocalModel = null,
    string? LocalPath = null,
    LocalLlmPlan? Plan = null)
{
    public string Name => ProviderIds.DisplayName(Id);

    public bool IsCloud => ProviderIds.IsCloud(Id);

    /// <summary>Transcript tokens per chunk: the model profile scaled to the context it gets (Local), or 24,000 (cloud).</summary>
    public int ChunkTokens => LocalModel is { } entry && Plan is { } plan
        ? Math.Max(256, entry.Llm.ChunkTokens * plan.ContextTokens / Math.Max(1, entry.Llm.ContextTokens))
        : 24_000;

    /// <summary>The answer limit of a map request: about a quarter of a local context, at most 1,400 tokens (the spike's).</summary>
    public int MapOutputTokens => Plan is { } plan ? Math.Min(1400, plan.ContextTokens / 4) : 8000;
}
