namespace Memento.AI.Http;

/// <summary>
/// A non-success HTTP answer, reduced to what error mapping needs. <see cref="ErrorMessage"/> is the provider's text,
/// used only to read numbers such as a token limit and whether it names the model; it is never logged or shown.
/// </summary>
internal sealed record CloudHttpFailure(int Status, string? ErrorType, string? ErrorCode, string? ErrorMessage, TimeSpan? RetryAfter)
{
    private static readonly string[] UnknownModelWords = ["not found", "does not exist", "unknown model", "invalid model", "not a valid model", "no such model"];

    /// <summary>
    /// Whether the service says it does not know <paramref name="model"/>: a model error code (<c>model_not_found</c>,
    /// <c>invalid_model</c>), or a 404 or 400 whose text names the model as missing ("model: claude-x" is Anthropic's 404).
    /// A 404 without a JSON error (a proxy's page, a wrong address) is not taken for one.
    /// </summary>
    public bool IsUnknownModel(string model)
    {
        if (ErrorCode is "model_not_found" or "invalid_model")
        {
            return true;
        }

        if (Status is not (400 or 404) || ErrorMessage is not { Length: > 0 } text || !text.Contains(model, StringComparison.Ordinal))
        {
            return false;
        }

        return (Status == 404 && ErrorType is "not_found_error" or "invalid_request_error")
            || UnknownModelWords.Any(w => text.Contains(w, StringComparison.OrdinalIgnoreCase));
    }
}
