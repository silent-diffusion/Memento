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

    /// <summary>After 2.0.0-beta.1: Review › More › Reprocess › Rebuild the mix (with evened levels, or as recorded).</summary>
    public const string ProjectRebuildMix = "project.rebuildMix";
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

    /// <summary>2.1: "Resume recording" after Stop: a new segment in the same recording.</summary>
    public const string RecordingResumeStopped = "recording.resumeStopped";
    public const string RecoveryList = "recovery.list";
    public const string RecoveryAcknowledge = "recovery.acknowledge";
    public const string DialogPickFolder = "dialog.pickFolder";
    public const string StatusGet = "status.get";

    public const string TranscriptGet = "transcript.get";
    public const string TranscriptEditSegment = "transcript.editSegment";
    public const string TranscriptSetSegmentSpeaker = "transcript.setSegmentSpeaker";
    public const string TranscriptRenameSpeaker = "transcript.renameSpeaker";
    public const string TranscriptMergeSpeakers = "transcript.mergeSpeakers";
    public const string TranscriptRestoreSpeaker = "transcript.restoreSpeaker";
    public const string TranscriptRestoreSpeakers = "transcript.restoreSpeakers";
    public const string TranscriptReduceSpeakers = "transcript.reduceSpeakers";
    public const string TranscriptRemoveSpeaker = "transcript.removeSpeaker";
    public const string TranscriptMarkReviewed = "transcript.markReviewed";
    public const string TranscriptSearch = "transcript.search";
    public const string TranscriptRetranscribe = "transcript.retranscribe";
    public const string TranscriptVersions = "transcript.versions";
    public const string TranscriptRestoreVersion = "transcript.restoreVersion";
    public const string TranscriptCopy = "transcript.copy";
    public const string TranscriptGetVersion = "transcript.getVersion";
    public const string HistoryLinks = "history.links";
    public const string ProcessingRetry = "processing.retry";
    public const string ProcessingCancel = "processing.cancel";
    public const string ProcessingPause = "processing.pause";
    public const string ProcessingResume = "processing.resume";
    public const string ModelsList = "models.list";
    public const string ModelsInstall = "models.install";
    public const string ModelsCancelInstall = "models.cancelInstall";
    public const string ModelsRemove = "models.remove";
    public const string EngineStatus = "engine.status";
    public const string EngineRefresh = "engine.refresh";

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

    /// <summary>2.0: Settings › Storage and history › Keep only the mix for existing recordings.</summary>
    public const string StorageKeepOnlyMix = "storage.keepOnlyMix";
    public const string AiSetKey = "ai.setKey";
    public const string AiClearKey = "ai.clearKey";
    public const string AppSetStartup = "app.setStartup";

    // M4: documents, templates, styles, providers, generation (BRIDGE.md "Methods (M4)").
    public const string ModulesList = "modules.list";
    public const string TemplatesList = "templates.list";
    public const string TemplatesGet = "templates.get";
    public const string TemplatesSave = "templates.save";
    public const string TemplatesDuplicate = "templates.duplicate";
    public const string TemplatesDelete = "templates.delete";
    public const string TemplatesResetBuiltIn = "templates.resetBuiltIn";
    public const string StylesList = "styles.list";
    public const string StylesGet = "styles.get";
    public const string StylesSave = "styles.save";
    public const string StylesDuplicate = "styles.duplicate";
    public const string StylesDelete = "styles.delete";
    public const string StylesResetBuiltIn = "styles.resetBuiltIn";
    public const string StylesSampleHtml = "styles.sampleHtml";
    public const string ProvidersList = "providers.list";
    public const string GenerationPreview = "generation.preview";
    public const string GenerationPreviewHtml = "generation.previewHtml";
    public const string GenerationStart = "generation.start";
    public const string GenerationConfirm = "generation.confirm";
    public const string GenerationCancel = "generation.cancel";

    /// <summary>2.0: "Preview what will be sent", or one of its passes, on the clipboard as text.</summary>
    public const string GenerationCopyPreview = "generation.copyPreview";
    public const string DocumentsList = "documents.list";
    public const string DocumentsGet = "documents.get";
    public const string DocumentsRenderHtml = "documents.renderHtml";
    public const string DocumentsCreate = "documents.create";
    public const string DocumentsSaveEdit = "documents.saveEdit";
    public const string DocumentsRename = "documents.rename";
    public const string DocumentsDuplicate = "documents.duplicate";
    public const string DocumentsDelete = "documents.delete";
    public const string DocumentsMakeTemplate = "documents.makeTemplate";
    public const string DocumentsVersions = "documents.versions";
    public const string DocumentsRestoreVersion = "documents.restoreVersion";
    public const string DocumentsGetVersion = "documents.getVersion";
    public const string DocumentsExport = "documents.export";
    public const string DocumentsCopy = "documents.copy";

    /// <summary>2.0: one module of a document (the Follow-up email) on the clipboard as plain text and HTML.</summary>
    public const string DocumentsCopyModule = "documents.copyModule";

    // H1: updates.
    public const string UpdatesStatus = "updates.status";
    public const string UpdatesCheck = "updates.check";
    public const string UpdatesApply = "updates.apply";

    // 2.0 Review: known voices, suggested chapters, selection mode.
    public const string VoicesList = "voices.list";
    public const string VoicesSetSuggest = "voices.setSuggest";
    public const string VoicesForget = "voices.forget";
    public const string VoicesForgetAll = "voices.forgetAll";
    public const string VoicesRemember = "voices.remember";
    public const string VoicesRevert = "voices.revert";
    public const string VoicesMatches = "voices.matches";
    public const string VoicesDecline = "voices.decline";
    public const string VoicesAcceptMatch = "voices.acceptMatch";
    public const string AnnotationsSuggestChapters = "annotations.suggestChapters";
    public const string AnnotationsDismissSuggestion = "annotations.dismissSuggestion";
    public const string AnnotationsRestoreSuggestion = "annotations.restoreSuggestion";
    public const string TranscriptSetSegmentsSpeaker = "transcript.setSegmentsSpeaker";

    // 2.0 video.
    public const string VideoListSources = "video.listSources";
    public const string VideoThumbnail = "video.thumbnail";
    public const string RecordingSetVideoSource = "recording.setVideoSource";
    public const string RecordingSetPip = "recording.setPip";
    public const string RecordingSetVideoQuality = "recording.setVideoQuality";
    public const string VideoRemoveEstimate = "video.removeEstimate";
    public const string VideoOpenPrivacySettings = "video.openPrivacySettings";
    public const string VideoRemove = "video.remove";

    // 2.1: Library folders and Delete audio.
    public const string FoldersList = "folders.list";
    public const string FoldersCreate = "folders.create";
    public const string FoldersRename = "folders.rename";
    public const string FoldersMove = "folders.move";
    public const string FoldersDelete = "folders.delete";
    public const string LibraryMoveToFolder = "library.moveToFolder";
    public const string AudioRemove = "audio.remove";
    public const string AudioRemoveEstimate = "audio.removeEstimate";
}
