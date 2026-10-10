namespace Memento.Core.Workers;

/// <summary>The <c>type</c> of each JSON line of the worker protocol (see <see cref="WorkerCommand"/> and <see cref="WorkerReply"/>).</summary>
public static class WorkerMessageTypes
{
    // Host → worker.
    public const string Start = "start";
    public const string Cancel = "cancel";

    /// <summary>More prompts for a local model job that stays loaded (<see cref="WorkerCommand.Llm"/>: the prompts).</summary>
    public const string Prompts = "prompts";

    /// <summary>No more prompts: the local model job that stays loaded unloads and ends with its <c>result</c>.</summary>
    public const string End = "end";

    /// <summary>2.0: one window for the live transcript (<see cref="WorkerCommand.Audio"/>).</summary>
    public const string Audio = "audio";

    // Worker → host.
    public const string Ready = "ready";
    public const string Device = "device";
    public const string Track = "track";
    public const string Progress = "progress";

    /// <summary>A speaker job finished one track (<see cref="WorkerReply.Diarized"/>); the host keeps it so a stopped job resumes after it.</summary>
    public const string Diarized = "diarized";

    /// <summary>A local model job that stays loaded answered one <c>prompts</c> line (<see cref="WorkerReply.Llm"/>: the outputs).</summary>
    public const string Batch = "batch";

    /// <summary>2.0: the live transcript heard one <c>audio</c> window (<see cref="WorkerReply.Window"/>, <see cref="WorkerReply.Segments"/>).</summary>
    public const string Heard = "heard";
    public const string Result = "result";
    public const string Error = "error";
    public const string Cancelled = "cancelled";
    public const string Log = "log";
}
