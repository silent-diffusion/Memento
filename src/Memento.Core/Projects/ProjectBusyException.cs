namespace Memento.Core.Projects;

/// <summary>The project is being recorded (its <c>recording.state.json</c> exists), so it cannot be deleted.</summary>
public sealed class ProjectBusyException : Exception
{
    public ProjectBusyException(string recordingId)
        : base($"Recording {recordingId} is still being recorded or recovered.")
    {
        RecordingId = recordingId;
    }

    public ProjectBusyException()
        : this(string.Empty)
    {
    }

    public ProjectBusyException(string message, Exception innerException)
        : base(message, innerException)
    {
        RecordingId = string.Empty;
    }

    public string RecordingId { get; }
}
