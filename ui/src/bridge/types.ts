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
  /** Passed back as processing.retry's remedyId: "retry", "cpu" or "model:<catalog id>". */
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
  /** From StageFailure.remedies: "retry", "cpu" or "model:<catalog id>" ("model:whisper-small"). */
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
] as const satisfies readonly EventName[];
