namespace Memento.Core.Bridge;

/// <summary>
/// The <c>area.reason</c> error codes methods answer with (docs/BRIDGE.md › Error codes). The router's own codes
/// are in <see cref="BridgeErrorCodes"/>. Every constant here is listed in BRIDGE.md and in the UI's <c>ERROR_CODES</c>.
/// </summary>
public static class DomainErrorCodes
{
    /// <summary><c>app.openExternal</c> was given something other than an https: link or an ms-settings: page.</summary>
    public const string AppOpenExternalUnsupportedTarget = "app.openExternal.unsupportedTarget";

    /// <summary>Windows could not open an allowed link.</summary>
    public const string AppOpenExternalFailed = "app.openExternal.failed";

    public const string SettingsInvalidValue = "settings.invalidValue";
    public const string SettingsLibraryMoveUnavailable = "settings.libraryMoveUnavailable";

    public const string ProjectNotFound = "project.notFound";
    public const string ProjectRecording = "project.recording";

    /// <summary>An <c>annotations.update*</c> or <c>annotations.remove*</c> named a chapter, highlight or topic the recording does not have.</summary>
    public const string AnnotationsNotFound = "annotations.notFound";

    public const string RecordingNoSources = "recording.noSources";
    public const string RecordingSourceUnavailable = "recording.sourceUnavailable";
    public const string RecordingNoSession = "recording.noSession";
    public const string RecordingDiskFull = "recording.diskFull";

    /// <summary>A second <c>recording.start</c> while one session is active.</summary>
    public const string RecordingAlreadyActive = "recording.alreadyActive";

    /// <summary>The recording has no transcript yet (M2).</summary>
    public const string TranscriptNone = "transcript.none";

    public const string TranscriptSegmentNotFound = "transcript.segmentNotFound";
    public const string TranscriptSpeakerNotFound = "transcript.speakerNotFound";
    public const string TranscriptVersionNotFound = "transcript.versionNotFound";

    /// <summary>No catalog model has that id.</summary>
    public const string ModelsNotFound = "models.notFound";

    /// <summary><c>models.remove</c> while a stage is using the model.</summary>
    public const string ModelsInUse = "models.inUse";

    /// <summary>The download could not start or connect; <c>detail</c> is the cause.</summary>
    public const string ModelsDownloadFailed = "models.downloadFailed";

    /// <summary><c>models.install</c> while another model is downloading; <c>detail</c> is that model's id (M2 clarification 6).</summary>
    public const string ModelsBusy = "models.busy";

    /// <summary>Not enough free space for the model.</summary>
    public const string ModelsNoSpace = "models.noSpace";

    /// <summary>The engine or model needed is not installed or available; <c>detail</c> says what to install or turn on.</summary>
    public const string EngineUnavailable = "engine.unavailable";
}
