namespace Memento.Core.Bridge.Contracts;

/// <summary>The AI providers in the settings snapshot (read side only): which have a saved key, and "Your own server" (2.5.0).</summary>
public sealed record AiProvidersSnapshot(AiProviderKeyState Anthropic, AiProviderKeyState Openai)
{
    /// <summary>2.5.0: "Your own server": whether a key is saved, its address, model and context, and where it runs.</summary>
    public AiServerState Server { get; set; } = new(false);
}
