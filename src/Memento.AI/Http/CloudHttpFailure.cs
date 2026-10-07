namespace Memento.AI.Http;

/// <summary>
/// A non-success HTTP answer, reduced to what error mapping needs. <see cref="ErrorMessage"/> is the provider's text,
/// used only to read numbers such as a token limit; it is never logged or shown.
/// </summary>
internal sealed record CloudHttpFailure(int Status, string? ErrorType, string? ErrorCode, string? ErrorMessage, TimeSpan? RetryAfter);
