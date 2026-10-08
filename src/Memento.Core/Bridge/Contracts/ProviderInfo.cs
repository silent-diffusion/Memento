namespace Memento.Core.Bridge.Contracts;

/// <summary>One AI provider and whether a generation could start with it now (<c>providers.list</c>).</summary>
/// <param name="Id"><c>anthropic</c>, <c>openai</c> or <c>local</c>.</param>
/// <param name="Name">"Claude", "ChatGPT", "Local model".</param>
/// <param name="Kind"><c>cloud</c> or <c>local</c>.</param>
/// <param name="Reason">Why it is not ready ("External AI is off", "No key saved", "Model not installed"); <c>null</c> when ready.</param>
/// <param name="ModelLabel">The model requests go to ("claude-opus-5-5", "Qwen3.5 4B · graphics card").</param>
public sealed record ProviderInfo(string Id, string Name, string Vendor, string Kind, bool Ready, string? Reason, string? ModelLabel)
{
    /// <summary>The error code a generation would be refused with (<c>ai.disabled</c>, <c>ai.noKey</c>, <c>ai.modelNotInstalled</c>, <c>ai.notEnoughVram</c>).</summary>
    public string? Code { get; init; }

    /// <summary>The specific sentence for the card or the dialog (DESIGN.md §17), or a note when ready ("runs on the graphics card").</summary>
    public string? Detail { get; init; }

    /// <summary>The catalog id of the local model to download when <see cref="Code"/> is <c>ai.modelNotInstalled</c>.</summary>
    public string? ModelId { get; init; }

    /// <summary>Local only: the discrete graphics card's memory and who holds it; <c>null</c> for cloud providers and on PCs without a card.</summary>
    public GpuMemoryInfo? GpuMemory { get; init; }

    /// <summary>
    /// Local only: why a graphics-card model does not run on the card now ("The graphics card has 0.8 GB of 6 GB free. Ollama
    /// (llama-server.exe) is using 5.0 GB. Qwen3.5 4B needs 3.6 GB on the card, so it runs on the processor …"); it also
    /// starts <see cref="Detail"/>. <c>null</c> when the model fits or runs on the processor by design.
    /// </summary>
    public string? GpuNote { get; init; }
}
