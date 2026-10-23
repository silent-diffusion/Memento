namespace Memento.Core.Bridge.Contracts;

/// <summary>Parameters of <c>ai.setKey</c>. The key is stored with DPAPI and never returned, logged or exported.</summary>
public sealed record AiSetKeyParams
{
    /// <summary><c>anthropic</c>, <c>openai</c> or <c>server</c> (2.5.0, "Your own server").</summary>
    public required string Provider { get; init; }

    public required string Key { get; init; }
}
