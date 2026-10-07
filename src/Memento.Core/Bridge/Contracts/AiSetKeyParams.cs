namespace Memento.Core.Bridge.Contracts;

/// <summary>Parameters of <c>ai.setKey</c>. The key is stored with DPAPI and never returned, logged or exported.</summary>
public sealed record AiSetKeyParams
{
    /// <summary><c>anthropic</c> or <c>openai</c>.</summary>
    public required string Provider { get; init; }

    public required string Key { get; init; }
}
