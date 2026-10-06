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
}
