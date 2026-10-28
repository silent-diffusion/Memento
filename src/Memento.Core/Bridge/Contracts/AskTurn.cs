namespace Memento.Core.Bridge.Contracts;

/// <summary>
/// One turn of a recording's Ask conversation (2.9.0), as kept in <c>ask/conversation.json</c> and sent to the UI.
/// </summary>
/// <param name="Id">Stable within the conversation (<c>t1</c>, <c>t2</c>…).</param>
/// <param name="Role"><c>user</c> or <c>assistant</c>.</param>
/// <param name="Text">The question, or the answer as the model wrote it (citations as <c>[12]</c>).</param>
/// <param name="At">When it was asked or answered.</param>
public sealed record AskTurn(string Id, string Role, string Text, DateTimeOffset At)
{
    /// <summary>Assistant: the provider id (<c>local</c>, <c>server</c>, <c>anthropic</c>, <c>openai</c>).</summary>
    public string? ProviderId { get; init; }

    /// <summary>Assistant: "Claude", "Local model".</summary>
    public string? ProviderName { get; init; }

    /// <summary>Assistant: the model label.</summary>
    public string? ModelLabel { get; init; }

    /// <summary>Assistant: nothing left this PC for this answer.</summary>
    public bool? StayedOnPc { get; init; }

    /// <summary>Assistant: the cited lines that were in what was sent, in order of first mention.</summary>
    public IReadOnlyList<AskCitation>? Citations { get; init; }

    /// <summary>Assistant: what the answer was given.</summary>
    public AskContextInfo? Context { get; init; }

    /// <summary>Assistant: the answer was stopped before it finished (cancelled); the text is what had arrived.</summary>
    public bool? Stopped { get; init; }
}