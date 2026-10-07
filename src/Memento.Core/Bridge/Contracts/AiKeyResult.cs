namespace Memento.Core.Bridge.Contracts;

/// <summary>Result of <c>ai.setKey</c> and <c>ai.clearKey</c>: whether a key is saved now (never the key).</summary>
public sealed record AiKeyResult(bool HasKey);
