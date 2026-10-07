namespace Memento.Core.Bridge.Contracts;

/// <summary>The AI providers in the settings snapshot (read side only): which have a saved key.</summary>
public sealed record AiProvidersSnapshot(AiProviderKeyState Anthropic, AiProviderKeyState Openai);
