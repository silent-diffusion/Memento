namespace Memento.AI;

/// <summary>A progress report from <see cref="IAiProvider.GenerateAsync"/>. Never logged: <see cref="Delta"/> is content.</summary>
/// <param name="Delta">Output text that arrived since the previous report.</param>
/// <param name="OutputTokens">Output tokens so far (estimated for cloud providers while streaming).</param>
/// <param name="RetryIn">With <see cref="AiProgressStage.WaitingToRetry"/>: how long until the next attempt.</param>
/// <param name="Attempt">The attempt in progress, from 1.</param>
/// <param name="Index">In a batch (<c>LocalAiProvider.GenerateManyAsync</c>): the request being worked on, from 0.</param>
public sealed record AiProgress(
    AiProgressStage Stage,
    string? Delta = null,
    int OutputTokens = 0,
    TimeSpan? RetryIn = null,
    int Attempt = 1,
    int? Index = null);
