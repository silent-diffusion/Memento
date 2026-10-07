namespace Memento.Core.Workers;

/// <summary>The <c>type</c> of each JSON line of the worker protocol (see <see cref="WorkerCommand"/> and <see cref="WorkerReply"/>).</summary>
public static class WorkerMessageTypes
{
    // Host → worker.
    public const string Start = "start";
    public const string Cancel = "cancel";

    // Worker → host.
    public const string Ready = "ready";
    public const string Device = "device";
    public const string Track = "track";
    public const string Progress = "progress";

    /// <summary>A speaker job finished one track (<see cref="WorkerReply.Diarized"/>); the host keeps it so a stopped job resumes after it.</summary>
    public const string Diarized = "diarized";
    public const string Result = "result";
    public const string Error = "error";
    public const string Cancelled = "cancelled";
    public const string Log = "log";
}
