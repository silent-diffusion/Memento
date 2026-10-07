namespace Memento.Core.Bridge.Contracts;

/// <summary>Parameters of <c>ai.clearKey</c>.</summary>
public sealed record AiProviderParams
{
    /// <summary><c>anthropic</c> or <c>openai</c>.</summary>
    public required string Provider { get; init; }
}
