namespace Memento.Core.Workers;

/// <summary>A running worker: its stdin and stdout, and how to stop it.</summary>
public interface IWorkerProcess : IDisposable
{
    int Id { get; }

    TextWriter Input { get; }

    TextReader Output { get; }

    /// <summary>Completes with the exit code when the process has exited.</summary>
    Task<int> Exited { get; }

    /// <summary>Processor time the worker has used (it is left out of the "PC is busy" measure).</summary>
    TimeSpan CpuTime { get; }

    /// <summary>The last lines the worker wrote to stderr (native diagnostics).</summary>
    IReadOnlyList<string> ErrorTail { get; }

    void Kill();
}
