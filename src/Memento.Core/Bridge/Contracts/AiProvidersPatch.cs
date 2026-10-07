namespace Memento.Core.Bridge.Contracts;

/// <summary>The cloud providers' models for <c>settings.set</c> (M4). Omitted providers keep their value.</summary>
public sealed record AiProvidersPatch
{
    public AiProviderPatch? Anthropic { get; init; }

    public AiProviderPatch? Openai { get; init; }
}
