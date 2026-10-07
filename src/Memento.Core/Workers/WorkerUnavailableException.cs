namespace Memento.Core.Workers;

/// <summary>The worker executable is missing or could not be started.</summary>
public sealed class WorkerUnavailableException : Exception
{
    public WorkerUnavailableException()
    {
    }

    public WorkerUnavailableException(string message)
        : base(message)
    {
    }

    public WorkerUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
