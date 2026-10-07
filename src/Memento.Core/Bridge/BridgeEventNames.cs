namespace Memento.Core.Bridge;

/// <summary>Event names pushed from host to UI. Mirrored in <c>ui/src/bridge/types.ts</c>.</summary>
public static class BridgeEventNames
{
    public const string ThemeChanged = "theme.changed";
    public const string FooterStatus = "status.footer";
    public const string RecordingState = "recording.state";
    public const string RecordingLevels = "recording.levels";
    public const string RecordingSourceLost = "recording.sourceLost";
    public const string RecordingStoppedByHost = "recording.stoppedByHost";
    public const string LibraryChanged = "library.changed";
    public const string ProcessingProgress = "processing.progress";
    public const string StorageLowSpace = "storage.lowSpace";
    public const string TranscriptChanged = "transcript.changed";
    public const string ModelsProgress = "models.progress";

    /// <summary>Optional live draft; this build never sends it (BRIDGE.md M2).</summary>
    public const string RecordingLiveTranscript = "recording.liveTranscript";
}
