namespace Memento.Core.Projects;

/// <summary>No project folder (or no readable <c>project.json</c>) exists for the id.</summary>
public sealed class ProjectNotFoundException : Exception
{
    public ProjectNotFoundException(string recordingId)
        : base($"Recording {recordingId} is not in the library.")
    {
        RecordingId = recordingId;
    }

    public ProjectNotFoundException()
        : this(string.Empty)
    {
    }

    public ProjectNotFoundException(string message, Exception innerException)
        : base(message, innerException)
    {
        RecordingId = string.Empty;
    }

    public string RecordingId { get; }
}
