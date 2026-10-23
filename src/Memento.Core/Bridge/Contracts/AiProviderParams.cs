namespace Memento.Core.Bridge.Contracts;

/// <summary>Parameters of <c>ai.clearKey</c>.</summary>
public sealed record AiProviderParams
{
    /// <summary><c>anthropic</c>, <c>openai</c> or <c>server</c> (2.5.0, "Your own server").</summary>
    public required string Provider { get; init; }
}
