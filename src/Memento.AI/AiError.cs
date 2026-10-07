namespace Memento.AI;

/// <summary>
/// A specific AI failure (DESIGN.md section 17): <see cref="Message"/> names the provider and the cause, says what is
/// safe, then the fix. It never contains a key, prompt text or answer text, so it can be logged and shown as is.
/// </summary>
/// <param name="Code">One of <see cref="AiErrorCodes"/>.</param>
/// <param name="Provider">The provider's display name ("Claude", "ChatGPT", "Local model").</param>
/// <param name="Message">The user-facing sentence(s).</param>
/// <param name="RetryAfter">With <see cref="AiErrorCodes.RateLimited"/>: when the provider said to try again.</param>
/// <param name="HttpStatus">The HTTP status for cloud failures, when there was a response.</param>
/// <param name="Diagnostic">A short technical note for logs and "details" (error type, exception type); never content or keys.</param>
public sealed record AiError(
    string Code,
    string Provider,
    string Message,
    TimeSpan? RetryAfter = null,
    int? HttpStatus = null,
    string? Diagnostic = null);
