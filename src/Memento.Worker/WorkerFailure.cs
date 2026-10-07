using Memento.Core.Workers;

namespace Memento.Worker;

/// <summary>A failure the worker reports as an <c>error</c> line with one of <see cref="WorkerErrorCodes"/>.</summary>
internal sealed class WorkerFailure : Exception
{
    public WorkerFailure()
    {
    }

    public WorkerFailure(string message)
        : base(message)
    {
    }

    public WorkerFailure(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public WorkerFailure(string code, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
    }

    public string Code { get; } = WorkerErrorCodes.Engine;
}
