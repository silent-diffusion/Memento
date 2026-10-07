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

/**
 * Error codes the host may answer with (BRIDGE.md, Error codes): the router's `bridge.*` codes
 * (BridgeErrorCodes.cs), then the methods' codes (DomainErrorCodes.cs). The C# tests check this list
 * against both files.
 */
export const ERROR_CODES = [
  'bridge.invalidJson',
  'bridge.invalidRequest',
  'bridge.unknownMethod',
  'bridge.invalidParams',
  'bridge.cancelled',
  'bridge.internal',
  'app.openExternal.unsupportedTarget',
  'app.openExternal.failed',
  'settings.invalidValue',
  'settings.libraryMoveUnavailable',
  'project.notFound',
  'project.recording',
  'annotations.notFound',
  'recording.noSources',
  'recording.sourceUnavailable',
  'recording.noSession',
  'recording.diskFull',
  'recording.alreadyActive',
  // M2
  'transcript.none',
  'transcript.segmentNotFound',
  'transcript.speakerNotFound',
  'transcript.versionNotFound',
  'models.notFound',
  'models.inUse',
  'models.downloadFailed',
  'models.busy',
  'models.noSpace',
  'engine.unavailable',
  // M3
  'agenda.fileTooLarge',
  'agenda.imageTooLarge',
  'agenda.unsupportedFormat',
  'agenda.unreadable',
  'agenda.protected',
  'agenda.noText',
  'agenda.noItems',
  'agenda.ocrUnavailable',
  'agenda.itemTooLong',
  'agenda.tooManyItems',
  'agenda.itemNotFound',
  'agenda.dropUnavailable',
  'attachments.tooLarge',
  'attachments.notFound',
  'library.importUnsupported',
  'library.busy',
  'export.destinationUnwritable',
  'export.nothingSelected',
  'export.notFound',
  'library.moveRefused',
  'storage.nothingToReclaim',
  'app.startupRefused',
  'ai.keyWriteFailed',
  // H1
  'updates.unavailable',
  'updates.notReady',
  'updates.busy',
] as const;

export type ErrorCode = (typeof ERROR_CODES)[number];

/** Codes the bridge client produces itself; the host never sends them. */
export const CLIENT_ERROR_CODES = ['bridge.timeout', 'bridge.sendFailed'] as const;

export type ClientErrorCode = (typeof CLIENT_ERROR_CODES)[number];

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

/**
 * Pipeline order. `topics` is the short local keyword stage after `speakers` (a finished one is left
 * out of rows, like `stored`); `optimize` converts the lossless files to the smaller AAC/MP3 choice,
 * after every other stage.
 */
export type StageName = 'stored' | 'transcript' | 'speakers' | 'topics' | 'minutes' | 'optimize';

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
  /**
   * [] means no processing has run ("Audio only"). A finished `stored` or `optimize` stage is left
   * out; library.processing's `stages` keep every stage.
   */
  stages: StageStatus[];
  /** Participant names and renamed speakers, for search and the meta line. */
  people: string[];
  /** Any stage active or queued. */
  isProcessing: boolean;
  state: RecordingLifecycle;
  /** Size of the project folder on disk, kept in the index. */
  sizeBytes: number;
  /**
   * M2: for a library.list `query` that matched transcript text, a short plain-text excerpt around
   * the match; null for title and people matches and when there is no query. The UI bolds the
   * query words itself.
   */
  matchSnippet: string | null;
}

/** `imported` (M3): the one track of a recording made with library.importMedia (`sourceId: 'imported'`). */
export type AudioSourceKind = 'microphone' | 'system' | 'application' | 'imported';

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
  /** 0 for tracks that started with the session; where a track added mid-session begins on the timeline. */
  startOffsetMs: number;
  /** When the track stopped before the session did (turned off or device lost). */
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

/** Values the host writes (BRIDGE.md, History): stored and optimize in M1; transcript, speakers and topics from M2. */
export type HistoryStage = StageName | 'recorded' | 'recovered' | 'edited';

export type HistoryEvent = 'started' | 'completed' | 'failed' | 'info';

export interface HistoryEntry {
  at: string;
  stage: HistoryStage;
  event: HistoryEvent;
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
  /** https://library.memento/<id>/mix.flac once finalized. The UI never builds these URLs. */
  mixUrl: string | null;
  /** https://library.memento/<id>/peaks.json. */
  peaksUrl: string | null;
  chapters: Chapter[];
  highlights: Highlight[];
  topics: Topic[];
  history: HistoryEntry[];
  integrity: ProjectIntegrity;
  sizeBytes: number;
}

// ---------------------------------------------------------------------------------------------
// Transcripts, speakers and models (M2)
// ---------------------------------------------------------------------------------------------

/** One word with its timing in seconds and the engine's confidence 0..1. */
export interface TranscriptWord {
  w: string;
  s: number;
  e: number;
  c: number;
}

export interface SegmentEdit {
  /** ISO 8601 with offset: the first edit. */
  at: string;
  /** The engine's text before the first edit; later edits keep it. */
  original: string;
}

export interface TranscriptSegment {
  id: string;
  /** Seconds from the start of the recording. */
  start: number;
  end: number;
  /** Track id the words came from. */
  track: string | null;
  /** Speaker id; null when speakers were not identified. */
  speaker: string | null;
  speakerConfidence: number | null;
  text: string;
  /** The lowest word confidence. */
  confidence: number;
  /** [] when word timestamps are off. */
  words: TranscriptWord[];
  edited: SegmentEdit | null;
}

/** Speaker colours sp1..sp4, assigned in order of first appearance and cycled (DESIGN.md §2.1). */
export type SpeakerColour = 1 | 2 | 3 | 4;

export interface Speaker {
  id: string;
  name: string;
  /** Named by the user rather than "Speaker 2". */
  renamed: boolean;
  color: SpeakerColour;
  talkTimeMs: number;
}

export interface TranscriptEngine {
  name: string;
  model: string;
  device: string;
  version: string;
  durationMs: number;
}

export interface Transcript {
  schemaVersion: 1;
  /** BCP-47 primary subtag. */
  language: string;
  languageDetected: boolean;
  engine: TranscriptEngine;
  speakers: Speaker[];
  segments: TranscriptSegment[];
  reviewed: boolean;
  /** Increments on every write. */
  version: number;
  /** From Settings when the transcript was made; words below it are marked. */
  lowConfidenceThreshold: number;
  /** Stretches of 10 s or more with speech on a track but no transcript; [] when none. */
  coverageGaps: CoverageGap[];
}

/** Speech the engine wrote nothing for (Whisper sometimes drops a passage). Seconds on the timeline. */
export interface CoverageGap {
  start: number;
  end: number;
  /** The track it was found on. */
  track: string | null;
}

export type TranscriptStatus = 'none' | 'queued' | 'running' | 'done' | 'failed' | 'paused';

export interface StageRemedy {
  /** Passed back as processing.retry's remedyId: "retry", "cpu", "model:<catalog id>" or "install:<catalog id>" (download a damaged model again). */
  id: string;
  label: string;
}

/** DESIGN.md §17 copy for a failed stage: what failed, what was kept, the most specific fix first. */
export interface StageFailure {
  stage: StageName;
  message: string;
  kept: string;
  remedies: StageRemedy[];
}

export type ModelEngine = 'transcription' | 'speakers' | 'ocr';

export interface ModelInstalling {
  percent: number;
  bytesDone: number;
}

export interface ModelInfo {
  id: string;
  engine: ModelEngine;
  name: string;
  description: string;
  sizeBytes: number;
  license: string;
  installed: boolean;
  /** Set while a download runs. */
  installing: ModelInstalling | null;
  recommended: boolean;
  runsOn: 'gpu' | 'cpu' | 'either';
  minVramBytes: number | null;
  /** "Most accurate", "Fast on CPU". */
  accuracyNote: string;
  /**
   * Speaker models: `segmentation` is always needed (not a choice), `embedding` is a voice model
   * Settings › Speakers chooses between. Null for transcription and OCR models.
   */
  role: ModelRole | null;
}

export type ModelRole = 'segmentation' | 'embedding';

export interface EngineStatusDetail {
  ready: boolean;
  /** "GPU" or "CPU"; null when nothing can run. */
  device: string | null;
  gpuName: string | null;
  /** Null on CPU-only machines. */
  freeVramBytes: number | null;
  /** The catalog id the engine would use, named even while it is not installed (`ready` is then false). */
  model: string | null;
  /** Why processing is paused, in words ("PC is busy"), or null. */
  paused: ProcessingPausedReason | null;
}

export interface TranscriptGetResult {
  /** Null until the first pass completes; a failed pass may still return a partial transcript. */
  transcript: Transcript | null;
  status: TranscriptStatus;
  failure: StageFailure | null;
}

export interface TranscriptEditSegmentParams {
  recordingId: string;
  segmentId: string;
  text: string;
}

export interface TranscriptEditSegmentResult {
  segment: TranscriptSegment;
  version: number;
}

export interface TranscriptSetSegmentSpeakerParams {
  recordingId: string;
  segmentId: string;
  /** Null clears the assignment. Ignored when `newSpeakerName` is given. */
  speakerId: string | null;
  /** Creates a speaker with this name and assigns it. */
  newSpeakerName?: string;
}

export interface TranscriptSetSegmentSpeakerResult {
  segment: TranscriptSegment;
  speakers: Speaker[];
}

export interface TranscriptRenameSpeakerParams {
  recordingId: string;
  speakerId: string;
  name: string;
}

export interface SpeakersResult {
  speakers: Speaker[];
}

export interface TranscriptMergeSpeakersParams {
  recordingId: string;
  fromSpeakerId: string;
  intoSpeakerId: string;
}

export interface TranscriptMergeSpeakersResult {
  speakers: Speaker[];
  segmentsChanged: number;
}

export interface TranscriptMarkReviewedParams {
  recordingId: string;
  reviewed: boolean;
}

export interface TranscriptMarkReviewedResult {
  reviewed: boolean;
}

export interface TranscriptSearchParams {
  recordingId: string;
  query: string;
}

export interface TranscriptSearchMatch {
  segmentId: string;
  /** Seconds. */
  start: number;
  /** Plain text around the match; the UI bolds the query words. */
  snippet: string;
}

export interface TranscriptSearchResult {
  matches: TranscriptSearchMatch[];
}

export interface TranscriptRetranscribeParams {
  recordingId: string;
  modelId?: string;
  language?: string;
}

export type TranscriptVersionReason = 'transcribed' | 'edited' | 'restored' | 'retranscribed';

export interface TranscriptVersion {
  id: string;
  at: string;
  reason: TranscriptVersionReason;
  engine: string | null;
  segments: number;
}

export interface TranscriptVersionsResult {
  /** Empty when version history is off. */
  versions: TranscriptVersion[];
}

export interface TranscriptRestoreVersionParams {
  recordingId: string;
  versionId: string;
}

export interface TranscriptRestoreVersionResult {
  transcript: Transcript;
}

export interface ProcessingRetryParams {
  recordingId: string;
  stage: StageName;
  /** From StageFailure.remedies: "retry", "cpu", "model:<catalog id>" ("model:whisper-small") or "install:<catalog id>". */
  remedyId?: string;
}

export interface ProcessingStageParams {
  recordingId: string;
  stage: StageName;
}

export interface ModelsListResult {
  models: ModelInfo[];
}

export interface ModelIdParams {
  modelId: string;
}

export interface EngineStatusResult {
  transcription: EngineStatusDetail;
  speakers: EngineStatusDetail;
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

export type TranscriptionTiming = 'after' | 'during';

/** M2 (BRIDGE.md, Settings snapshot M2 additions). */
export interface TranscriptionSettings {
  /** Transcribe automatically after recording (true). */
  auto: boolean;
  /** 'during' adds the live draft; the authoritative pass still runs after. */
  timing: TranscriptionTiming;
  /** true */
  pauseWhenBusy: boolean;
  /** Default: the recommended model that fits the hardware. */
  modelId: string;
  /** Default: small. */
  cpuFallbackModelId: string;
  /** 'auto' or a BCP-47 primary subtag. */
  language: string;
  /** true */
  keepWordTimestamps: boolean;
  /** 0.5 */
  lowConfidenceThreshold: number;
}

export interface SpeakerSettings {
  identify: boolean;
  /** "auto" or a whole number from 1 to 20; applied when one track has speech. */
  expectedSpeakers: 'auto' | number;
  rememberRenamed: boolean;
  embeddingModelId: string;
}

export interface HistorySettings {
  /** true */
  keepVersions: boolean;
  /** 90 */
  keepDays: number;
}

export interface SettingsSnapshot {
  theme: ThemePreference;
  /** The library folder in effect. */
  libraryPath: string;
  listDensity: ListDensity;
  recording: RecordingSettings;
  /** M2 */
  transcription: TranscriptionSettings;
  speakers: SpeakerSettings;
  history: HistorySettings;
  /** M3 */
  general: GeneralSettings;
  export: ExportSettings;
  ai: AiSettings;
  storage: StorageReclaimSettings;
}

/**
 * Partial update; omitted or null fields keep their value. The `recording` block is replaced whole
 * (the UI always sends all of it); the M2 blocks merge field by field, so they may carry only the
 * fields that change.
 */
export interface SettingsSetParams {
  theme?: ThemePreference | null;
  /** Accepted only when equal to the current location; moving fails with settings.libraryMoveUnavailable. */
  libraryPath?: string | null;
  listDensity?: ListDensity | null;
  recording?: RecordingSettings | null;
  /** M2: merged field by field on the host. */
  transcription?: Partial<TranscriptionSettings> | null;
  speakers?: Partial<SpeakerSettings> | null;
  history?: Partial<HistorySettings> | null;
  /**
   * M3: merged field by field on the host (`export.defaults` replaces whole). A missing or null
   * field keeps its value, except `export.defaultFolder: null` and `storage.reclaimOlderThanDays:
   * null`, which clear it.
   */
  general?: Partial<GeneralSettings> | null;
  export?: Partial<ExportSettings> | null;
  ai?: AiSettingsPatch | null;
  storage?: Partial<StorageReclaimSettings> | null;
}

/** settings.set's `ai` block: each field optional, `share` merged field by field; never `providers`. */
export interface AiSettingsPatch {
  enabled?: boolean;
  askBeforeSend?: boolean;
  keepRecord?: boolean;
  share?: Partial<AiShareSettings>;
}

// ---------------------------------------------------------------------------------------------
// Library and projects
// ---------------------------------------------------------------------------------------------

/** `size` (M3): largest first, for Settings › Storage › Review large recordings. */
export type LibrarySort = 'newest' | 'oldest' | 'longest' | 'title' | 'size';

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
  /** Every stage in pipeline order, finished stored and optimize included (meta.stages leaves those out). */
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

/**
 * Partial<Chapter> (ChapterPatch.cs). Add: `atMs` required, `title` defaults to "", `origin` to
 * 'user', and any `id` is ignored (the host assigns it). Update: `id` required; the fields present change.
 */
export type ChapterPatch = Partial<Chapter>;

/** annotations.addChapter and annotations.updateChapter. An unknown id answers annotations.notFound. */
export interface ChapterParams {
  recordingId: string;
  chapter: ChapterPatch;
}

export interface ChapterIdParams {
  recordingId: string;
  chapterId: string;
}

export interface ChaptersResult {
  chapters: Chapter[];
}

/**
 * Partial<Highlight> (HighlightPatch.cs). Add: `atMs` required, `note` defaults to "", `origin` to
 * 'user', `segmentId` to null, and any `id` is ignored. Update: `id` required; the fields present change.
 */
export type HighlightPatch = Partial<Highlight>;

/** annotations.addHighlight and annotations.updateHighlight. An unknown id answers annotations.notFound. */
export interface HighlightParams {
  recordingId: string;
  highlight: HighlightPatch;
}

export interface HighlightIdParams {
  recordingId: string;
  highlightId: string;
}

export interface HighlightsResult {
  highlights: Highlight[];
}

/**
 * Partial<Topic> (TopicPatch.cs) for annotations.addTopic: `label` required and not blank, `origin`
 * defaults to 'user', any `id` is ignored. A label the recording already has (ignoring case) changes nothing.
 */
export type TopicPatch = Partial<Topic>;

export interface TopicParams {
  recordingId: string;
  topic: TopicPatch;
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
  /** M2: the transcription engine's probe result, for the footer's left side. */
  detail: EngineStatusDetail;
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

/**
 * Why heavy processing waits: free space below the threshold, a recording or a busy processor
 * (while "Pause when busy" is on), or processing.pause.
 */
export type ProcessingPausedReason = 'Low disk space' | 'PC is busy' | 'Paused by you';

export interface FooterStatusPayload {
  engine: EngineStatus;
  storage: StorageStatus;
  recording: FooterRecordingStatus;
  /** Why processing is paused, in words, or null. */
  processingPaused: ProcessingPausedReason | null;
  /** M3: the running export, if any. Hosts before M3 leave it out. */
  export?: FooterExportStatus;
  /** H1: the update downloading in the background, if any. Hosts before 0.5.0 leave it out. */
  update?: FooterUpdateStatus;
}

export interface FooterUpdateStatus {
  downloading: boolean;
  percent: number | null;
  version: string | null;
}

/** updates.status / updates.check result and the updates.progress payload (BRIDGE.md "Updates"). */
export interface UpdateStatus {
  currentVersion: string;
  /** failed: only after updates.check ("Check now"); automatic checks that fail stay idle. */
  state: 'unavailable' | 'idle' | 'checking' | 'downloading' | 'ready' | 'failed';
  availableVersion: string | null;
  percent: number | null;
  lastCheckedAt: string | null;
  /** After updates.check: the newest-version line, why it failed, or that the download waits. */
  message: string | null;
  /** A newer version waits to download until no recording or processing runs. */
  deferred: boolean;
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

export type TranscriptChangeReason = 'transcribed' | 'edited' | 'speakers' | 'restored' | 'topics';

export interface TranscriptChangedPayload {
  recordingId: string;
  version: number;
  reason: TranscriptChangeReason;
}

export type ModelProgressState = 'downloading' | 'verifying' | 'done' | 'failed';

export interface ModelsProgressPayload {
  modelId: string;
  percent: number;
  bytesDone: number;
  bytesTotal: number;
  state: ModelProgressState;
  message: string | null;
}

/** A rough draft segment, seconds from the start of the session. */
export interface LiveTranscriptSegment {
  start: number;
  end: number;
  text: string;
}

export interface RecordingLiveTranscriptPayload {
  sessionId: string;
  /** The draft so far; each event replaces the last, and the full pass replaces it entirely. */
  segments: LiveTranscriptSegment[];
}

export interface StorageLowSpacePayload {
  freeBytes: number;
  thresholdBytes: number;
  recordingContinues: boolean;
  transcriptionPaused: boolean;
}

// ---------------------------------------------------------------------------------------------
// M3: agenda import, attachments, media import, export, remaining Settings (BRIDGE.md M3)
// ---------------------------------------------------------------------------------------------

export type AgendaSourceKind = 'text' | 'pastedText' | 'markdown' | 'csv' | 'tsv' | 'docx' | 'xlsx' | 'pdf' | 'image';

/** One item the local parser found. `level` 0 is a top-level item; `location` like "page 2, line 14". */
export interface AgendaParsedItem {
  text: string;
  uncertain: boolean;
  uncertainReason: string | null;
  level: number;
  location: string | null;
}

export interface AgendaWarning {
  code: string;
  message: string;
}

export interface AgendaParsePreview {
  /** The file name, or "Pasted text". */
  source: string;
  sourceKind: AgendaSourceKind;
  title: string | null;
  items: AgendaParsedItem[];
  warnings: AgendaWarning[];
  /** "Windows OCR" when an image was read. */
  ocrEngine: string | null;
  /** Handle the host keeps for the original file until agenda.apply or agenda.discard. */
  attachmentToken: string | null;
}

export type AttachmentKind = 'agenda' | 'file';

export interface Attachment {
  id: string;
  name: string;
  sizeBytes: number;
  addedAt: string;
  kind: AttachmentKind;
  contentType: string | null;
}

export type AudioExportFormat = 'flac' | 'wav' | 'mp3';
export type TranscriptExportFormat = 'json' | 'markdown' | 'text' | 'srt';
export type DocumentExportFormat = 'docx' | 'pdf' | 'markdown';

export interface ExportAudioChoice {
  on: boolean;
  format: AudioExportFormat;
  /** MP3 only; null for FLAC and WAV. */
  bitrateKbps: number | null;
}

export interface ExportSelection {
  audioMixed: ExportAudioChoice;
  tracks: ExportAudioChoice;
  transcript: { on: boolean; formats: TranscriptExportFormat[] };
  /** M4 fills this; M3 exports nothing here. */
  documents: { on: boolean; documentIds: string[]; format: DocumentExportFormat };
  details: { on: boolean };
  attachments: { on: boolean };
}

/** The components of an export, named as the keys of ExportSelection. */
export type ExportComponent = keyof ExportSelection;

export interface ExportEstimateItem {
  /** Which row of the dialog the file belongs to (BRIDGE.md M3 clarification 1). */
  component: ExportComponent;
  /** The file name as it will be written. */
  name: string;
  bytes: number;
}

/** A ticked row that has nothing to write, and why. */
export interface ExportUnavailable {
  component: ExportComponent;
  /** "Not transcribed yet", "No attachments". */
  reason: string;
}

/** Sizes for lossy formats are estimates. */
export interface ExportEstimate {
  files: number;
  bytes: number;
  items: ExportEstimateItem[];
  unavailable: ExportUnavailable[];
}

/** `createSubfolder`: a folder named after the recording (sanitised title + date) inside `folder`. */
export interface ExportDestination {
  folder: string;
  createSubfolder: boolean;
}

export interface LibraryUsageLargest {
  recordingId: string;
  title: string;
  sizeBytes: number;
}

export interface LibraryUsage {
  totalBytes: number;
  freeBytes: number;
  count: number;
  largest: LibraryUsageLargest | null;
}

export interface AgendaImportFileParams {
  /** Null while the recording does not exist yet (M3 clarification 2); parsing does not need it. */
  recordingId: string | null;
  /** Without a path the host shows the file picker. */
  path?: string;
}

export interface AgendaImportDroppedParams {
  recordingId: string | null;
  /** The dropped File objects' names; the host matches them to the pending WebView2 drop. */
  paths: string[];
}

export interface AgendaImportResult {
  preview: AgendaParsePreview | null;
  cancelled: boolean;
}

export interface AgendaParseTextParams {
  recordingId: string | null;
  text: string;
}

export interface AgendaParseTextResult {
  preview: AgendaParsePreview;
}

export interface AgendaApplyItem {
  text: string;
  uncertain: boolean;
  uncertainReason: string | null;
}

/** At most AGENDA_MAX_ITEMS items of at most AGENDA_MAX_ITEM_LENGTH characters. */
export interface AgendaApplyParams {
  recordingId: string;
  items: AgendaApplyItem[];
  source: string;
  sourceKind: AgendaSourceKind;
  attachmentToken: string | null;
}

export interface AgendaDiscardParams {
  attachmentToken: string;
}

export interface AgendaSetCoveredParams {
  recordingId: string;
  itemId: string;
  covered: boolean;
}

export interface AgendaResult {
  agenda: Agenda;
}

/** The host refuses longer items (agenda.itemTooLong) and more items (agenda.tooManyItems). */
export const AGENDA_MAX_ITEM_LENGTH = 200;
export const AGENDA_MAX_ITEMS = 200;

export interface AttachmentsListResult {
  attachments: Attachment[];
}

export interface AttachmentsAddParams {
  recordingId: string;
  /** Without a path the host shows the file picker. 100 MB per file (attachments.tooLarge). */
  path?: string;
}

export interface AttachmentsAddResult {
  attachment: Attachment | null;
  cancelled: boolean;
}

export interface AttachmentIdParams {
  recordingId: string;
  attachmentId: string;
}

export interface LibraryImportMediaParams {
  /** Without a path the host shows the file picker. */
  path?: string;
  title?: string;
  type?: RecordingType;
}

export interface LibraryImportMediaResult {
  recordingId: string | null;
  cancelled: boolean;
}

export interface ProjectChangeTypeParams {
  recordingId: string;
  /** A built-in type or a custom name of 1 to 40 characters. */
  type: RecordingType;
}

export interface ExportEstimateParams {
  recordingId: string;
  selection: ExportSelection;
}

export interface ExportRunParams {
  recordingId: string;
  selection: ExportSelection;
  destination: ExportDestination;
  /** Stores selection and destination as the Settings › Export defaults. */
  remember: boolean;
}

export interface JobResult {
  jobId: string;
}

export interface JobIdParams {
  jobId: string;
}

export interface LibraryRebuildIndexResult {
  recordings: number;
}

export interface LibraryMoveParams {
  newPath: string;
}

export type ReclaimCodec = 'aac' | 'mp3';

export interface StorageReclaimParams {
  /** null: every recording older than settings.storage.reclaimOlderThanDays. */
  recordingIds: string[] | null;
  downmixMono: boolean;
  codec: ReclaimCodec;
  /** Null or left out: the default for the codec (192 kbps). */
  bitrateKbps?: number | null;
}

export type AiProvider = 'anthropic' | 'openai';

export interface AiSetKeyParams {
  provider: AiProvider;
  key: string;
}

export interface AiProviderParams {
  provider: AiProvider;
}

/** The key itself is never returned. */
export interface AiKeyResult {
  hasKey: boolean;
}

export interface AppStartupParams {
  startWithWindows: boolean;
}

export type JobState = 'running' | 'done' | 'failed' | 'cancelled';

export interface ExportProgressPayload {
  jobId: string;
  recordingId: string;
  percent: number;
  currentFile: string | null;
  state: JobState;
  message: string | null;
  outputFolder: string | null;
  files: number;
  bytes: number;
}

export interface LibraryMoveProgressPayload {
  jobId: string;
  percent: number;
  /** running, done or failed: moves and reclaims cannot be cancelled. */
  state: JobState;
  message: string | null;
  newPath: string;
}

export interface StorageReclaimProgressPayload {
  jobId: string;
  percent: number;
  /** running, done or failed: moves and reclaims cannot be cancelled. */
  state: JobState;
  message: string | null;
  recordingsDone: number;
  bytesFreed: number;
}

/** status.footer (M3): "Exporting {title} · 42%". */
export interface FooterExportStatus {
  active: boolean;
  percent: number | null;
  title: string | null;
}

export interface GeneralSettings {
  startWithWindows: boolean;
  /** Stored now; applied in M5. */
  keepRunningInTray: boolean;
  language: 'en';
  /** H1: check at start and daily and download in the background; off = only "Check now". Default true. */
  autoUpdate: boolean;
}

export interface ExportSettings {
  saveCopiesOutside: boolean;
  defaultFolder: string | null;
  askWhereEachTime: boolean;
  createSubfolder: boolean;
  defaults: ExportSelection;
}

/** What may be sent to an external AI service. Audio and video never are. */
export interface AiShareSettings {
  transcript: boolean;
  details: boolean;
  participants: boolean;
  agenda: boolean;
  highlights: boolean;
  attachments: boolean;
}

export interface AiProviderStatus {
  hasKey: boolean;
}

/** What settings.set accepts for `ai`: the providers are read-only (keys go through ai.setKey). */
export interface AiSettingsInput {
  /** false by default */
  enabled: boolean;
  askBeforeSend: boolean;
  keepRecord: boolean;
  share: AiShareSettings;
}

export interface AiSettings extends AiSettingsInput {
  /** Read side only: whether a key is stored, never the key. */
  providers: Record<AiProvider, AiProviderStatus>;
}

export interface StorageReclaimSettings {
  /** null: never. */
  reclaimOlderThanDays: number | null;
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
  'transcript.get': { params: RecordingIdParams; result: TranscriptGetResult };
  'transcript.editSegment': { params: TranscriptEditSegmentParams; result: TranscriptEditSegmentResult };
  'transcript.setSegmentSpeaker': { params: TranscriptSetSegmentSpeakerParams; result: TranscriptSetSegmentSpeakerResult };
  'transcript.renameSpeaker': { params: TranscriptRenameSpeakerParams; result: SpeakersResult };
  'transcript.mergeSpeakers': { params: TranscriptMergeSpeakersParams; result: TranscriptMergeSpeakersResult };
  'transcript.markReviewed': { params: TranscriptMarkReviewedParams; result: TranscriptMarkReviewedResult };
  'transcript.search': { params: TranscriptSearchParams; result: TranscriptSearchResult };
  'transcript.retranscribe': { params: TranscriptRetranscribeParams; result: EmptyResult };
  'transcript.versions': { params: RecordingIdParams; result: TranscriptVersionsResult };
  'transcript.restoreVersion': { params: TranscriptRestoreVersionParams; result: TranscriptRestoreVersionResult };
  'processing.retry': { params: ProcessingRetryParams; result: EmptyResult };
  'processing.cancel': { params: ProcessingStageParams; result: EmptyResult };
  'processing.pause': { params: EmptyParams; result: EmptyResult };
  'processing.resume': { params: EmptyParams; result: EmptyResult };
  'models.list': { params: EmptyParams; result: ModelsListResult };
  'models.install': { params: ModelIdParams; result: EmptyResult };
  'models.cancelInstall': { params: ModelIdParams; result: EmptyResult };
  'models.remove': { params: ModelIdParams; result: EmptyResult };
  'engine.status': { params: EmptyParams; result: EngineStatusResult };
  // M3
  'agenda.importFile': { params: AgendaImportFileParams; result: AgendaImportResult };
  'agenda.importDropped': { params: AgendaImportDroppedParams; result: AgendaImportResult };
  'agenda.parseText': { params: AgendaParseTextParams; result: AgendaParseTextResult };
  'agenda.apply': { params: AgendaApplyParams; result: Project };
  'agenda.discard': { params: AgendaDiscardParams; result: EmptyResult };
  'agenda.setCovered': { params: AgendaSetCoveredParams; result: AgendaResult };
  'attachments.list': { params: RecordingIdParams; result: AttachmentsListResult };
  'attachments.add': { params: AttachmentsAddParams; result: AttachmentsAddResult };
  'attachments.remove': { params: AttachmentIdParams; result: EmptyResult };
  'attachments.open': { params: AttachmentIdParams; result: EmptyResult };
  'library.importMedia': { params: LibraryImportMediaParams; result: LibraryImportMediaResult };
  'project.changeType': { params: ProjectChangeTypeParams; result: Project };
  'export.estimate': { params: ExportEstimateParams; result: ExportEstimate };
  'export.run': { params: ExportRunParams; result: JobResult };
  'export.cancel': { params: JobIdParams; result: EmptyResult };
  'export.openFolder': { params: JobIdParams; result: EmptyResult };
  'library.usage': { params: EmptyParams; result: LibraryUsage };
  'library.rebuildIndex': { params: EmptyParams; result: LibraryRebuildIndexResult };
  'library.move': { params: LibraryMoveParams; result: JobResult };
  'storage.reclaim': { params: StorageReclaimParams; result: JobResult };
  'ai.setKey': { params: AiSetKeyParams; result: AiKeyResult };
  'ai.clearKey': { params: AiProviderParams; result: AiKeyResult };
  'app.setStartup': { params: AppStartupParams; result: AppStartupParams };
  // H1
  'updates.status': { params: EmptyParams; result: UpdateStatus };
  'updates.check': { params: EmptyParams; result: UpdateStatus };
  'updates.apply': { params: EmptyParams; result: EmptyResult };
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
  'transcript.changed': TranscriptChangedPayload;
  'models.progress': ModelsProgressPayload;
  'recording.liveTranscript': RecordingLiveTranscriptPayload;
  // M3
  'export.progress': ExportProgressPayload;
  'library.moveProgress': LibraryMoveProgressPayload;
  'storage.reclaimProgress': StorageReclaimProgressPayload;
  // H1
  'updates.progress': UpdateStatus;
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
  'transcript.get',
  'transcript.editSegment',
  'transcript.setSegmentSpeaker',
  'transcript.renameSpeaker',
  'transcript.mergeSpeakers',
  'transcript.markReviewed',
  'transcript.search',
  'transcript.retranscribe',
  'transcript.versions',
  'transcript.restoreVersion',
  'processing.retry',
  'processing.cancel',
  'processing.pause',
  'processing.resume',
  'models.list',
  'models.install',
  'models.cancelInstall',
  'models.remove',
  'engine.status',
  'agenda.importFile',
  'agenda.importDropped',
  'agenda.parseText',
  'agenda.apply',
  'agenda.discard',
  'agenda.setCovered',
  'attachments.list',
  'attachments.add',
  'attachments.remove',
  'attachments.open',
  'library.importMedia',
  'project.changeType',
  'export.estimate',
  'export.run',
  'export.cancel',
  'export.openFolder',
  'library.usage',
  'library.rebuildIndex',
  'library.move',
  'storage.reclaim',
  'ai.setKey',
  'ai.clearKey',
  'app.setStartup',
  'updates.status',
  'updates.check',
  'updates.apply',
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
  'transcript.changed',
  'models.progress',
  'recording.liveTranscript',
  'export.progress',
  'library.moveProgress',
  'storage.reclaimProgress',
  'updates.progress',
] as const satisfies readonly EventName[];
