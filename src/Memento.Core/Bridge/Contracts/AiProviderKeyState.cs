namespace Memento.Core.Bridge.Contracts;

/// <summary>Whether a key is saved for one AI provider. The key itself never crosses the bridge.</summary>
public sealed record AiProviderKeyState(bool HasKey);
