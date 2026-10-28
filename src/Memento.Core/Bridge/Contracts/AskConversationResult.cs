namespace Memento.Core.Bridge.Contracts;

/// <summary>Result of <c>ask.get</c> and <c>ask.clear</c>: the recording's conversation and the question being answered, if any.</summary>
/// <param name="JobId">The running (or waiting to be confirmed) question's job, or <c>null</c>.</param>
public sealed record AskConversationResult(string RecordingId, IReadOnlyList<AskTurn> Turns, string? JobId);