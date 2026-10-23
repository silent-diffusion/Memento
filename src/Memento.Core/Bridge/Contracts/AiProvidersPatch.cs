namespace Memento.Core.Bridge.Contracts;

/// <summary>The providers' settings for <c>settings.set</c> (M4; "Your own server" from 2.5.0). Omitted providers keep their value.</summary>
public sealed record AiProvidersPatch
{
    public AiProviderPatch? Anthropic { get; init; }

    public AiProviderPatch? Openai { get; init; }

    /// <summary>2.5.0: the server's address, model and context.</summary>
    public AiServerPatch? Server { get; init; }
}
