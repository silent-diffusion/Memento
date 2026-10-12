namespace Memento.Core.Bridge.Contracts;

/// <summary>A model Settings › AI and privacy lists under a provider's key (BRIDGE.md "Settings (M4)").</summary>
/// <param name="Id">The model id sent to the provider ("claude-haiku-5-5").</param>
/// <param name="Note">The plain-language note beside it ("The fastest and cheapest.").</param>
/// <param name="ContextTokens">Prompt plus answer tokens one request may use.</param>
public sealed record AiModelChoice(string Id, string Note, int ContextTokens);
