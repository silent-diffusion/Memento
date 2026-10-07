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

    public const string TranscriptGet = "transcript.get";
    public const string TranscriptEditSegment = "transcript.editSegment";
    public const string TranscriptSetSegmentSpeaker = "transcript.setSegmentSpeaker";
    public const string TranscriptRenameSpeaker = "transcript.renameSpeaker";
    public const string TranscriptMergeSpeakers = "transcript.mergeSpeakers";
    public const string TranscriptMarkReviewed = "transcript.markReviewed";
    public const string TranscriptSearch = "transcript.search";
    public const string TranscriptRetranscribe = "transcript.retranscribe";
    public const string TranscriptVersions = "transcript.versions";
    public const string TranscriptRestoreVersion = "transcript.restoreVersion";
    public const string ProcessingRetry = "processing.retry";
    public const string ProcessingCancel = "processing.cancel";
    public const string ProcessingPause = "processing.pause";
    public const string ProcessingResume = "processing.resume";
    public const string ModelsList = "models.list";
    public const string ModelsInstall = "models.install";
    public const string ModelsCancelInstall = "models.cancelInstall";
    public const string ModelsRemove = "models.remove";
    public const string EngineStatus = "engine.status";

    public const string AgendaImportFile = "agenda.importFile";
    public const string AgendaImportDropped = "agenda.importDropped";
    public const string AgendaParseText = "agenda.parseText";
    public const string AgendaApply = "agenda.apply";
    public const string AgendaDiscard = "agenda.discard";
    public const string AgendaSetCovered = "agenda.setCovered";
    public const string AttachmentsList = "attachments.list";
    public const string AttachmentsAdd = "attachments.add";
    public const string AttachmentsRemove = "attachments.remove";
    public const string AttachmentsOpen = "attachments.open";
    public const string LibraryImportMedia = "library.importMedia";
    public const string ProjectChangeType = "project.changeType";
    public const string ExportEstimate = "export.estimate";
    public const string ExportRun = "export.run";
    public const string ExportCancel = "export.cancel";
    public const string ExportOpenFolder = "export.openFolder";
    public const string LibraryUsage = "library.usage";
    public const string LibraryRebuildIndex = "library.rebuildIndex";
    public const string LibraryMove = "library.move";
    public const string StorageReclaim = "storage.reclaim";
    public const string AiSetKey = "ai.setKey";
    public const string AiClearKey = "ai.clearKey";
    public const string AppSetStartup = "app.setStartup";
    public const string UpdatesStatus = "updates.status";
    public const string UpdatesCheck = "updates.check";
    public const string UpdatesApply = "updates.apply";
}
