namespace Memento.Core.Transcripts;

/// <summary>Values of <c>transcript.get</c>'s <c>status</c>.</summary>
public static class TranscriptStatuses
{
    public const string None = "none";
    public const string Queued = "queued";
    public const string Running = "running";
    public const string Done = "done";
    public const string Failed = "failed";
    public const string Paused = "paused";
}
