namespace Memento.Core.Workers;

/// <summary>Structured failures a worker reports with an <c>error</c> line before it exits.</summary>
public static class WorkerErrorCodes
{
    /// <summary>The model file could not be loaded (damaged, wrong format, or the engine library failed to start).</summary>
    public const string ModelLoad = "modelLoad";

    /// <summary>A track could not be opened or decoded.</summary>
    public const string Audio = "audio";

    /// <summary>The engine ran out of memory.</summary>
    public const string OutOfMemory = "outOfMemory";

    /// <summary>The engine failed while processing.</summary>
    public const string Engine = "engine";

    /// <summary>The job spec is not valid.</summary>
    public const string InvalidJob = "invalidJob";
}
