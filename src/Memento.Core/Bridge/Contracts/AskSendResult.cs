namespace Memento.Core.Bridge.Contracts;

/// <summary>Result of <c>ask.send</c>. With <see cref="ConfirmationRequired"/> nothing is sent until <c>ask.confirm</c>.</summary>
/// <param name="Question">The question as it was saved in the conversation.</param>
public sealed record AskSendResult(string JobId, AskTurn Question)
{
    public bool ConfirmationRequired { get; init; }

    public AskSendSummary? Summary { get; init; }
}