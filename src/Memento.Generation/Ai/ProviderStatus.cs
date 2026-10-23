using Memento.AI.Local;
using Memento.Core.Ai;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Settings;

namespace Memento.Generation.Ai;

/// <summary>
/// Whether a provider could run a generation now, worked out from Settings, the key store, the model manager and the free
/// video memory only (no provider is constructed and nothing is sent), and the budgets the pipeline uses with it.
/// </summary>
/// <param name="Code">Why not: <c>ai.disabled</c>, <c>ai.noKey</c>, <c>ai.modelNotInstalled</c>, <c>ai.notEnoughVram</c>; for "Your own server" <c>ai.noServer</c>, <c>ai.serverInsecure</c>, <c>ai.noModel</c>.</param>
/// <param name="Reason">The short card text ("External AI is off", "No key saved", "Model not installed").</param>
/// <param name="Detail">The full sentence (DESIGN.md §17), or a note when ready.</param>
/// <param name="Model">The cloud model id, or the local catalog id.</param>
/// <param name="ModelLabel">"claude-opus-5-5", "Qwen3.5 4B · graphics card", "qwen3:8b at localhost:11434".</param>
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

    /// <summary>Claude or ChatGPT (a model from Memento's list, a cloud HTTP client). Consent is keyed on <see cref="LeavesPc"/>, not on this.</summary>
    public bool IsCloud => ProviderIds.IsCloud(Id);

    /// <summary>"Your own server" (2.5.0).</summary>
    public bool IsServer => Id == ProviderIds.Server;

    /// <summary>The bridge's <c>kind</c>: <c>cloud</c>, <c>server</c> or <c>local</c>.</summary>
    public string Kind => ProviderIds.Kind(Id);

    /// <summary>
    /// A generation with this provider takes the inputs off this PC: Claude and ChatGPT always, the local model never,
    /// "Your own server" unless its address is this PC (2.5.0). Every consent and "nothing leaves the PC" decision keys on
    /// this: "Allow external AI services", "Ask before every send", the share switches, the preview's "stays on this PC"
    /// and the record's <c>stayedOnPc</c>.
    /// </summary>
    public bool LeavesPc => Id switch
    {
        ProviderIds.Local => false,
        ProviderIds.Server => ServerAddress?.LeavesPc ?? false,
        _ => true,
    };

    /// <summary>"Your own server" only: the address from Settings, parsed; <c>null</c> when none is set.</summary>
    public ServerAddress? ServerAddress { get; init; }

    /// <summary>"Your own server" only: the context the server runs the model with (Settings).</summary>
    public int ServerContextTokens { get; init; } = AiServerSettings.DefaultContextTokens;

    /// <summary>Local only: the discrete card's memory and who holds it, when there is a card.</summary>
    public GpuMemoryInfo? GpuMemory { get; init; }

    /// <summary>
    /// Local only: why a graphics-card model does not run on the card now (DESIGN.md §17), also the start of
    /// <see cref="Detail"/>; <c>null</c> when it fits or the model is a processor one.
    /// </summary>
    public string? GpuNote { get; init; }

    /// <summary>Local, a larger model: <see cref="MemoryPlanner"/>'s placement (2.2); <c>null</c> for the small models.</summary>
    public LocalMemoryPlan? MemoryPlan { get; init; }

    /// <summary>Local, a larger model: the plan as Settings and the Builder show it.</summary>
    public LocalModelPlanInfo? PlanInfo { get; init; }

    /// <summary>Cloud only: the model chosen in Settings, its context and what is sent with it (after 2.1).</summary>
    public CloudModel? Cloud { get; init; }

    /// <summary>The most transcript tokens one cloud request carries; a quarter of the model's context when that is less.</summary>
    public const int CloudChunkTokens = 24_000;

    /// <summary>
    /// Transcript tokens per chunk: the model profile scaled to the context it gets (Local), or 24,000 for a cloud model,
    /// less when a quarter of the chosen model's context is smaller (the transcript is split, never refused for its length).
    /// </summary>
    public int ChunkTokens => LocalModel is { } entry && Plan is { } plan
        ? Math.Max(256, entry.Llm.ChunkTokens * plan.ContextTokens / Math.Max(1, entry.Llm.ContextTokens))
        : Math.Max(256, Math.Min(CloudChunkTokens, (IsServer ? ServerContextTokens : Cloud?.ContextTokens ?? int.MaxValue) / 4));

    /// <summary>
    /// The answer limit of a map request: about a quarter of a local context, at most 1,400 tokens (the spike's); for "Your
    /// own server" a quarter of its context (at most 8,000), so a chunk, the instructions and the answer fit together.
    /// </summary>
    public int MapOutputTokens => Plan is { } plan ? Math.Min(1400, plan.ContextTokens / 4)
        : IsServer ? Math.Min(8000, Math.Max(512, ServerContextTokens / 4))
        : 8000;
}
