namespace Memento.AI.Http;

/// <summary>
/// A streamed answer, or one line or event of its stream, went past the size Memento reads
/// (<see cref="CloudStreamContext.MaxAnswerChars"/>); the request runner reports it as an unreadable answer.
/// </summary>
internal sealed class CloudAnswerTooLongException : Exception
{
    public CloudAnswerTooLongException()
        : base("The answer is too long.")
    {
    }

    public CloudAnswerTooLongException(string message)
        : base(message)
    {
    }

    public CloudAnswerTooLongException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
