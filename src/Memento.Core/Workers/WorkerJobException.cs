namespace Memento.Core.Workers;

/// <summary>The worker reported a structured <c>error</c> line.</summary>
public sealed class WorkerJobException : Exception
{
    public WorkerJobException()
    {
    }

    public WorkerJobException(string message)
        : base(message)
    {
        Code = WorkerErrorCodes.Engine;
    }

    public WorkerJobException(string message, Exception innerException)
        : base(message, innerException)
    {
        Code = WorkerErrorCodes.Engine;
    }

    public WorkerJobException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    /// <summary>One of <see cref="WorkerErrorCodes"/>.</summary>
    public string Code { get; } = WorkerErrorCodes.Engine;
}
