# Memento — Bridge contract

The host ↔ UI contract (ARCHITECTURE.md §3). Every method and event listed here exists as a C# record in `src/Memento.Core/Bridge/Contracts/` and a TypeScript type in `ui/src/bridge/types.ts`. JSON field names are camelCase. Times are ISO 8601 with offset unless the field ends in `Ms` (milliseconds, integer). Ids are opaque strings. Lists are never null; use `[]`.

Status: **M0 and M1 methods and events are implemented** in the host (`src/Memento.Core/Bridge/Methods/`) and in the UI's browser-preview mock (`ui/src/bridge/mock*.ts`); this document is what both follow. `ContractSerializationTests` pins the host's JSON, and `ContractDocumentationTests` checks that every host error code and stage name is listed here and in `types.ts`.

## Shared types

```ts
type RecordingType = 'meeting' | 'interview' | 'presentation' | 'lecture' | 'dictation' | 'research' | 'general' | string; // any other string is a custom type name
type StageName = 'stored' | 'transcript' | 'speakers' | 'minutes' | 'optimize'; // pipeline order
type StageState = 'done' | 'active' | 'queued' | 'failed';
interface StageStatus { stage: StageName; state: StageState; percent: number | null; /* M1: */ label: string | null; /* e.g. "64% · local GPU", "Done", "Queued", "Transcript failed" */ }

interface RecordingSummary {                // one Library row / card
  id: string; title: string; type: RecordingType;
  createdAt: string; durationMs: number;
  participantCount: number;                 // people listed for the recording; 0 = a solo recording ("Just me"), 1 = "1 speaker"
  hasVideo: boolean;
  stages: StageStatus[];                    // [] means "Audio only"; a finished `stored` or `optimize` stage is left out (see Stages below)
  // M1:
  people: string[];                         // participant names + renamed speakers, for search and the meta line
  isProcessing: boolean;                    // any stage active or queued
  state: 'recording' | 'finalizing' | 'ready' | 'recovered' | 'failed';
  sizeBytes: number;                        // size of the project folder on disk (kept in the index)
}

interface AudioSource {                     // M1
  id: string;                               // stable within a session: "mic:<endpointId>", "system:<endpointId>", "app:<pid>"
  kind: 'microphone' | 'system' | 'application';
  name: string;                             // "Shure MV7", "Everything this PC plays", "Zoom"
  detail: string;                           // "USB", "Default output", "Only this app"
  isDefault: boolean;
  processId: number | null;                 // application sources only
}

interface Track { id: string; sourceId: string; sourceKind: AudioSource['kind']; name: string; file: string; /* relative to the project folder */ sampleRate: number; channels: number; durationMs: number; sha256: string | null; startOffsetMs: number; /* 0 for tracks that started with the session; the timeline position where a track added mid-session begins */ endedEarlyAtMs: number | null; }

interface Agenda { source: string | null; /* "agenda.docx" */ parsedLocally: boolean; items: AgendaItem[]; }
interface AgendaItem { id: string; text: string; covered: boolean; uncertain: boolean; uncertainReason: string | null; }

interface RecordingDetails {
  title: string; type: RecordingType; participants: string[]; purpose: string; platform: string;
  organization: string; location: string; notes: string; tags: string[]; agenda: Agenda;
}

interface Chapter { id: string; atMs: number; title: string; origin: 'user' | 'local' | 'ai'; }
interface Highlight { id: string; atMs: number; note: string; origin: 'user' | 'local' | 'ai'; segmentId: string | null; }
interface Topic { id: string; label: string; origin: 'user' | 'local' | 'ai'; }

interface HistoryEntry { at: string; stage: StageName | 'recorded' | 'recovered' | 'edited'; event: 'started' | 'completed' | 'failed' | 'info'; summary: string; detail: string | null; /* engine, model, device, duration, what was sent */ }

interface Project {                         // M1: everything Review needs for one recording
  summary: RecordingSummary;
  details: RecordingDetails;
  tracks: Track[];
  mixUrl: string | null;                    // https://library.memento/<id>/mix.flac once finalized
  peaksUrl: string | null;                  // https://library.memento/<id>/peaks.json
  chapters: Chapter[]; highlights: Highlight[]; topics: Topic[];
  history: HistoryEntry[];
  integrity: { algorithm: 'sha256'; computedAt: string | null };
  sizeBytes: number;
}
```

The host maps a second virtual host, `https://library.memento/`, to the library's `projects` folder (`<library>/projects`, from `LibraryUrls.MappedFolder`), so a URL is `https://library.memento/<id>/<file inside the project folder>`. The library root itself is not served: `library.db` and anything else beside `projects` cannot be reached from the page, and because the browser resolves `.` and `..` segments (also `%2e%2e`) before the path reaches the folder, no URL can climb out of `projects`. The host never hands out a URL with a `.` or `..` segment; ids and file names are escaped segment by segment. The mapping uses `CoreWebView2HostResourceAccessKind.Allow` (not DenyCors: the page lives on `https://app.memento/`, and DenyCors would block its cross-origin `fetch` of `peaks.json` while `<audio>` would still work). Navigation stays restricted to `app.memento`, so only our page can read it. When the library folder changes, the host clears the mapping and maps the new `projects` folder. The UI never constructs those URLs itself; it uses the ones the host returns.

`peaks.json` (written by `Memento.Audio.Mixing.PeakBuilder`): `{ "schemaVersion": 1, "windowMs": 50, "peaks": [[rms, peak], …] }` with both values linear in 0..1 over all channels of the mix, one pair per window; the duration is `peaks.length × windowMs`. The UI draws `peak` as the outer waveform.

A project exists from `recording.start` onwards: `project.get`, `project.updateDetails` and `annotations.*` work during recording (highlight notes and pre-start details are saved then). `library.list` returns projects in every state except `recording`; a `finalizing` project appears with its `stored` stage active, which is what the processing card shows.

### Stages

M1 runs two stages; M2 adds `transcript`, `speakers` and `minutes` between them.

- `stored` — finalize: tracks to lossless FLAC, the mix, `peaks.json` and SHA-256 hashes. Runs for every recording.
- `optimize` — only when Settings › Recording › storage asks for AAC or MP3: converts the lossless FLAC files to that smaller format, after every other stage (the transcript is always made from the lossless files). If the setting is FLAC, the stage is not listed at all. When it fails, the FLAC files are kept.

`RecordingSummary.stages` (Library rows and `library.processing.current.meta`) leaves out a **finished** `stored` or `optimize` stage, so a recording with nothing else run reads "Audio only" (DESIGN.md §4). Either stage is listed while it is active or queued and when it failed. `library.processing.current.stages` and `processing.progress.stages` always carry every stage, finished `stored` and `optimize` included.

UI names (`ui/src/format/recording.ts`): done or queued pill "Stored" / "Smaller files"; active pill "Storing" / "Making smaller"; processing-card column "Stored" / "Smaller files". The UI's `stagePills` also hides a finished `stored` or `optimize` stage unless some stage failed.

### History

`HistoryEntry.stage` and `event` values the host writes to `history.jsonl` in M1:

| `stage` | `event`s | Written when |
|---|---|---|
| `recorded` | `started`, `completed`, `info` | recording starts; recording stops (`info` when the host stopped it, e.g. drive full) |
| `stored` | `started`, `completed`, `failed`, `info` | finalize begins, ends or fails; `info` for a warning while storing |
| `optimize` | `started`, `completed`, `failed`, `info` | conversion to the smaller format; `info` for "Kept as lossless FLAC" and "Kept the separate tracks" |
| `recovered` | `info` | an interrupted recording was repaired at launch |
| `edited` | `info` | `project.updateDetails` ("Details edited") and `project.rename` ("Renamed") |

From M2 the `transcript`, `speakers` and `minutes` stages write `started`, `completed` and `failed` lines too.

## Methods

| Method | Params | Result | Notes |
|---|---|---|---|
| `app.version` | `{}` | `{ version, osVersion, isDarkTheme }` | M0 |
| `app.openExternal` | `{ url }` | `{ opened }` | M0. https: or ms-settings: only |
| `ui.ready` | `{}` | `{}` | M0 |
| `settings.get` | `{}` | `SettingsSnapshot` | M0; M1 extends the snapshot (below) |
| `settings.set` | `Partial<SettingsSnapshot>` (nulls keep) | `SettingsSnapshot` | M0; M1 adds fields. Top-level fields merge; the `recording` block is **replaced whole** when present (the UI always sends the full block). |
| `library.list` | `{ query?: string, type?: RecordingType \| 'all', sort?: 'newest' \| 'oldest' \| 'longest' \| 'title' }` | `{ recordings: RecordingSummary[], totalDurationMs, totalCount }` | M1 adds params. `query` searches titles, people and (M2) transcripts. Result reflects the filter. |
| `library.processing` | `{}` | `{ current: { recordingId, title, meta: RecordingSummary, stages: StageStatus[] } \| null, othersCount }` | M1. The processing card: the newest recording with a stage active or queued. `stages` lists every stage, finished `stored` and `optimize` included; `meta.stages` follows the row rule (see Stages). |
| `project.get` | `{ recordingId }` | `Project` | M1 |
| `project.updateDetails` | `{ recordingId, details: Partial<RecordingDetails> }` | `Project` | M1. Works during recording too. |
| `project.deleteEstimate` | `{ recordingId }` | `{ title, sizeBytes, items: string[] }` | M1. Feeds the delete confirmation copy. `items` are lower-case noun phrases the UI joins into one sentence, e.g. `["the recording", "its 3 tracks", "its transcript", "its 2 documents"]`. |
| `project.delete` | `{ recordingId }` | `{}` | M1. Refused with `project.recording` while that recording is active. |
| `project.rename` | `{ recordingId, title }` | `Project` | M1 |
| `annotations.addChapter` | `{ recordingId, chapter: Partial<Chapter> }` | `{ chapters }` | M1. `chapter.atMs` is required; `title` defaults to `""`, `origin` to `'user'`. |
| `annotations.updateChapter` | `{ recordingId, chapter: Partial<Chapter> }` | `{ chapters }` | M1. `chapter.id` names the chapter (required); the other fields present are changed. |
| `annotations.removeChapter` | `{ recordingId, chapterId }` | `{ chapters }` | M1 |
| `annotations.addHighlight` | `{ recordingId, highlight: Partial<Highlight> }` | `{ highlights }` | M1. `highlight.atMs` is required; `note` defaults to `""`, `origin` to `'user'`, `segmentId` to `null`. |
| `annotations.updateHighlight` | `{ recordingId, highlight: Partial<Highlight> }` | `{ highlights }` | M1. `highlight.id` names the highlight (required); the other fields present are changed. |
| `annotations.removeHighlight` | `{ recordingId, highlightId }` | `{ highlights }` | M1 |
| `annotations.addTopic` | `{ recordingId, topic: Partial<Topic> }` | `{ topics }` | M1. `topic.label` is required (not blank); `origin` defaults to `'user'`. A label the recording already has (ignoring case) changes nothing. |
| `annotations.removeTopic` | `{ recordingId, topicId }` | `{ topics }` | M1. There is no `updateTopic`. |
| `sources.list` | `{}` | `{ audio: AudioSource[], videoAvailable: false }` | M1. Re-enumerates each call. |
| `recording.start` | `{ title, type, sourceIds: string[] }` | `{ sessionId, recordingId, startedAt }` | M1. Fails with `recording.noSources` if empty, `recording.sourceUnavailable` (detail names it) if one cannot open; the others are not started in that case. `recording.alreadyActive` while another session is recording or paused, `recording.diskFull` if the library drive is too full to start. |
| `recording.setSource` | `{ sessionId, sourceId, enabled }` | `{ tracks: Track[] }` | M1. Starts or ends one track. |
| `recording.pause` / `recording.resume` | `{ sessionId }` | `{}` | M1 |
| `recording.markHighlight` | `{ sessionId, note?: string }` | `{ highlight: Highlight }` | M1 |
| `recording.stop` | `{ sessionId }` | `{ recordingId }` | M1. Returns when finalize has started; `recording.state` events report `finalizing` then `ready`. |
| `recording.current` | `{}` | `{ session: RecordingStatePayload \| null }` | M1. Lets the UI rejoin an active session after a reload. |
| `recovery.list` | `{}` | `{ items: { recordingId, title, startedAt, tracksIntact, tracksTotal, lastCheckpointAt, recoveredDurationMs, mayBeMissingMs }[] }` | M1. Projects repaired at launch. |
| `recovery.acknowledge` | `{ recordingId }` | `{}` | M1. Dismisses the dialog for this project. |
| `dialog.pickFolder` | `{ title, initialPath?: string }` | `{ path: string \| null }` | M1 |
| `status.get` | `{}` | `FooterStatusPayload` | M1 |

`annotations.*` in detail: each method answers the recording's whole list after the change, chapters and highlights sorted by `atMs`, topics in the order they were added. On an add the host assigns the id (`c…`, `h…`, `t…`, opaque); an `id` sent with an add is ignored. An update or remove naming an id the recording does not have answers `annotations.notFound` (`detail` is that id) and changes nothing; an unknown `recordingId` answers `project.notFound`; a missing `atMs`, a blank topic label, an update without an id or an `origin` other than `user`, `local`, `ai` answers `bridge.invalidParams`. All of them work during recording (`recording.markHighlight` adds through the session; the note is then saved with `annotations.updateHighlight`).

## Events

| Event | Payload | Notes |
|---|---|---|
| `theme.changed` | `{ isDark }` | M0 |
| `status.footer` | `{ engine: { ready, device }, storage: { freeBytes, lowSpace }, /* M1: */ recording: { active: boolean, lastCheckpointAt: string \| null, lostSource: string \| null }, processingPaused: string \| null /* reason */ }` | M0, extended in M1. `processingPaused` is the reason in words, shown after "Transcription paused · ". M1 sends exactly one value, `"Low disk space"` (`FooterStatusService.LowSpaceReason`), whenever free space on the library drive is below the low-space threshold or a recording paused processing for that reason; otherwise `null`. |
| `recording.state` | `RecordingStatePayload = { sessionId, recordingId, state: 'recording' \| 'paused' \| 'finalizing' \| 'ready' \| 'stopped', startedAt, elapsedMs /* recorded time, excluding paused time */, tracks: Track[], lastCheckpointAt, highlightsCount }` | M1, on every change and at least every second while recording |
| `recording.levels` | `{ sessionId, levels: { sourceId: string, rms: number /* 0..1 */, peak: number }[] }` | M1, ≤ 30 per second |
| `recording.sourceLost` | `{ sessionId, sourceId, name, atMs, remaining: string[] }` | M1 |
| `recording.stoppedByHost` | `{ sessionId, recordingId, reason: 'diskFull' \| 'deviceLost' \| 'error', atMs, message }` | M1 |
| `library.changed` | `{ recordingIds: string[] }` | M1, after any project write; the UI refetches |
| `processing.progress` | `{ recordingId, stages: StageStatus[] }` | M1. Every stage, finished ones included: `stored`, and `optimize` when the storage format is AAC or MP3; `transcript`/`speakers`/`minutes` from M2. |
| `storage.lowSpace` | `{ freeBytes, thresholdBytes, recordingContinues: boolean, transcriptionPaused: boolean }` | M1, banner |

## Settings snapshot (M1)

```ts
interface SettingsSnapshot {
  theme: 'system' | 'light' | 'dark'; libraryPath: string; listDensity: 'comfortable' | 'compact';
  recording: {
    defaultType: RecordingType; defaultSourceIds: string[];            // remembered selection
    keepSeparateTracks: true;                                           // fixed in M1
    storage: { codec: 'flac' | 'aac' | 'mp3'; bitrateKbps: number | null; downmixMono: boolean; keepOnlyMix: boolean }; // aac/mp3: the optimize stage converts after processing
    checkpointSeconds: number;                                          // 30
    lowSpaceGb: number;                                                 // 10
  };
}
```

## Error codes

Every error is `{ code, message, detail }`. Codes are `area.reason`; the UI lists them in `ERROR_CODES` (`ui/src/bridge/types.ts`), the host in `BridgeErrorCodes.cs` (router) and `DomainErrorCodes.cs` (methods). Messages follow DESIGN.md §17: name the thing, give the amount or time, say what is safe, offer the fix.

The router's own codes carry the `bridge.` prefix (M0):

| Code | When |
|---|---|
| `bridge.invalidJson` | The message is not JSON. |
| `bridge.invalidRequest` | The message is not a request (`id`, `method`, `params`), or a project file on disk has a schema this version cannot read. |
| `bridge.unknownMethod` | No such method. |
| `bridge.invalidParams` | The params do not match the method (missing or unknown fields, wrong types) or a value is out of range (title length, `atMs`, sort, origin, …). |
| `bridge.cancelled` | The request was cancelled because Memento is closing. |
| `bridge.internal` | An unexpected failure; the message still says what is safe. |

Method codes:

| Code | When |
|---|---|
| `app.openExternal.unsupportedTarget` | `app.openExternal` was given something other than a complete https: link or an ms-settings: page (M0). |
| `app.openExternal.failed` | Windows could not open an allowed link (M0). |
| `settings.invalidValue` | `settings.set` with a value that is not available (theme, list density, a recording setting); nothing was changed (M0). |
| `settings.libraryMoveUnavailable` | `settings.set` with a different `libraryPath`; moving the library is not available in this version. |
| `project.notFound` | The `recordingId` names no project in the library (also for ids that are not valid project ids). |
| `project.recording` | `project.delete` while that recording is recording or being saved. |
| `annotations.notFound` | `annotations.update*` / `annotations.remove*` named a chapter, highlight or topic the recording does not have; `detail` is the id. |
| `recording.noSources` | `recording.start` without a source. |
| `recording.sourceUnavailable` | A chosen source could not be opened (`detail` names it); nothing was started. |
| `recording.noSession` | The `sessionId` names no active session. |
| `recording.diskFull` | `recording.start` while the library drive is too full to record; nothing was started. (A drive that fills during recording stops it through `recording.stoppedByHost`.) |
| `recording.alreadyActive` | `recording.start` while another session is recording or paused; `detail` is that session's id. |

The UI's bridge client adds two codes of its own, never sent by the host: `bridge.timeout` (no answer in time) and `bridge.sendFailed` (the request could not be posted).

---

# M2 — Transcription and speakers (contract; to be implemented)

Everything below is additive. Host and UI build to it from the same text; the UI's mock implements it with sample data.

## Shared types (M2)

```ts
interface TranscriptWord { w: string; s: number; e: number; c: number }          // seconds; c = confidence 0..1
interface TranscriptSegment {
  id: string; start: number; end: number;                                        // seconds
  track: string | null;                                                          // track id the words came from
  speaker: string | null; speakerConfidence: number | null;                      // speaker id; null when speakers were not identified
  text: string; confidence: number;                                              // min of word confidences
  words: TranscriptWord[];                                                       // [] when word timestamps are off
  edited: { at: string; original: string } | null;
}
interface Speaker { id: string; name: string; renamed: boolean; color: 1 | 2 | 3 | 4; talkTimeMs: number }
interface Transcript {
  schemaVersion: 1; language: string; languageDetected: boolean;
  engine: { name: string; model: string; device: string; version: string; durationMs: number };
  speakers: Speaker[]; segments: TranscriptSegment[];
  reviewed: boolean; version: number;                                            // version increments on every write
  lowConfidenceThreshold: number;                                                // from settings at generation time
}
type TranscriptStatus = 'none' | 'queued' | 'running' | 'done' | 'failed' | 'paused';
interface StageFailure { stage: StageName; message: string; kept: string; remedies: { id: string; label: string }[] }   // DESIGN §17 copy: what failed, what was kept, most specific fix first
interface ModelInfo {
  id: string; engine: 'transcription' | 'speakers' | 'ocr'; name: string; description: string;
  sizeBytes: number; license: string; installed: boolean; installing: { percent: number; bytesDone: number } | null;
  recommended: boolean; runsOn: 'gpu' | 'cpu' | 'either'; minVramBytes: number | null; accuracyNote: string;   // "Most accurate", "Fast on CPU"
}
interface EngineStatusDetail { ready: boolean; device: string | null; gpuName: string | null; freeVramBytes: number | null; model: string | null; paused: string | null }
```

`StageName` gains `transcript` and `speakers` as running stages (they already exist as pill names). `HistoryEntry.stage` gains `transcript`, `speakers`, `topics`; `detail` carries engine, model, device, duration and segment count.

## Methods (M2)

| Method | Params | Result | Notes |
|---|---|---|---|
| `transcript.get` | `{ recordingId }` | `{ transcript: Transcript \| null, status: TranscriptStatus, failure: StageFailure \| null }` | `transcript` is null until the first pass completes; a failed pass may still return a partial transcript with `status: 'failed'`. |
| `transcript.editSegment` | `{ recordingId, segmentId, text }` | `{ segment: TranscriptSegment, version }` | Keeps `edited.original` from the first edit. Words are re-aligned proportionally (confidence set to 1 for edited words). |
| `transcript.setSegmentSpeaker` | `{ recordingId, segmentId, speakerId: string \| null, newSpeakerName?: string }` | `{ segment, speakers }` | `newSpeakerName` creates a speaker and assigns it. |
| `transcript.renameSpeaker` | `{ recordingId, speakerId, name }` | `{ speakers }` | Updates every segment by reference; `renamed: true`. |
| `transcript.mergeSpeakers` | `{ recordingId, fromSpeakerId, intoSpeakerId }` | `{ speakers, segmentsChanged }` | |
| `transcript.markReviewed` | `{ recordingId, reviewed }` | `{ reviewed }` | |
| `transcript.search` | `{ recordingId, query }` | `{ matches: { segmentId, start, snippet }[] }` | Case-insensitive, word-boundary aware. |
| `transcript.retranscribe` | `{ recordingId, modelId?: string, language?: string }` | `{}` | Queues a new pass; when version history is on the current transcript is kept as a version. Refused with `project.recording` while recording. |
| `transcript.versions` | `{ recordingId }` | `{ versions: { id, at, reason: 'transcribed' \| 'edited' \| 'restored' \| 'retranscribed', engine: string \| null, segments: number }[] }` | Empty when history is off. |
| `transcript.restoreVersion` | `{ recordingId, versionId }` | `{ transcript }` | The replaced transcript becomes a version. |
| `processing.retry` | `{ recordingId, stage, remedyId?: string }` | `{}` | `remedyId` from `StageFailure.remedies` (e.g. `cpu`, `model:small`). |
| `processing.cancel` | `{ recordingId, stage }` | `{}` | Partial results are kept. |
| `processing.pause` / `processing.resume` | `{}` | `{}` | Global; shown in the footer as "Transcription paused". |
| `models.list` | `{}` | `{ models: ModelInfo[] }` | Catalog plus installed state; re-reads disk. |
| `models.install` | `{ modelId }` | `{}` | Downloads with SHA-256 verification; progress via `models.progress`. One download at a time. |
| `models.cancelInstall` | `{ modelId }` | `{}` | Removes the partial file. |
| `models.remove` | `{ modelId }` | `{}` | Refused with `models.inUse` while a stage is using it. |
| `engine.status` | `{}` | `{ transcription: EngineStatusDetail, speakers: EngineStatusDetail }` | Probe result; `freeVramBytes` null on CPU-only. |
| `library.list` | (as M1) | `RecordingSummary` | `query` now also matches transcript text (FTS); the summary gains `matchSnippet: string \| null` for transcript hits. |

## Events (M2)

| Event | Payload | Notes |
|---|---|---|
| `processing.progress` | (as M1) stages now include `transcript` and `speakers` with `percent` and `label` ("64% · local GPU", "Queued", "Paused · PC is busy") | |
| `transcript.changed` | `{ recordingId, version, reason: 'transcribed' \| 'edited' \| 'speakers' \| 'restored' \| 'topics' }` | The UI refetches `transcript.get` (or applies the edit it made). |
| `models.progress` | `{ modelId, percent, bytesDone, bytesTotal, state: 'downloading' \| 'verifying' \| 'done' \| 'failed', message: string \| null }` | |
| `status.footer` | adds `engine.detail: EngineStatusDetail` | Footer left side: "Local transcription ready · GPU (RTX 3060)" / "Transcription paused · PC is busy" / "No transcription model installed". |
| `recording.liveTranscript` | `{ sessionId, segments: { start: number, end: number, text: string }[] }` | Optional. Rough draft segments for the Recording session's Live transcript card; replaced entirely by the full pass. If live transcription is not available in a build, the host never sends it and the card says so. |

## Settings snapshot (M2 additions)

```ts
transcription: {
  auto: boolean;                         // transcribe automatically after recording (true)
  timing: 'after' | 'during';            // 'during' adds the live draft; the authoritative pass still runs after
  pauseWhenBusy: boolean;                // true
  modelId: string;                       // default: the recommended model that fits the hardware
  cpuFallbackModelId: string;            // default: small
  language: 'auto' | string;             // BCP-47 primary subtag
  keepWordTimestamps: boolean;           // true
  lowConfidenceThreshold: number;        // 0.5
}
speakers: { identify: boolean; expectedSpeakers: 'auto' | number; rememberRenamed: boolean; embeddingModelId: string }
history: { keepVersions: boolean; keepDays: number }     // true, 90
```

## Error codes (M2)

`transcript.none` (no transcript yet), `transcript.segmentNotFound`, `transcript.speakerNotFound`, `transcript.versionNotFound`, `models.notFound`, `models.inUse`, `models.downloadFailed` (detail: cause), `models.noSpace`, `engine.unavailable` (detail: what to install or where to turn it on).
