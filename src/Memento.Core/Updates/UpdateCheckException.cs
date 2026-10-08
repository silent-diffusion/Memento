namespace Memento.Core.Updates;

/// <summary>The update feed could not be reached, read or downloaded; the message says what happened, in words.</summary>
public sealed class UpdateCheckException : Exception
{
    public UpdateCheckException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }

    public UpdateCheckException()
        : this("The update server could not be reached.")
    {
    }

    public UpdateCheckException(string message)
        : this(message, null)
    {
    }
}
