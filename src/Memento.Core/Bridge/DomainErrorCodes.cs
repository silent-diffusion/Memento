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

    /// <summary>The agenda file is over the 25 MB limit (M3).</summary>
    public const string AgendaFileTooLarge = "agenda.fileTooLarge";

    /// <summary>The agenda image is wider or taller than 10,000 pixels.</summary>
    public const string AgendaImageTooLarge = "agenda.imageTooLarge";

    public const string AgendaUnsupportedFormat = "agenda.unsupportedFormat";
    public const string AgendaUnreadable = "agenda.unreadable";
    public const string AgendaProtected = "agenda.protected";
    public const string AgendaNoText = "agenda.noText";
    public const string AgendaNoItems = "agenda.noItems";

    /// <summary>No text recognition for an image; <c>detail</c> says how to add the OCR language in Windows Settings.</summary>
    public const string AgendaOcrUnavailable = "agenda.ocrUnavailable";

    /// <summary><c>agenda.apply</c> with an item over 200 characters; <c>detail</c> is its 1-based position.</summary>
    public const string AgendaItemTooLong = "agenda.itemTooLong";

    /// <summary><c>agenda.apply</c> with more than 200 items.</summary>
    public const string AgendaTooManyItems = "agenda.tooManyItems";

    /// <summary>The dropped file's path did not reach the host; the UI falls back to the picker.</summary>
    public const string AgendaDropUnavailable = "agenda.dropUnavailable";

    /// <summary>An attachment over the 100 MB limit.</summary>
    public const string AttachmentsTooLarge = "attachments.tooLarge";

    /// <summary>The attachment id names no attachment of the recording; <c>detail</c> is the id.</summary>
    public const string AttachmentsNotFound = "attachments.notFound";

    /// <summary>Media Foundation cannot decode the file chosen for <c>library.importMedia</c>.</summary>
    public const string LibraryImportUnsupported = "library.importUnsupported";

    /// <summary><c>library.move</c> (or another library-wide job) while recording, processing or exporting.</summary>
    public const string LibraryBusy = "library.busy";

    /// <summary>The export folder cannot be written; <c>detail</c> says why. Nothing was written.</summary>
    public const string ExportDestinationUnwritable = "export.destinationUnwritable";

    /// <summary><c>export.run</c> with nothing ticked that can be written.</summary>
    public const string ExportNothingSelected = "export.nothingSelected";

    /// <summary>The job id names no export of this session.</summary>
    public const string ExportNotFound = "export.notFound";
}
