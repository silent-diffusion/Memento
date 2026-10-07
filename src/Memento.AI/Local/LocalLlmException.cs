namespace Memento.AI.Local;

/// <summary>A local job failure with its <see cref="AiError"/> (code and user-facing copy), raised in the worker or by a job client.</summary>
public sealed class LocalLlmException : Exception
{
    public LocalLlmException()
        : this(AiErrors.LocalFailed(LocalAiProvider.ProviderName, "the model", "unknown"))
    {
    }

    public LocalLlmException(string message)
        : this(AiErrors.LocalFailed(LocalAiProvider.ProviderName, "the model", message))
    {
    }

    public LocalLlmException(string message, Exception innerException)
        : this(AiErrors.LocalFailed(LocalAiProvider.ProviderName, "the model", message), innerException)
    {
    }

    public LocalLlmException(AiError error, Exception? innerException = null)
        : base(error?.Message, innerException)
    {
        ArgumentNullException.ThrowIfNull(error);
        Error = error;
    }

    public AiError Error { get; }

    public string Code => Error.Code;
}
