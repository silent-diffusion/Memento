namespace Memento.AI.Http;

/// <summary>An error event inside a successful stream (for example <c>overloaded_error</c>).</summary>
internal sealed class CloudStreamException : Exception
{
    public CloudStreamException()
    {
    }

    public CloudStreamException(string message)
        : base(message)
    {
    }

    public CloudStreamException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public CloudStreamException(string? errorType, bool retryable)
        : base("The provider reported an error in the stream.")
    {
        ErrorType = errorType;
        Retryable = retryable;
    }

    /// <summary>The provider's error type or code; never its message.</summary>
    public string? ErrorType { get; }

    /// <summary>The provider said it did not process the request (overload, rate limit, transient server error).</summary>
    public bool Retryable { get; }
}
