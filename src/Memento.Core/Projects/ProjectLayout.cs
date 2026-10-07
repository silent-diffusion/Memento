namespace Memento.Core.Projects;

/// <summary>File and folder names inside a project folder (ARCHITECTURE.md §4).</summary>
public static class ProjectLayout
{
    public const string ProjectsFolder = "projects";
    public const string ManifestFile = "project.json";
    public const string TracksFolder = "tracks";
    public const string MixBaseName = "mix";
    public const string PeaksFile = "peaks.json";
    public const string AnnotationsFile = "annotations.json";
    public const string HistoryFile = "history.jsonl";
    public const string RecordingStateFile = "recording.state.json";
    public const string AttachmentsFolder = "attachments";
    public const string VersionsFolder = "versions";
    public const string TranscriptFile = "transcript.json";

    /// <summary>Segments of a pass that has not finished yet, so an interrupted pass resumes where it stopped.</summary>
    public const string TranscriptPartialFile = "transcript.partial.json";

    /// <summary>The tracks an unfinished speaker pass has done, so it continues with the others.</summary>
    public const string SpeakersPartialFile = "speakers.partial.json";
    public const string DocumentsFolder = "documents";
}
