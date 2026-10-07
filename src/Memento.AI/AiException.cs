namespace Memento.AI;

/// <summary>Thrown by providers for every failure except cancellation; <see cref="Exception.Message"/> is <see cref="AiError.Message"/>.</summary>
public sealed class AiException : Exception
{
    public AiException()
        : this(new AiError(AiErrorCodes.ProviderError, "AI", "The AI provider failed."))
    {
    }

    public AiException(string message)
        : this(new AiError(AiErrorCodes.ProviderError, "AI", message))
    {
    }

    public AiException(string message, Exception innerException)
        : this(new AiError(AiErrorCodes.ProviderError, "AI", message), innerException)
    {
    }

    /// <param name="innerException">
    /// Kept for diagnosis. Providers pass only exceptions whose text cannot carry a key or content (socket and
    /// timeout exceptions); HTTP bodies are never attached.
    /// </param>
    public AiException(AiError error, Exception? innerException = null)
        : base(error?.Message, innerException)
    {
        ArgumentNullException.ThrowIfNull(error);
        Error = error;
    }

    public AiError Error { get; }

    public string Code => Error.Code;
}
