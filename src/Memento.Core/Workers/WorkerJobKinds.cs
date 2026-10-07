namespace Memento.Core.Workers;

/// <summary>What a worker process is asked to do.</summary>
public static class WorkerJobKinds
{
    public const string Transcribe = "transcribe";
    public const string Diarize = "diarize";

    /// <summary>A local language-model job for document generation (<see cref="WorkerJob.Llm"/>).</summary>
    public const string Llm = "llm";
}
