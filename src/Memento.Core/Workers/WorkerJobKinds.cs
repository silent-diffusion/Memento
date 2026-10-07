namespace Memento.Core.Workers;

/// <summary>What a worker process is asked to do.</summary>
public static class WorkerJobKinds
{
    public const string Transcribe = "transcribe";
    public const string Diarize = "diarize";
}
