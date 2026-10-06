namespace Memento.Core.Bridge;

/// <summary>The M1 <c>area.reason</c> error codes (docs/BRIDGE.md › Error codes).</summary>
public static class DomainErrorCodes
{
    public const string ProjectNotFound = "project.notFound";
    public const string ProjectRecording = "project.recording";
    public const string RecordingNoSources = "recording.noSources";
    public const string RecordingSourceUnavailable = "recording.sourceUnavailable";
    public const string RecordingNoSession = "recording.noSession";
    public const string RecordingDiskFull = "recording.diskFull";

    /// <summary>Proposed addition to BRIDGE.md: a second <c>recording.start</c> while one session is active.</summary>
    public const string RecordingAlreadyActive = "recording.alreadyActive";

    public const string SettingsLibraryMoveUnavailable = "settings.libraryMoveUnavailable";
    public const string SettingsInvalidValue = "settings.invalidValue";
}
