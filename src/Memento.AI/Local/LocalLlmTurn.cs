namespace Memento.AI.Local;

/// <summary>A conversation turn in a <see cref="LocalLlmPrompt"/>: <c>user</c> or <c>assistant</c>.</summary>
public sealed record LocalLlmTurn(string Role, string Content)
{
    public const string User = "user";
    public const string Assistant = "assistant";
}
