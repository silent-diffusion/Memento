// Host <-> UI bridge contracts (docs/BRIDGE.md). Mirrors src/Memento.Core/Bridge/Contracts/*.cs exactly;
// ContractSerializationTests on the C# side pins the JSON these types describe.

/** UI -> host request. */
export interface BridgeRequest<M extends MethodName = MethodName> {
  id: number;
  method: M;
  params: MethodParams<M>;
}

/** A structured failure; `message` is written for people (DESIGN.md §17). */
export interface BridgeError {
  code: string;
  message: string;
  detail: string | null;
}

/** Host -> UI answer. `id` is null only when the request was too malformed to carry one. */
export type BridgeResponse =
  | { id: number | null; result: unknown; error?: never }
  | { id: number | null; error: BridgeError; result?: never };

/** Host -> UI notification. */
export interface BridgeEventEnvelope<E extends EventName = EventName> {
  event: E;
  payload: EventPayload<E>;
}

/** Parameters of a method that takes none. */
export type EmptyParams = Record<string, never>;

/** Result of a method that only acknowledges. */
export type EmptyResult = Record<string, never>;

/** Error codes the host may answer with (BRIDGE.md, Error codes). */
export const ERROR_CODES = [
  'invalidRequest',
  'invalidJson',
  'invalidParams',
  'unknownMethod',
  'cancelled',
  'internal',
  'project.notFound',
  'project.recording',
  'recording.noSources',
  'recording.sourceUnavailable',
  'recording.noSession',
  'recording.diskFull',
  'settings.libraryMoveUnavailable',
] as const;

export type ErrorCode = (typeof ERROR_CODES)[number];

// ---------------------------------------------------------------------------------------------
// Shared types
// ---------------------------------------------------------------------------------------------

export type BuiltInRecordingType =
  | 'meeting'
  | 'interview'
  | 'presentation'
  | 'lecture'
  | 'dictation'
  | 'research'
  | 'general';

/** A built-in type, or any other string as the name of a custom type. */
export type RecordingType = BuiltInRecordingType | (string & Record<never, never>);

export type StageName = 'stored' | 'transcript' | 'speakers' | 'minutes';

export type StageState = 'done' | 'active' | 'queued' | 'failed';

/** One pipeline stage of a recording; a status pill in the Library (DESIGN.md §5.4). */
export interface StageStatus {
  stage: StageName;
  state: StageState;
  /** 0-100 while active, otherwise null. */
  percent: number | null;
  /** Host wording for the processing card: "64% · local GPU", "Done", "Queued", "Transcript failed". */
  label: string | null;
}

export type RecordingLifecycle = 'recording' | 'finalizing' | 'ready' | 'recovered' | 'failed';

/** One Library row or card (DESIGN.md §4, §16). */
export interface RecordingSummary {
  id: string;
  title: string;
  type: RecordingType;
  /** ISO 8601 with offset. */
  createdAt: string;
  durationMs: number;
  /** People listed for the recording: 0 reads "Just me" (a solo recording), 1 "1 speaker", n "n people". */
  participantCount: number;
  hasVideo: boolean;
  /** [] means no processing has run ("Audio only"). */
  stages: StageStatus[];
  /** Participant names and renamed speakers, for search and the meta line. */
  people: string[];
  /** Any stage active or queued. */
  isProcessing: boolean;
  state: RecordingLifecycle;
  /** Size of the project folder on disk, kept in the index. */
  sizeBytes: number;
}

export type AudioSourceKind = 'microphone' | 'system' | 'application';

export interface AudioSource {
  /** Stable within a session: "mic:<endpointId>", "system:<endpointId>", "app:<pid>". */
  id: string;
  kind: AudioSourceKind;
  /** "Shure MV7", "Everything this PC plays", "Zoom". */
  name: string;
  /** "USB", "Default output", "Only this app". */
  detail: string;
  isDefault: boolean;
  /** Application sources only. */
  processId: number | null;
}

export interface Track {
  id: string;
  sourceId: string;
  sourceKind: AudioSourceKind;
  name: string;
  /** Relative to the project folder. */
  file: string;
  sampleRate: number;
  channels: number;
  durationMs: number;
  sha256: string | null;
  endedEarlyAtMs: number | null;
}

export interface AgendaItem {
  id: string;
  text: string;
  covered: boolean;
  uncertain: boolean;
  uncertainReason: string | null;
}

export interface Agenda {
  /** "agenda.docx". */
  source: string | null;
  parsedLocally: boolean;
  items: AgendaItem[];
}

export interface RecordingDetails {
  title: string;
  type: RecordingType;
  participants: string[];
  purpose: string;
  platform: string;
  organization: string;
  location: string;
  notes: string;
  tags: string[];
  agenda: Agenda;
}

export type AnnotationOrigin = 'user' | 'local' | 'ai';

export interface Chapter {
  id: string;
  atMs: number;
  title: string;
  origin: AnnotationOrigin;
}

export interface Highlight {
  id: string;
  atMs: number;
  note: string;
  origin: AnnotationOrigin;
  segmentId: string | null;
}

export interface Topic {
  id: string;
  label: string;
  origin: AnnotationOrigin;
}

export interface HistoryEntry {
  at: string;
  stage: StageName | 'recorded' | 'edited' | 'exported' | 'recovered';
  event: 'started' | 'progress' | 'completed' | 'failed' | 'info';
  summary: string;
  /** Engine, model, device, duration, what was sent. */
  detail: string | null;
}

export interface ProjectIntegrity {
  algorithm: 'sha256';
  computedAt: string | null;
}

/** Everything Review needs for one recording. */
export interface Project {
  summary: RecordingSummary;
  details: RecordingDetails;
  tracks: Track[];
  /** https://library.memento/projects/<id>/mix.flac once finalized. The UI never builds these URLs. */
  mixUrl: string | null;
  /** https://library.memento/projects/<id>/peaks.json. */
  peaksUrl: string | null;
  chapters: Chapter[];
  highlights: Highlight[];
  topics: Topic[];
  history: HistoryEntry[];
  integrity: ProjectIntegrity;
  sizeBytes: number;
}

// ---------------------------------------------------------------------------------------------
// App and settings
// ---------------------------------------------------------------------------------------------

export interface AppVersionResult {
  version: string;
  osVersion: string;
  isDarkTheme: boolean;
}

/** An https: link or an ms-settings: page. */
export interface OpenExternalParams {
  url: string;
}

export interface OpenExternalResult {
  opened: boolean;
}

export type ThemePreference = 'system' | 'light' | 'dark';

export type ListDensity = 'comfortable' | 'compact';

export type StorageCodec = 'flac' | 'aac' | 'mp3';

export interface RecordingStorageSettings {
  codec: StorageCodec;
  /** Lossy codecs only; null for FLAC. */
  bitrateKbps: number | null;
  downmixMono: boolean;
  keepOnlyMix: boolean;
}

export interface RecordingSettings {
  defaultType: RecordingType;
  /** The remembered source selection. */
  defaultSourceIds: string[];
  /** Fixed in M1. */
  keepSeparateTracks: true;
  storage: RecordingStorageSettings;
  /** 30 by default. */
  checkpointSeconds: number;
  /** 10 by default. */
  lowSpaceGb: number;
}

export interface SettingsSnapshot {
  theme: ThemePreference;
  /** The library folder in effect. */
  libraryPath: string;
  listDensity: ListDensity;
  recording: RecordingSettings;
}

/**
 * Partial update; omitted or null fields keep their value. The UI always sends the whole
 * `recording` block when it changes any part of it, so a shallow or a deep merge on the host
 * gives the same result.
 */
export interface SettingsSetParams {
  theme?: ThemePreference | null;
  /** Accepted only when equal to the current location; moving fails with settings.libraryMoveUnavailable. */
  libraryPath?: string | null;
  listDensity?: ListDensity | null;
  recording?: RecordingSettings | null;
}

// ---------------------------------------------------------------------------------------------
// Library and projects
// ---------------------------------------------------------------------------------------------

export type LibrarySort = 'newest' | 'oldest' | 'longest' | 'title';

export interface LibraryListParams {
  /** Searches titles and people (M2: transcripts). */
  query?: string;
  type?: RecordingType | 'all';
  sort?: LibrarySort;
}

/** The result reflects the filter: count and total are for the matching recordings only. */
export interface LibraryListResult {
  recordings: RecordingSummary[];
  totalDurationMs: number;
  totalCount: number;
}

export interface ProcessingCurrent {
  recordingId: string;
  title: string;
  meta: RecordingSummary;
  stages: StageStatus[];
}

export interface LibraryProcessingResult {
  current: ProcessingCurrent | null;
  othersCount: number;
}

export interface RecordingIdParams {
  recordingId: string;
}

export interface ProjectUpdateDetailsParams {
  recordingId: string;
  details: Partial<RecordingDetails>;
}

export interface ProjectRenameParams {
  recordingId: string;
  title: string;
}

/** Feeds the delete confirmation. `items` are lower-case noun phrases ("the recording", "its 3 tracks"). */
export interface ProjectDeleteEstimate {
  title: string;
  sizeBytes: number;
  items: string[];
}

export interface ChapterParams {
  recordingId: string;
  chapter: Partial<Chapter>;
}

export interface ChapterIdParams {
  recordingId: string;
  chapterId: string;
}

export interface ChaptersResult {
  chapters: Chapter[];
}

export interface HighlightParams {
  recordingId: string;
  highlight: Partial<Highlight>;
}

export interface HighlightIdParams {
  recordingId: string;
  highlightId: string;
}

export interface HighlightsResult {
  highlights: Highlight[];
}

export interface TopicParams {
  recordingId: string;
  topic: Partial<Topic>;
}

export interface TopicIdParams {
  recordingId: string;
  topicId: string;
}

export interface TopicsResult {
  topics: Topic[];
}

// ---------------------------------------------------------------------------------------------
// Sources and recording
// ---------------------------------------------------------------------------------------------

export interface SourcesListResult {
  audio: AudioSource[];
  videoAvailable: false;
}

export interface RecordingStartParams {
  title: string;
  type: RecordingType;
  sourceIds: string[];
}

export interface RecordingStartResult {
  sessionId: string;
  recordingId: string;
  startedAt: string;
}

export interface SessionParams {
  sessionId: string;
}

export interface RecordingSetSourceParams {
  sessionId: string;
  sourceId: string;
  enabled: boolean;
}

export interface RecordingSetSourceResult {
  tracks: Track[];
}

export interface RecordingMarkHighlightParams {
  sessionId: string;
  note?: string;
}

export interface RecordingMarkHighlightResult {
  highlight: Highlight;
}

export interface RecordingStopResult {
  recordingId: string;
}

export type RecordingSessionState = 'recording' | 'paused' | 'finalizing' | 'ready' | 'stopped';

export interface RecordingStatePayload {
  sessionId: string;
  recordingId: string;
  state: RecordingSessionState;
  startedAt: string;
  elapsedMs: number;
  tracks: Track[];
  lastCheckpointAt: string | null;
  highlightsCount: number;
}

export interface RecordingCurrentResult {
  session: RecordingStatePayload | null;
}

// ---------------------------------------------------------------------------------------------
// Recovery, dialogs, status
// ---------------------------------------------------------------------------------------------

export interface RecoveredRecording {
  recordingId: string;
  title: string;
  startedAt: string;
  tracksIntact: number;
  tracksTotal: number;
  lastCheckpointAt: string | null;
  recoveredDurationMs: number;
  mayBeMissingMs: number;
}

export interface RecoveryListResult {
  items: RecoveredRecording[];
}

export interface PickFolderParams {
  title: string;
  initialPath?: string;
}

export interface PickFolderResult {
  path: string | null;
}

export interface EngineStatus {
  ready: boolean;
  /** GPU or CPU; null when no engine is ready. */
  device: string | null;
}

export interface StorageStatus {
  /** Free bytes on the library drive, or null when it cannot be read. */
  freeBytes: number | null;
  /** Below the low-space threshold (10 GB by default). */
  lowSpace: boolean;
}

export interface FooterRecordingStatus {
  active: boolean;
  lastCheckpointAt: string | null;
  /** Name of a source that stopped during the active recording. */
  lostSource: string | null;
}

export interface FooterStatusPayload {
  engine: EngineStatus;
  storage: StorageStatus;
  recording: FooterRecordingStatus;
  /** Why processing is paused ("PC is busy"), or null. */
  processingPaused: string | null;
}

// ---------------------------------------------------------------------------------------------
// Events
// ---------------------------------------------------------------------------------------------

export interface ThemeChangedPayload {
  isDark: boolean;
}

export interface SourceLevel {
  sourceId: string;
  /** 0..1 */
  rms: number;
  peak: number;
}

export interface RecordingLevelsPayload {
  sessionId: string;
  levels: SourceLevel[];
}

export interface RecordingSourceLostPayload {
  sessionId: string;
  sourceId: string;
  name: string;
  atMs: number;
  /** Names of the sources still recording. */
  remaining: string[];
}

export interface RecordingStoppedByHostPayload {
  sessionId: string;
  recordingId: string;
  reason: 'diskFull' | 'deviceLost' | 'error';
  atMs: number;
  message: string;
}

export interface LibraryChangedPayload {
  recordingIds: string[];
}

export interface ProcessingProgressPayload {
  recordingId: string;
  stages: StageStatus[];
}

export interface StorageLowSpacePayload {
  freeBytes: number;
  thresholdBytes: number;
  recordingContinues: boolean;
  transcriptionPaused: boolean;
}

// ---------------------------------------------------------------------------------------------
// Maps
// ---------------------------------------------------------------------------------------------

/** Every host method: name -> params and result. Mirrors BridgeMethodNames.cs. */
export interface BridgeMethods {
  'app.version': { params: EmptyParams; result: AppVersionResult };
  'app.openExternal': { params: OpenExternalParams; result: OpenExternalResult };
  'ui.ready': { params: EmptyParams; result: EmptyResult };
  'settings.get': { params: EmptyParams; result: SettingsSnapshot };
  'settings.set': { params: SettingsSetParams; result: SettingsSnapshot };
  'library.list': { params: LibraryListParams; result: LibraryListResult };
  'library.processing': { params: EmptyParams; result: LibraryProcessingResult };
  'project.get': { params: RecordingIdParams; result: Project };
  'project.updateDetails': { params: ProjectUpdateDetailsParams; result: Project };
  'project.deleteEstimate': { params: RecordingIdParams; result: ProjectDeleteEstimate };
  'project.delete': { params: RecordingIdParams; result: EmptyResult };
  'project.rename': { params: ProjectRenameParams; result: Project };
  'annotations.addChapter': { params: ChapterParams; result: ChaptersResult };
  'annotations.updateChapter': { params: ChapterParams; result: ChaptersResult };
  'annotations.removeChapter': { params: ChapterIdParams; result: ChaptersResult };
  'annotations.addHighlight': { params: HighlightParams; result: HighlightsResult };
  'annotations.updateHighlight': { params: HighlightParams; result: HighlightsResult };
  'annotations.removeHighlight': { params: HighlightIdParams; result: HighlightsResult };
  'annotations.addTopic': { params: TopicParams; result: TopicsResult };
  'annotations.removeTopic': { params: TopicIdParams; result: TopicsResult };
  'sources.list': { params: EmptyParams; result: SourcesListResult };
  'recording.start': { params: RecordingStartParams; result: RecordingStartResult };
  'recording.setSource': { params: RecordingSetSourceParams; result: RecordingSetSourceResult };
  'recording.pause': { params: SessionParams; result: EmptyResult };
  'recording.resume': { params: SessionParams; result: EmptyResult };
  'recording.markHighlight': { params: RecordingMarkHighlightParams; result: RecordingMarkHighlightResult };
  'recording.stop': { params: SessionParams; result: RecordingStopResult };
  'recording.current': { params: EmptyParams; result: RecordingCurrentResult };
  'recovery.list': { params: EmptyParams; result: RecoveryListResult };
  'recovery.acknowledge': { params: RecordingIdParams; result: EmptyResult };
  'dialog.pickFolder': { params: PickFolderParams; result: PickFolderResult };
  'status.get': { params: EmptyParams; result: FooterStatusPayload };
}

/** Every host event: name -> payload. Mirrors BridgeEventNames.cs. */
export interface BridgeEvents {
  'theme.changed': ThemeChangedPayload;
  'status.footer': FooterStatusPayload;
  'recording.state': RecordingStatePayload;
  'recording.levels': RecordingLevelsPayload;
  'recording.sourceLost': RecordingSourceLostPayload;
  'recording.stoppedByHost': RecordingStoppedByHostPayload;
  'library.changed': LibraryChangedPayload;
  'processing.progress': ProcessingProgressPayload;
  'storage.lowSpace': StorageLowSpacePayload;
}

export type MethodName = keyof BridgeMethods;
export type MethodParams<M extends MethodName> = BridgeMethods[M]['params'];
export type MethodResult<M extends MethodName> = BridgeMethods[M]['result'];
export type EventName = keyof BridgeEvents;
export type EventPayload<E extends EventName> = BridgeEvents[E];

export const METHOD_NAMES = [
  'app.version',
  'app.openExternal',
  'ui.ready',
  'settings.get',
  'settings.set',
  'library.list',
  'library.processing',
  'project.get',
  'project.updateDetails',
  'project.deleteEstimate',
  'project.delete',
  'project.rename',
  'annotations.addChapter',
  'annotations.updateChapter',
  'annotations.removeChapter',
  'annotations.addHighlight',
  'annotations.updateHighlight',
  'annotations.removeHighlight',
  'annotations.addTopic',
  'annotations.removeTopic',
  'sources.list',
  'recording.start',
  'recording.setSource',
  'recording.pause',
  'recording.resume',
  'recording.markHighlight',
  'recording.stop',
  'recording.current',
  'recovery.list',
  'recovery.acknowledge',
  'dialog.pickFolder',
  'status.get',
] as const satisfies readonly MethodName[];

export const EVENT_NAMES = [
  'theme.changed',
  'status.footer',
  'recording.state',
  'recording.levels',
  'recording.sourceLost',
  'recording.stoppedByHost',
  'library.changed',
  'processing.progress',
  'storage.lowSpace',
] as const satisfies readonly EventName[];
