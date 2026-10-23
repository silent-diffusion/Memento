using Memento.AI;
using Memento.Generation.Ai;

namespace Memento.Generation.Generation;

/// <summary>
/// Stands in for a provider where only its name, kind and token count are needed (the payload preview, the chunk count),
/// so previewing never constructs a real provider. It cannot generate.
/// </summary>
internal sealed class PreviewProvider(ProviderStatus status) : IAiProvider
{
    private readonly EstimatingTokenCounter _counter = status.Id switch
    {
        ProviderIds.Anthropic => EstimatingTokenCounter.Claude,
        ProviderIds.OpenAi => EstimatingTokenCounter.OpenAi,
        ProviderIds.Server => EstimatingTokenCounter.Server,
        _ => EstimatingTokenCounter.Generic,
    };

    public string Id => status.Id;

    public string DisplayName => status.Name;

    public AiProviderKind Kind => status.Id switch
    {
        ProviderIds.Local => AiProviderKind.Local,
        ProviderIds.Server => AiProviderKind.Server,
        _ => AiProviderKind.Cloud,
    };

    /// <summary>The model; for "Your own server" with its address ("qwen3:8b at localhost:11434"), as the preview names it.</summary>
    public string Model => status.IsServer ? status.ModelLabel ?? string.Empty : status.Model ?? string.Empty;

    public AiCapabilities Capabilities { get; } = new(
        status.Plan?.ContextTokens ?? (status.IsServer ? status.ServerContextTokens : status.Cloud?.ContextTokens) ?? 1_000_000,
        8000,
        true,
        status.Id == ProviderIds.Local,
        true,
        false);

    public int CountTokens(string text) => _counter.Count(text);

    public Task<AiReadiness> CheckAsync(CancellationToken cancellationToken) => Task.FromResult(AiReadiness.Ready());

    public Task<AiResponse> GenerateAsync(AiRequest request, IProgress<AiProgress>? progress, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("The preview provider never generates.");
}
