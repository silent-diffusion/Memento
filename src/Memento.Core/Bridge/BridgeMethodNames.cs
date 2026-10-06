namespace Memento.Core.Bridge;

/// <summary>Request method names (<c>area.verb</c>). Mirrored in <c>ui/src/bridge/types.ts</c>.</summary>
public static class BridgeMethodNames
{
    public const string AppVersion = "app.version";
    public const string AppOpenExternal = "app.openExternal";
    public const string SettingsGet = "settings.get";
    public const string SettingsSet = "settings.set";
    public const string LibraryList = "library.list";
    public const string UiReady = "ui.ready";

    public const string LibraryProcessing = "library.processing";
    public const string ProjectGet = "project.get";
    public const string ProjectUpdateDetails = "project.updateDetails";
    public const string ProjectDeleteEstimate = "project.deleteEstimate";
    public const string ProjectDelete = "project.delete";
    public const string ProjectRename = "project.rename";
    public const string AnnotationsAddChapter = "annotations.addChapter";
    public const string AnnotationsUpdateChapter = "annotations.updateChapter";
    public const string AnnotationsRemoveChapter = "annotations.removeChapter";
    public const string AnnotationsAddHighlight = "annotations.addHighlight";
    public const string AnnotationsUpdateHighlight = "annotations.updateHighlight";
    public const string AnnotationsRemoveHighlight = "annotations.removeHighlight";
    public const string AnnotationsAddTopic = "annotations.addTopic";
    public const string AnnotationsRemoveTopic = "annotations.removeTopic";
    public const string SourcesList = "sources.list";
    public const string RecordingStart = "recording.start";
    public const string RecordingSetSource = "recording.setSource";
    public const string RecordingPause = "recording.pause";
    public const string RecordingResume = "recording.resume";
    public const string RecordingMarkHighlight = "recording.markHighlight";
    public const string RecordingStop = "recording.stop";
    public const string RecordingCurrent = "recording.current";
    public const string RecoveryList = "recovery.list";
    public const string RecoveryAcknowledge = "recovery.acknowledge";
    public const string DialogPickFolder = "dialog.pickFolder";
    public const string StatusGet = "status.get";
}
