# Memento — Bridge contract

The host ↔ UI contract (ARCHITECTURE.md §3). Every method and event listed here exists as a C# record in `src/Memento.Core/Bridge/Contracts/` and a TypeScript type in `ui/src/bridge/types.ts`. JSON field names are camelCase. Times are ISO 8601 with offset unless the field ends in `Ms` (milliseconds, integer). Ids are opaque strings. Lists are never null; use `[]`.

Status: **M0, M1, M2 and M3 methods and events are implemented** (M3 in 0.4.0) in the host (`src/Memento.Core/Bridge/Methods/`) and in the UI's browser-preview mock (`ui/src/bridge/mock*.ts`). This document is what both follow. `ContractSerializationTests`, `M2ContractSerializationTests` and `M3ContractSerializationTests` pin the host's JSON, and `ContractDocumentationTests` checks that the host's error codes and stage names are exactly the ones listed here and in `types.ts`.

## Shared types

```ts
type RecordingType = 'meeting' | 'interview' | 'presentation' | 'lecture' | 'dictation' | 'research' | 'general' | string; // any other string is a custom type name
type StageName = 'stored' | 'transcript' | 'speakers' | 'topics' | 'minutes' | 'optimize'; // pipeline order
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
  whoSpoke: WhoSpoke;                       // after 1.2.0: the recording's own speaker count and names
}
interface WhoSpoke { count: number | null; /* 1–20, or null */ names: string[]; /* at most 20, 1–100 characters each */ }

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

M1 runs two stages; M2 adds `transcript`, `speakers` and `topics` between them (`minutes`, the documents stage, arrives with M4).

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

From M2:

| `stage` | `event`s | Written when |
|---|---|---|
| `transcript` | `started`, `completed`, `failed`, `info` | a pass starts (engine, model, device, tracks), ends (engine version, model, device, audio length and time, segments, words, low-confidence words, language) or fails; `info` for "Skipped <track>" (a silent track), "Speech without a transcript at 0:03–0:18" (a coverage gap, see `Transcript.coverageGaps`) and "Dropped N repeated lines" (the repeat filter, see Shared types (M2)) |
| `speakers` | `started`, `completed`, `failed`, `info` | identification starts ("Identifying speakers", "… (continuing where it stopped)" or "… (from the voices heard before)"; the detail names the models, tracks, "3 expected (this recording)" or "(Settings)" and "2 names given"), ends ("Found 2 speakers"; the detail says "5 voices heard, grouped into 2 speakers (2 expected, this recording)", "1 name kept from before and 2 names from Who spoke" and the talk-time shares) or fails; `info` for "No speakers to identify" |
| `topics` | `completed`, `failed` | local keyword topics were found, or could not be saved |
| `edited` | `info` | also "Transcript edited", "Speaker changed", "Speaker renamed", "Speakers merged", "Speaker restored", "Speaker removed" (Undo), "Speakers reduced" (`transcript.reduceSpeakers`: "2 speakers left, 3 speakers merged by voice, 41 lines moved"), "Speakers restored" (its Undo), "Transcript version restored" |

## Methods

| Method | Params | Result | Notes |
|---|---|---|---|
| `app.version` | `{}` | `{ version, osVersion, isDarkTheme }` | M0 |
| `app.openExternal` | `{ url }` | `{ opened }` | M0. https: or ms-settings: only |
| `ui.ready` | `{}` | `{}` | M0 |
| `settings.get` | `{}` | `SettingsSnapshot` | M0; M1 extends the snapshot (below) |
| `settings.set` | `Partial<SettingsSnapshot>` (nulls keep) | `SettingsSnapshot` | M0; M1 adds fields. Top-level fields merge; the `recording` block is **replaced whole** when present (the UI always sends the full block). The M2 blocks `transcription`, `speakers` and `history` and the M3 blocks `general`, `export`, `ai` and `storage` merge **field by field**: a field that is missing or null keeps its value (M3 exceptions: see Settings snapshot (M3 additions)). |
| `library.list` | `{ query?: string, type?: RecordingType \| 'all', sort?: 'newest' \| 'oldest' \| 'longest' \| 'title' \| /* M3: */ 'size' }` | `{ recordings: RecordingSummary[], totalDurationMs, totalCount }` | M1 adds params. `query` searches titles, people and (M2) transcripts. Result reflects the filter. |
| `library.processing` | `{}` | `{ current: { recordingId, title, meta: RecordingSummary, stages: StageStatus[] } \| null, othersCount }` | M1. The processing card: the newest recording with a stage active or queued. `stages` lists every stage, finished `stored` and `optimize` included; `meta.stages` follows the row rule (see Stages). |
| `project.get` | `{ recordingId }` | `Project` | M1 |
| `project.updateDetails` | `{ recordingId, details: Partial<RecordingDetails> }` | `Project` | M1. Works during recording too. After 1.2.0 `whoSpoke` replaces whole: `count` null or a whole number 1–20, `names` at most 20 of 1–100 characters (trimmed; a repeat, ignoring case, is dropped); anything else answers `bridge.invalidParams` and nothing changes. Before recording starts the UI keeps it and sends it with the other pre-start details. |
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
| `recording.current` | `{}` | `{ session: RecordingStatePayload \| null }` | M1. Lets the UI rejoin an active session after a reload. A session counts while it records, is paused or finalizes; once its `ready` or `stopped` state has been sent the answer is `null`. An answer that arrives after a `recording.state` event for the session is older than that event and is ignored. |
| `recovery.list` | `{}` | `{ items: { recordingId, title, startedAt, tracksIntact, tracksTotal, lastCheckpointAt, recoveredDurationMs, mayBeMissingMs }[] }` | M1. Projects repaired at launch. |
| `recovery.acknowledge` | `{ recordingId }` | `{}` | M1. Dismisses the dialog for this project. |
| `dialog.pickFolder` | `{ title, initialPath?: string }` | `{ path: string \| null }` | M1 |
| `status.get` | `{}` | `FooterStatusPayload` | M1 |

`annotations.*` in detail: each method answers the recording's whole list after the change, chapters and highlights sorted by `atMs`, topics in the order they were added. On an add the host assigns the id (`c…`, `h…`, `t…`, opaque); an `id` sent with an add is ignored. An update or remove naming an id the recording does not have answers `annotations.notFound` (`detail` is that id) and changes nothing; an unknown `recordingId` answers `project.notFound`; a missing `atMs`, a blank topic label, an update without an id or an `origin` other than `user`, `local`, `ai` answers `bridge.invalidParams`. All of them work during recording (`recording.markHighlight` adds through the session; the note is then saved with `annotations.updateHighlight`).

## Events

| Event | Payload | Notes |
|---|---|---|
| `theme.changed` | `{ isDark }` | M0 |
| `status.footer` | `{ engine: { ready, device, /* M2: */ detail: EngineStatusDetail }, storage: { freeBytes, lowSpace }, /* M1: */ recording: { active: boolean, lastCheckpointAt: string \| null, lostSource: string \| null }, processingPaused: string \| null /* reason */ }` | M0, extended in M1 and M2. `processingPaused` is the reason in words, shown after "Transcription paused · ": exactly one of `"Low disk space"` (free space on the library drive is below the low-space threshold, `FooterStatusService.LowSpaceReason`), `"PC is busy"` (a recording is running or the processor is busy, while "Pause when busy" is on) or `"Paused by you"` (`processing.pause`); otherwise `null`. M1 sent only the first. |
| `recording.state` | `RecordingStatePayload = { sessionId, recordingId, state: 'recording' \| 'paused' \| 'finalizing' \| 'ready' \| 'stopped', startedAt, elapsedMs /* recorded time, excluding paused time */, tracks: Track[], lastCheckpointAt, highlightsCount }` | M1, on every change and at least every second while recording |
| `recording.levels` | `{ sessionId, levels: { sourceId: string, rms: number /* 0..1 */, peak: number }[] }` | M1, ≤ 30 per second |
| `recording.sourceLost` | `{ sessionId, sourceId, name, atMs, remaining: string[] }` | M1 |
| `recording.stoppedByHost` | `{ sessionId, recordingId, reason: 'diskFull' \| 'deviceLost' \| 'error', atMs, message }` | M1 |
| `library.changed` | `{ recordingIds: string[] }` | M1, after any project write; the UI refetches |
| `processing.progress` | `{ recordingId, stages: StageStatus[] }` | M1. Every stage, finished ones included: `stored`, and `optimize` when the storage format is AAC or MP3; `transcript`/`speakers`/`topics` from M2. |
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

The UI's bridge client adds two codes of its own, never sent by the host: `bridge.timeout` (no answer in time: 10 s, or 30 min for the calls that can open a Windows picker and so wait for the person: `dialog.pickFolder`, `agenda.importFile`, `attachments.add` and `library.importMedia`) and `bridge.sendFailed` (the request could not be posted).

---

# M2 — Transcription and speakers (implemented in 0.3.0)

Everything below is additive. Host and UI build to it from the same text; the UI's mock implements it with sample data. The Clarifications at the end were decided after both halves landed; the sections above already say what they decided.

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
  coverageGaps: { start: number; end: number; track: string | null }[];          // seconds; speech without a transcript, [] when none
}
type TranscriptStatus = 'none' | 'queued' | 'running' | 'done' | 'failed' | 'paused';
interface StageFailure { stage: StageName; message: string; kept: string; remedies: { id: string; label: string }[] }   // DESIGN §17 copy: what failed, what was kept, most specific fix first
interface ModelInfo {
  id: string; engine: 'transcription' | 'speakers' | 'ocr'; name: string; description: string;
  sizeBytes: number; license: string; installed: boolean; installing: { percent: number; bytesDone: number } | null;
  recommended: boolean; runsOn: 'gpu' | 'cpu' | 'either'; minVramBytes: number | null; accuracyNote: string;   // "Most accurate", "Fast on CPU"
  role: 'segmentation' | 'embedding' | null;     // speaker models: segmentation is always needed, the embedding (voice) model is the Settings choice
}
interface EngineStatusDetail { ready: boolean; device: string | null; gpuName: string | null; freeVramBytes: number | null; model: string | null; paused: string | null;
                               gpuMemory: GpuMemoryInfo | null; note: string | null }   // added after 1.1.0, see "Graphics card memory"; both null in status.footer
// model: the catalog id the engine would use, named even while it is not installed (ready is then false).
```

`StageName` gains `transcript`, `speakers` and `topics` as running stages. `HistoryEntry.stage` gains `transcript`, `speakers`, `topics`; `detail` carries engine, model, device, duration and segment count.

- `coverageGaps`: stretches of at least **10 s** where a track has speech energy but no segment of that track has words (Whisper sometimes drops a passage, ENGINE-NOTES.md §D). Each one also gets a History `info` line. Review shows a notice at the gap's position, "Nothing was transcribed between 0:03 and 0:18, although there was speech.", with a "Transcribe again with {other model}" action (`transcript.retranscribe` with Settings' CPU-fallback model, or another installed model when the transcript already came from that one).
- Repeated lines: a segment whose text is the same as the previous segment's on the same track (ignoring case, spacing and end punctuation) three or more times in a row is a known whisper.cpp failure. The host keeps the first and drops the repeats, and writes a History `info` line "Dropped N repeated lines" with the time and the repeated text.
- `speakerConfidence`: sherpa-onnx's turn score is a similarity, not a probability. The host maps it linearly (a score of 0.2 or less → 0, 0.6 or more → 1; a track with a single cluster, score −2, → 1) and multiplies it by the winning speaker's share of the speech the segment overlaps. The UI marks a speaker as uncertain below 0.7.
- `engine.model` is the catalog id (`whisper-large-v3-turbo`); `engine.device` is `"GPU (Vulkan)"` or `"CPU"`.

## Methods (M2)

| Method | Params | Result | Notes |
|---|---|---|---|
| `transcript.get` | `{ recordingId }` | `{ transcript: Transcript \| null, status: TranscriptStatus, failure: StageFailure \| null }` | `transcript` is null until the first pass completes; a failed pass may still return a partial transcript with `status: 'failed'`. `status` follows the `transcript` stage; `failure` is the transcript stage's failure, or else the `speakers` stage's (so a failed speaker pass shows its remedies while `status` is `done`). |
| `transcript.editSegment` | `{ recordingId, segmentId, text }` | `{ segment: TranscriptSegment, version }` | Keeps `edited.original` from the first edit. Words are re-aligned proportionally (confidence set to 1 for edited words). An edit back to the original wording (Undo) sets `edited` to `null` again. |
| `transcript.setSegmentSpeaker` | `{ recordingId, segmentId, speakerId: string \| null, newSpeakerName?: string }` | `{ segment, speakers }` | `newSpeakerName` creates a speaker and assigns it. |
| `transcript.renameSpeaker` | `{ recordingId, speakerId, name }` | `{ speakers }` | Updates every segment by reference; `renamed: true`. |
| `transcript.mergeSpeakers` | `{ recordingId, fromSpeakerId, intoSpeakerId }` | `{ speakers, segmentsChanged }` | |
| `transcript.restoreSpeaker` | `{ recordingId, speaker: { id, name, color, renamed }, segmentIds: string[] }` | `{ speakers, segmentsChanged }` | Added for Undo (after 1.1.0). Puts a speaker back as it was: added after the others when the transcript has no speaker with that `id`, otherwise its name, colour and renamed flag are set; then every listed line is assigned to it (`speakerConfidence` 1). The inverse of a merge (the merged speaker with its lines), a rename (`segmentIds: []`) and `transcript.removeSpeaker`. `id` is 1–40 letters, digits, `-` or `_`; `color` 1–4; `name` 1–100 characters, else `bridge.invalidParams`. A line the transcript does not have answers `transcript.segmentNotFound` and nothing changes. History: "Speaker restored". |
| `transcript.removeSpeaker` | `{ recordingId, speakerId }` | `{ speakers }` | Added for Undo (after 1.1.0): the inverse of a speaker added with `newSpeakerName`. Removes a speaker no line is assigned to; while lines still are, `transcript.speakerInUse` (`detail`: the id) and nothing changes. History: "Speaker removed". |
| `transcript.reduceSpeakers` | `{ recordingId, count }` | `{ speakers, merged: SpeakerMerge[], segmentsChanged, basis: 'voices' \| 'talkTime' }` | After 1.2.0 (Review's "Reduce to {n} speakers"). Merges speakers, the two whose voices sound most alike first, until `count` (1–20, else `bridge.invalidParams`) are left, in one write. A speaker's voice is the speech-weighted mix of the voices (voices.json) of the lines it has now; a speaker none of whose lines has a voice goes first, least talk time first, into the speaker with the most; without voices.json every merge is least talk time first (`basis: 'talkTime'`). Two speakers the user named (`renamed`) are never merged with each other, so fewer merges than asked are possible; a named speaker keeps its name (an unnamed one goes into it), otherwise the one with less talk time goes into the one with more. No id, name or colour changes. `SpeakerMerge` is `{ speaker: SpeakerRestore, intoSpeakerId, segmentIds }` in the order made, each with the lines the speaker had at that moment. Nothing to merge writes nothing. History: "Speakers reduced". |
| `transcript.restoreSpeakers` | `{ recordingId, speakers: { speaker: SpeakerRestore, segmentIds: string[] }[] }` | `{ speakers, segmentsChanged }` | After 1.2.0: the Undo of a reduce (its merges last first). `transcript.restoreSpeaker` for each entry in order, in one write; 1–500 entries, each checked as there (`bridge.invalidParams`), and a line the transcript does not have answers `transcript.segmentNotFound` with nothing changed. History: "Speakers restored". |
| `transcript.markReviewed` | `{ recordingId, reviewed }` | `{ reviewed }` | |
| `transcript.search` | `{ recordingId, query }` | `{ matches: { segmentId, start, snippet }[] }` | Case-insensitive, word-boundary aware. |
| `transcript.retranscribe` | `{ recordingId, modelId?: string, language?: string }` | `{}` | Queues a new pass, then `speakers` (when on) and `topics`; when version history is on the current transcript is kept as a version. Refused with `project.recording` while recording, `models.notFound` for a model the catalog does not have. |
| `transcript.versions` | `{ recordingId }` | `{ versions: { id, at, reason: 'transcribed' \| 'edited' \| 'restored' \| 'retranscribed', engine: string \| null, segments: number }[] }` | Empty when history is off. Newest first. `engine` names the engine and model, e.g. `"whisper.cpp whisper-small"`. |
| `transcript.restoreVersion` | `{ recordingId, versionId }` | `{ transcript }` | The replaced transcript becomes a version. |
| `processing.retry` | `{ recordingId, stage, remedyId?: string }` | `{}` | `remedyId` from `StageFailure.remedies`: `retry` (or none: run again as before), `cpu` (run on the processor), `model:<catalog id>` (e.g. `model:whisper-small`) or `install:<catalog id>` (download a model again whose installed file failed its SHA-256 check before use; the stage keeps waiting with cause `noModel` and runs once the model is installed). Retrying `transcript` also queues `speakers` (when on) and `topics`; a pass that stopped continues from its partial results. Retrying a finished `speakers` stage identifies the speakers again (Review's "Identify speakers again") with the recording's Who spoke, else Settings; when voices.json holds every track as the diarizer heard it with the same models and threshold, it regroups at once without listening again. Names a person gave carry over to the speaker that took most of their lines, and the lines as corrected are kept as a transcript version. An unknown remedy answers `bridge.invalidParams`, an unknown model `models.notFound`. |
| `processing.cancel` | `{ recordingId, stage }` | `{}` | Partial results are kept. The stage ends `failed` (label "Cancelled") with one remedy, `retry`, which continues from the partial results. |
| `processing.pause` / `processing.resume` | `{}` | `{}` | Global; shown in the footer as "Transcription paused". |
| `models.list` | `{}` | `{ models: ModelInfo[] }` | Catalog plus installed state; re-reads disk. |
| `models.install` | `{ modelId }` | `{}` | Downloads with SHA-256 verification; progress via `models.progress`. One download at a time. |
| `models.cancelInstall` | `{ modelId }` | `{}` | Removes the partial file. The download's last `models.progress` is `state: 'failed'` with a message saying it was cancelled; it is sent before this call answers. |
| `models.remove` | `{ modelId }` | `{}` | Refused with `models.inUse` while a stage is using it. |
| `engine.status` | `{}` | `{ transcription: EngineStatusDetail, speakers: EngineStatusDetail }` | Probe result; `freeVramBytes` null on CPU-only. |
| `engine.refresh` | `{}` | as `engine.status` | Added after 1.1.0: Settings' "Check again". Reads the card again with nothing cached (who holds its memory is otherwise reused for 4 s) and updates the footer when where transcription runs has changed. Any parameter is `bridge.invalidParams`. |
| `library.list` | (as M1) | `RecordingSummary` | `query` now also matches transcript text (FTS); the summary gains `matchSnippet: string \| null`: the words around the first transcript hit, and `null` when only the title or people matched (or there is no query). |

## Events (M2)

| Event | Payload | Notes |
|---|---|---|
| `processing.progress` | (as M1) stages now include `transcript` and `speakers` with `percent` and `label` ("64% · local GPU", "Queued", "Paused · PC is busy") | |
| `transcript.changed` | `{ recordingId, version, reason: 'transcribed' \| 'edited' \| 'speakers' \| 'restored' \| 'topics' }` | The UI refetches `transcript.get` (or applies the edit it made). |
| `models.progress` | `{ modelId, percent, bytesDone, bytesTotal, state: 'downloading' \| 'verifying' \| 'done' \| 'failed', message: string \| null }` | `message` says why a download failed, including "The download of … was cancelled; the partial file was removed." after `models.cancelInstall`. |
| `status.footer` | `engine` is `{ ready, device, detail: EngineStatusDetail }` | Footer left side: "Local transcription ready · GPU (RTX 3060)" / "Transcription paused · PC is busy" / "No transcription model installed". `processingPaused` is one of "Low disk space", "PC is busy", "Paused by you", or null. |
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
speakers: { identify: boolean; expectedSpeakers: 'auto' | number /* 1–20 */; rememberRenamed: boolean; embeddingModelId: string }
history: { keepVersions: boolean; keepDays: number }     // true, 90
```

`settings.set` merges each of these blocks field by field: send only the fields that change (sending the whole block works too). `expectedSpeakers` is the string `"auto"` or a whole number from 1 to 20; anything else answers `settings.invalidValue`. It is the speaker count for recordings without their own (`RecordingDetails.whoSpoke`, after 1.2.0): the voices the diarizer found on all tracks are grouped until that many are left (never split), for any number of tracks.

## Error codes (M2)

`transcript.none` (no transcript yet), `transcript.segmentNotFound`, `transcript.speakerNotFound`, `transcript.versionNotFound`, `transcript.speakerInUse` (removing a speaker while lines are assigned to it; detail: its id), `models.notFound`, `models.inUse`, `models.downloadFailed` (detail: cause), `models.busy` (detail: the model downloading now), `models.noSpace`, `engine.unavailable` (detail: what to install or where to turn it on).

## Clarifications (M2, decided after the UI landed)

1. `StageName` gains `topics` (a short local stage after `speakers`; rows omit it when done, like `stored`).
2. `matchSnippet` and `transcript.search` snippets are plain text; the UI emphasises the query words itself.
3. A transcript version is kept (when history is on) on the first edit after a pass or a restore, on retranscribe, and on restore; `reason` says how the saved copy came about.
4. A stage waiting for a busy PC stays `active`, keeps its `percent`, and its label reads `Paused · <reason>`; `processing.resume` releases the busy pause for the current stage until the next busy detection.
5. Once a transcript exists, every highlight carries a non-null `segmentId` (the host attaches highlights to the segment at their time when a transcript is produced).
6. `models.install` while another download is running answers `models.busy` (detail: the running model id).
7. `settings.set` with a `modelId` / `cpuFallbackModelId` / `embeddingModelId` that is not installed answers `settings.invalidValue` naming the field.
8. Catalog model ids are fixed strings both sides use: `whisper-large-v3-turbo`, `whisper-medium`, `whisper-small`, `whisper-base`, `pyannote-segmentation-3-0`, `nemo-titanet-small`, `tesseract-eng`, and an optional eighth, `3dspeaker-eres2net-base` (an alternative voice model).

Decided at the M2 integration (0.3.0), from the host's proposals:

9. `Transcript.coverageGaps: { start, end, track }[]`, flagged from 10 s of uncovered speech, shown in Review and listed in History.
10. `RecordingSummary.matchSnippet` is `null` when only the title or people matched.
11. `status.footer.engine` is `{ ready, device, detail }`; `processingPaused` is one of "Low disk space", "PC is busy", "Paused by you", or null.
12. `settings.set` merges the M2 blocks field by field; only `recording` is replaced whole. `expectedSpeakers` is "auto" or 1–20.
13. Remedy ids are `retry`, `cpu`, `model:<id>` and `install:<id>`. Every installed model is checked against its catalog SHA-256 before a stage uses it (hashed once, then trusted by its `<file>.verified.json` stamp while its size and last write time are unchanged); a file that does not match is moved aside to `<file>.corrupt-<time>`, reads as not installed, `models.progress` reports it `failed` with the reason, and the stage fails with cause `noModel` and the remedy `install:<id>` first. `processing.cancel` leaves the stage `failed` with the remedy `retry`. Retrying the transcript also queues `speakers` and `topics`.
14. A stage whose model is not installed is `failed` with the label "Waiting for a model" and a failure naming the model; it is queued again by itself as soon as a model is installed.
15. `transcript.get.failure` falls back to the `speakers` stage's failure.
16. `speakerConfidence` is calibrated as described under Shared types (M2).
17. `models.progress` reports a cancelled download as `failed` with a message.
18. A kept version's `engine` reads like "whisper.cpp whisper-small".
19. A heavy stage stopped by a busy PC (or closing Memento) resumes where it stopped: `transcript` from the last finished window of each track (`transcript.partial.json`), `speakers` from the last finished track (`speakers.partial.json`). One worker job that uses the graphics card runs at a time, and worker processes end when Memento ends.
20. The repeat filter (Shared types (M2)) drops runs of three or more identical lines and says so in History.
21. `ModelInfo.role` tells the speech-segmentation model (needed, not a choice) from the voice models Settings › Speakers chooses between; `null` for transcription and OCR models.

## Speakers: Who spoke, grouping and Reduce (after 1.2.0)

1. **Who spoke.** `RecordingDetails.whoSpoke` is the recording's own expectation, set on the Record screen's details or Review's People pane: `count` (1–20) and/or `names`. The speakers stage groups to `count`, else to as many as `names`, else to Settings' `expectedSpeakers`, else by sound alone (Auto). `project.json` schema v3 stores it in `details.whoSpoke`; the v2 → v3 step writes `{ "count": null, "names": [] }` and keeps what is readable of a damaged value.
2. **Grouping.** The diarizer always clusters each track by threshold (`DiarizeJob.numClusters` is −1), and the host groups the voices it found across all tracks: voices with little speech (under 30 s, or 5 % of all speech when that is less) are set aside, the main voices are joined, most alike first (cosine of the speech-weighted embeddings), until the count is left, and then each little voice joins the main voice it sounds most like. Voices are only ever joined, never split, so fewer voices than the count stay as they are. Without a count, main voices on one track that sound alike (see ENGINE-NOTES.md §L) are joined, little voices join the main voice on their track they sound like (one with under 10 s of speech always joins the most alike), and voices of different tracks stay different people.
3. **Names.** After grouping, a name a person gave in the transcript goes to the new speaker that took most of that speaker's lines (by time, same track; at least a quarter of either's talk time), one to one; then `whoSpoke.names` not used yet go to the remaining speakers in order of first appearance. Both count as given by the user (`renamed: true`).
4. **voices.json** (schema v1, in the project folder, never exported): the signature (models and threshold), what the diarizer heard per track (turns and voice embeddings) and the voices that won lines, each with its embedding and those lines. Identifying speakers again with the same signature regroups from it at once. `transcript.reduceSpeakers` mixes a speaker's voice from the voices of the lines it has now.
5. A speakers pass over a transcript whose last change was an edit keeps that transcript as a version (reason `edited`) before replacing its speakers.
6. **Undo.** Review registers a reduce as one step: Undo calls `transcript.restoreSpeakers` with the merges last first, Redo calls `transcript.reduceSpeakers` again. "Merge {old} into {new}" from a line's speaker menu is `transcript.mergeSpeakers`, undone with `transcript.restoreSpeaker` like the People list's merge.

---

# M3 — Agenda import, attachments, media import, export, remaining Settings (implemented in 0.4.0)

Additive. The parsing library in `src/Memento.Documents/Agenda` (`AgendaImporter`) serves these methods. The Clarifications at the end were decided after both halves landed (1–8) and at the 0.4.0 integration (9–21); where they differ from the tables above, they win.

## Shared types (M3)

```ts
type AgendaSourceKind = 'text' | 'pastedText' | 'markdown' | 'csv' | 'tsv' | 'docx' | 'xlsx' | 'pdf' | 'image';
interface AgendaParsedItem { text: string; uncertain: boolean; uncertainReason: string | null; level: number; location: string | null }   // location like "page 2, line 14"
interface AgendaParsePreview {
  source: string;                       // file name, or "Pasted text"
  sourceKind: AgendaSourceKind; title: string | null;
  items: AgendaParsedItem[]; warnings: { code: string; message: string }[];
  ocrEngine: string | null;             // "Windows OCR" when an image was read
  attachmentToken: string | null;       // handle the host keeps for the original file until `agenda.apply` or discard
}
interface Attachment { id: string; name: string; sizeBytes: number; addedAt: string; kind: 'agenda' | 'file'; contentType: string | null }
type AudioExportFormat = 'flac' | 'wav' | 'mp3';
type TranscriptExportFormat = 'json' | 'markdown' | 'text' | 'srt';
interface ExportSelection {
  audioMixed: { on: boolean; format: AudioExportFormat; bitrateKbps: number | null };
  tracks:     { on: boolean; format: AudioExportFormat; bitrateKbps: number | null };
  transcript: { on: boolean; formats: TranscriptExportFormat[]; options?: TranscriptTextOptions | null };   // options: after 1.2.0, see "Clipboard and transcript text options"
  documents:  { on: boolean; documentIds: string[]; format: 'docx' | 'pdf' | 'markdown' };   // M4 fills this; M3 exports nothing here and the row is disabled with "Documents arrive in a later version"
  details:    { on: boolean };
  attachments:{ on: boolean };
}
type ExportComponent = keyof ExportSelection;                                    // 'audioMixed' | 'tracks' | 'transcript' | ...
interface ExportEstimate {
  files: number; bytes: number;                                                  // every item, plus manifest.json
  items: { component: ExportComponent; name: string; bytes: number }[];          // name as written, e.g. "Attachments/agenda.docx"
  unavailable: { component: ExportComponent; reason: string }[];                 // a ticked row with nothing to write: "Not transcribed yet", "No attachments"
}
interface ExportDestination { folder: string; createSubfolder: boolean }                   // subfolder named after the recording (sanitised title + date)
interface LibraryUsage { totalBytes: number; freeBytes: number; count: number; largest: { recordingId: string; title: string; sizeBytes: number } | null }
```

`RecordingDetails.agenda.source` carries the `AgendaParsePreview.source` and `parsedLocally` stays true; `RecordingSummary.type` may change through `project.changeType`.

## Methods (M3)

| Method | Params | Result | Notes |
|---|---|---|---|
| `agenda.importFile` | `{ recordingId: string \| null, path?: string }` | `{ preview: AgendaParsePreview \| null, cancelled: boolean }` | The host shows its file picker; a `path` from the page is refused with `bridge.invalidParams` (security audit 2026-10-07) (filters: Word, PDF, Excel, CSV, Markdown, text, images). Parsing happens on this PC. Errors use the `agenda.*` codes below. |
| `agenda.importDropped` | `{ recordingId: string \| null, paths: string[] }` | as above | For a drop onto the drop zone. The page only sees file names, so the UI sends this request with `chrome.webview.postMessageWithAdditionalObjects(request, files)` (the bridge client's `callWithFiles`), `paths` holding the `File` objects' names. The host reads the real paths of the attached files (`CoreWebView2File.Path`) from the same message before the request runs, keeps the last drop for 2 minutes, and matches each name first as a full path, then by file name. If nothing matches it answers `agenda.dropUnavailable` and the UI opens the file picker instead. The browser-preview mock treats the names as the paths. |
| `agenda.parseText` | `{ recordingId: string \| null, text }` | `{ preview }` | Pasted text. |
| `agenda.apply` | `{ recordingId, items: { text, uncertain, uncertainReason }[], source, sourceKind, attachmentToken: string \| null }` | `Project` | Stores the agenda (ids assigned, `covered: false`), copies the original file into `attachments/` as kind `agenda` when a token is given, appends History. Items longer than 200 characters are refused with `agenda.itemTooLong` (`detail`: the item's position, from 1); more than 200 items (counted after blank ones are dropped) with `agenda.tooManyItems` (`detail`: the count). An empty `items` list clears the agenda. |
| `agenda.discard` | `{ attachmentToken }` | `{}` | Drops a pending original file. |
| `agenda.setCovered` | `{ recordingId, itemId, covered }` | `{ agenda }` | Works during recording. |
| `attachments.list` | `{ recordingId }` | `{ attachments: Attachment[] }` | |
| `attachments.add` | `{ recordingId, path?: string }` | `{ attachment: Attachment \| null, cancelled }` | The host's picker; a `path` from the page is refused with `bridge.invalidParams`. 100 MB limit per file (`attachments.tooLarge`). |
| `attachments.remove` | `{ recordingId, attachmentId }` | `{}` | Confirmation is the UI's job. |
| `attachments.open` | `{ recordingId, attachmentId }` | `{}` | Opens with the Windows default app. |
| `library.importMedia` | `{ path?: string, title?: string, type?: RecordingType }` | `{ recordingId: string \| null, cancelled }` | Imports an existing audio file (wav, flac, mp3, m4a, wma, ogg/opus where Media Foundation can decode them; video containers are accepted for their audio track only in 1.0, with a warning) as a project with one track `imported`, then runs the normal stages. Progress through `processing.progress` (`stored`). `library.importUnsupported` when it cannot be decoded. The file comes from the host's picker; a `path` from the page is refused with `bridge.invalidParams`. |
| `project.changeType` | `{ recordingId, type }` | `Project` | Custom types are any non-empty string ≤ 40 chars. |
| `export.estimate` | `{ recordingId, selection }` | `ExportEstimate` | Fast; sizes for lossy formats are estimates. |
| `export.run` | `{ recordingId, selection, destination, remember: boolean }` | `{ jobId }` | Writes in the background; `remember` stores selection and destination as the Settings › Export defaults. Writes a `manifest.json` beside the files (shape: M3 integration clarification 17); file names follow clarification 16. Jobs run one at a time (clarification 19). Never modifies the project. Refused with `export.destinationUnwritable` (detail: why) before anything is written. |
| `export.cancel` | `{ jobId }` | `{}` | Partial files are removed. |
| `export.openFolder` | `{ jobId }` | `{}` | Opens the destination in Explorer. |
| `library.usage` | `{}` | `LibraryUsage` | |
| `library.rebuildIndex` | `{}` | `{ recordings: number }` | |
| `library.move` | `{ newPath }` | `{ jobId }` | Copy, verify hashes, switch, then delete the old folder; refused while recording or processing (`library.busy`). Progress via `library.moveProgress`. |
| `storage.reclaim` | `{ recordingIds: string[] \| null, downmixMono: boolean, codec: 'aac' \| 'mp3', bitrateKbps?: number \| null }` | `{ jobId }` | Runs the optimize stage on the given (or all older than `settings.storage.reclaimOlderThanDays`) recordings. Transcripts are never touched. |
| `ai.setKey` / `ai.clearKey` | `{ provider: 'anthropic' \| 'openai', key }` / `{ provider }` | `{ hasKey: boolean }` | DPAPI; the key is never returned. M3 stores keys so Settings is complete; M4 uses them. |
| `app.setStartup` | `{ startWithWindows: boolean }` | `{ startWithWindows }` | Registry `Run` key for the current user. |

## Events (M3)

| Event | Payload |
|---|---|
| `export.progress` | `{ jobId, recordingId, percent, currentFile: string \| null, state: 'running' \| 'done' \| 'failed' \| 'cancelled', message: string \| null, outputFolder: string \| null, files: number, bytes: number }` |
| `library.moveProgress` | `{ jobId, percent, state: 'running' \| 'done' \| 'failed', message: string \| null, newPath }` |
| `storage.reclaimProgress` | `{ jobId, percent, state: 'running' \| 'done' \| 'failed', message: string \| null, recordingsDone, bytesFreed }` (a reclaim cut short by closing Memento ends `failed`) |
| `status.footer` | adds `export?: { active: boolean, percent: number \| null, title: string \| null }` (the footer shows "Exporting {title} · 42%"). The M3 host always sends it (`active: false` when idle); the UI accepts it missing from older hosts. |

## Settings snapshot (M3 additions)

```ts
general:  { startWithWindows: boolean; keepRunningInTray: boolean; language: 'en' }   // tray: stored; applied in M5
export:   { saveCopiesOutside: boolean; defaultFolder: string | null; askWhereEachTime: boolean; createSubfolder: boolean; defaults: ExportSelection }
ai:       { enabled: boolean; askBeforeSend: boolean; keepRecord: boolean;                 // false, true, true
            share: { transcript: boolean; details: boolean; participants: boolean; agenda: boolean; highlights: boolean; attachments: boolean };   // all true but attachments
            providers: { anthropic: { hasKey: boolean }; openai: { hasKey: boolean } } }   // read side only; never the keys
storage:  { reclaimOlderThanDays: number | null }
```

`settings.set` merges each M3 block field by field (send only what changes, or the whole block); `export.defaults` is replaced whole when present, and `ai.providers` is never accepted. A missing field keeps its value. A JSON `null` keeps its value too, except for the two fields whose value can be empty: `export.defaultFolder: null` clears the default folder and `storage.reclaimOlderThanDays: null` clears the age ("Never").

## Error codes (M3)

`agenda.fileTooLarge`, `agenda.imageTooLarge`, `agenda.unsupportedFormat`, `agenda.unreadable`, `agenda.protected`, `agenda.noText`, `agenda.noItems`, `agenda.ocrUnavailable` (detail: how to install the OCR language in Windows Settings), `agenda.itemTooLong`, `agenda.tooManyItems`, `agenda.itemNotFound` (an unknown agenda item), `agenda.dropUnavailable`, `attachments.tooLarge`, `attachments.notFound`, `library.importUnsupported`, `library.busy`, `export.destinationUnwritable`, `export.nothingSelected`, `export.notFound` (job), `library.moveRefused` (the target folder was checked and refused before anything was copied; detail: the folder), `storage.nothingToReclaim` (no recording chosen, or none older than the Settings age), `app.startupRefused` (Windows refused to change the startup entry; nothing was changed), `ai.keyWriteFailed` (Windows could not store or remove the key; nothing was changed).

## Design references

Export dialog: DESIGN §15 and `ExportDialog.dc.html`. Details sheet and agenda import: §14 and `AgendaImport.dc.html` (parsed state, uncertain items with the `accent-soft` notice and reasons, Replace, the AI fallback card disabled with the Settings pointer until M4). Settings: §11 for General, Export, Storage and history, AI and privacy (keys masked, Replace/Add), Documents (defaults rows disabled with "Available in a later version"; History rows live). Library first-run "Import audio or video" becomes live. Review › Details tab: Agenda with its source caption and a Replace link; Attachments list with open/remove.

## Clarifications (M3, decided after the UI landed)

1. `ExportEstimate.items[]` gains `component` (a key of `ExportSelection`); `unavailable` is `{ component, reason }[]`. The UI estimates with every row ticked once and sums the ticked rows itself, so `export.estimate` must be cheap and must return every component.
2. `agenda.importFile`, `agenda.importDropped` and `agenda.parseText` accept `recordingId: null` before a recording exists; the UI keeps the preview and calls `agenda.apply` with the token once `recording.start` returns an id. Pending tokens live at least 30 minutes.
3. `library.list` accepts `sort: 'size'` (largest first).
4. Export folder naming is shared: characters Windows forbids become " - ", runs of spaces and dashes collapse, the title is capped at 80 characters, then a space and the first 10 characters of `createdAt`; e.g. `Design review - library screen 2026-10-05`. Host and UI must produce the same string.
5. `status.footer.export` may be absent (older hosts); `settings.set` takes `ai` without `providers`; keys change only through `ai.setKey` / `ai.clearKey`.
6. `AudioSource['kind']` and `Track.sourceKind` gain `'imported'` for media-import tracks (`sourceId: 'imported'`).
7. `agenda.setCovered` with an unknown item answers `agenda.itemNotFound`.
8. `library.importMedia` on a video container records the "audio only" warning as a History `info` entry; no extra field.

Decided at the M3 integration (0.4.0), from the host's notes:

9. Drag and drop: the UI sends `agenda.importDropped` with `chrome.webview.postMessageWithAdditionalObjects(request, files)` (see the method's row). Without the files attached the host cannot know the paths and answers `agenda.dropUnavailable`.
10. `agenda.apply` with an expired or unknown `attachmentToken` still stores the items; History says the original file was not attached. Tokens live one hour.
11. `settings.set` merges the M3 blocks field by field; `export.defaultFolder: null` and `storage.reclaimOlderThanDays: null` clear the value (Settings snapshot (M3 additions)).
12. Refusals that used `bridge.invalidParams` or `bridge.internal` have their own codes: `library.moveRefused` (`library.move` checked the target and copied nothing), `storage.nothingToReclaim` (`storage.reclaim` with `recordingIds: []`, with `null` and no age set, or with nothing older than the age; no job starts), `app.startupRefused` (`app.setStartup`, or `settings.set` with `general.startWithWindows`, when Windows refuses the `Run` entry) and `ai.keyWriteFailed` (`ai.setKey` / `ai.clearKey` when DPAPI or the file write fails). `ai.setKey` keys are 8 to 500 characters with no spaces, otherwise `bridge.invalidParams`; no message ever contains the key.
13. `library.move` is refused with `library.busy` while a recording is in progress or starting, and `recording.start` is refused with `library.busy` (detail `move`) while the library is being copied. A move is also refused during an import, an export, a reclaim or another move; `export.run`, `library.importMedia` and `storage.reclaim` are refused with `library.busy` during a move.
14. `project.json` schema v2 keeps the attachments index as a typed `attachments` field (extension data in v1); the v1 → v2 step keeps readable entries and drops damaged ones. `attachments.open` opens the file with its Windows default app; for a program or script it opens the containing folder instead of running it.
15. `library.importMedia` that fails while decoding or storing removes the new project (the original file is never changed). An import cut short by a crash or power cut is found at the next launch, still `finalizing`: the host marks it `failed`, with its `stored` stage `failed` and labelled "Import interrupted", a failure carrying the remedy `importAgain`, the half-imported copy removed and a History `failed` line. The Library row reads "Import interrupted · Import again"; `processing.retry { stage: 'stored', remedyId: 'importAgain' }` imports the same file into the same project (`bridge.invalidParams` naming the file when it is no longer there). Delete removes it as usual. Imports keep their source in `project.json` (`importedFrom: { path, name }`).
16. Exported file names, with `base` the folder name of clarification 4: the mix `{base}.{ext}`, each track `{base} - {track name}.{ext}` (the name made safe for Windows: forbidden and control characters become spaces, at most 80 characters, never a device name such as `CON`; the track id when nothing is left), the transcript `{base} - transcript.json` / `.md` / `.txt` and `{base}.srt`, the details `{base} - details.json`, and attachments under `Attachments/` with their own names. A name already taken in the destination gets " (2)". `ui/src/format/export-naming.cases.json` lists cases that `ExportNaming` (host) and `exportFolderName` / `exportFileNames` (UI) are both tested against.
17. `manifest.json` beside the files: `{ schemaVersion: 1, app: "Memento", mementoVersion, recordingId, title, exportedAt, algorithm: "sha256", files: [{ name, bytes, sha256 }] }`; `name` is relative to the export folder with forward slashes (`Attachments/agenda.docx`). The manifest does not list itself.
18. `export.estimate`'s `files` and `bytes` are the totals of its `items` plus `manifest.json` (one file, its estimated size) when anything would be written; `items` never lists the manifest. A reason in `unavailable` is given only for a ticked row. The UI sums the ticked rows and adds the manifest share (`files − items.length`, `bytes − Σ items.bytes`) once.
19. Export jobs run one at a time in the order `export.run` was called; a waiting job sends no `export.progress` until it starts. On cancel or failure the files written so far are removed and the last `export.progress` has `files: 0, bytes: 0`; on success `files` and `bytes` count the manifest too.
20. "Pause when the PC is busy" no longer pauses a transcription pass on the graphics card for a busy processor (the pass barely uses it); a recording in progress still pauses it while the setting is on, and low disk space and `processing.pause` always do. Passes on the processor, and the speaker pass, pause as before. The footer's `processingPaused` follows the stage that runs or waits.
21. Settings › Transcription and Speakers keep Remove off, with the note "Needed by the current settings", for the default transcription model, the model used without a graphics card, the default voice model and, while Identify speakers is on, the speech-segmentation model. `models.remove` itself still refuses only a model in use (`models.inUse`).

## Shared types (M4)

Documents, templates, styles, AI providers and generation. The document model, module catalog, templates, styles, renderer and exporters live in `src/Memento.Documents` (M4b); providers, the payload composer and the chunker in `src/Memento.AI` (M4a); the store, the pipeline and these methods in `src/Memento.Generation` (M4d). Records: `src/Memento.Core/Bridge/Contracts`, serialized by `M4BridgeJsonContext`.

```ts
type ModuleId = 'title' | 'summary' | 'executiveSummary' | 'participants' | 'agenda' | 'topic' | 'discussion' | 'decisions' | 'actionItems' | 'owner' | 'deadline' | 'openQuestions' | 'quote' | 'highlight' | 'chapter' | 'timeline' | 'followUpEmail' | 'nextMeeting' | 'meetingPurpose' | 'notes' | 'fullTranscript' | 'customText' | 'customAi';
type ContentShape = 'paragraph' | 'list' | 'table' | 'chips' | 'labelValue' | 'quote' | 'timeline' | 'transcript' | 'text';
interface ModuleInfo { id: ModuleId; name: string; group: 'structure' | 'detail' | 'custom'; shape: ContentShape; generated: boolean; defaultLength: 'short' | 'medium' | 'long';
                       groundingRule: string | null; description: string;
                       groundingRules: string[]; defaultLinkToTranscript: boolean; columns: string[]; labels: string[] }   // the last four are additive
type TextSize = 'smaller' | 'normal' | 'larger';
interface ModuleSettings { id: string; module: ModuleId; instructions: string; length: 'short' | 'medium' | 'long'; textSize: TextSize; linkToTranscript: boolean; customTitle: string | null; customText: string | null }
interface TemplateRow { modules: ModuleSettings[] }                              // 1–3
interface InputSelection { transcript: boolean; details: boolean; participants: boolean; agenda: boolean; highlights: boolean; attachments: boolean; previousDocuments: boolean }   // audio/video never exist here
interface Template {
  id: string; name: string; builtIn: boolean; recordingTypes: RecordingType[]; rows: TemplateRow[]; inputs: InputSelection;
  providerId: ProviderId | null; styleId: string;
  output: { alsoExportDocx: boolean; alsoExportMarkdown: boolean; alsoExportPdf?: boolean };
  modifiedAt: string | null;            // null: a built-in never changed
  documentKind: string;                 // "Meeting minutes": the meta line's kind and the word in "Generate {documentKind}"
  processingInstructions?: string;      // whole-document instructions; never override the grounding rules
  customized?: boolean;                 // a built-in with a customised copy (Reset applies)
}
type ProviderId = 'anthropic' | 'openai' | 'local';
interface ProviderInfo { id: ProviderId; name: string; vendor: string /* "Anthropic", "OpenAI", "This PC" */; kind: 'cloud' | 'local'; ready: boolean;
                         reason: string | null /* "External AI is off", "No key saved", "Model not installed", "Not enough video memory" */; modelLabel: string | null;
                         code: string | null /* ai.disabled | ai.noKey | ai.modelNotInstalled | ai.notEnoughVram */; detail: string | null /* the §17 sentence, or a note when ready */; modelId: string | null /* local catalog id */;
                         gpuMemory: GpuMemoryInfo | null /* local only, after 1.1.0 */; gpuNote: string | null /* local only: why a card model is off the card; also starts detail */ }
interface StyleSettings { headingFace: 'sans' | 'serif'; bodyFace: 'sans' | 'serif'; baseSize: 'small' | 'normal' | 'large'; headingCase: 'normal' | 'smallCaps'; numberedHeadings: boolean; headingColor: 'navy' | 'ink' | 'forest' | 'burgundy'; tableHeaderFill: boolean; ruleUnderTitle: boolean; linesBetweenSections: boolean; spacing: 'tight' | 'normal' | 'airy'; paper: 'letter' | 'a4'; pageNumbers: boolean; runningHeader: boolean }
interface Style { id: string; name: string; builtIn: boolean; settings: StyleSettings; usedByTemplates: number; modifiedAt: string | null; customized?: boolean }
interface DocumentSummary { id: string; name: string; kind: 'generated' | 'written'; templateName: string | null; styleId: string; providerId: ProviderId | null; generatedAt: string | null; version: number; versions: number; modifiedAt: string; sizeBytes: number }
interface GenerationRecord {
  templateId: string; templateName: string; styleId: string; providerId: ProviderId; modelLabel: string; startedAt: string; durationMs: number;
  inputs: InputSelection; payloadHash: string; payloadKept: boolean; chunks: number;
  modules: { moduleId: string; claims: number; verified: number; dropped: number; notDiscussed: boolean }[];   // generated modules only
  sent: string[];                       // the included sections, as the preview names them: "Transcript (118 segments, 4 speakers)"
  bytes: number; stayedOnPc: boolean;   // true for the local model: nothing was sent
  claims: { id: string; moduleId: string; text: string; t: number | null; verdict: 'supported' | 'unsupported' | 'notChecked'; kept: boolean; reason: string | null }[];
  payloadText: string | null;           // when "keep a record of what was sent" was on
}
interface DocumentModule { id: string; module: ModuleId | string; title: string; textSize: TextSize; linkToTranscript: boolean; blocks: Block[] }   // Block per src/Memento.Documents/Model/Blocks
interface DocumentContent { schemaVersion: 1; id: string; title: string; meta: string; rows: { modules: DocumentModule[] }[]; record: GenerationRecord | null; styleId: string | null; version: number }
interface GenerationProgress { jobId: string; recordingId: string; documentId: string | null; stage: 'composing' | 'generating' | 'verifying' | 'rendering' | 'done' | 'failed' | 'cancelled'; moduleId: string | null; percent: number; message: string | null; code: string | null /* on failed: ai.network, ai.notEnoughVram… */ }
interface GenerationSendSummary { providerId: ProviderId; providerName: string; modelLabel: string | null; inputsUsed: InputSelection; bytes: number; chunks: number }
```

## Methods (M4)

| Method | Params | Result | Notes |
|---|---|---|---|
| `modules.list` | `{}` | `{ modules: ModuleInfo[] }` | The catalog; the palette reads it. |
| `templates.list` / `templates.get` | `{}` / `{ templateId }` | `{ templates: Template[] }` / `Template` | Built-ins first, then the user's by name. |
| `templates.save` | `{ template: Template }` | `Template` | New id (a slug of the name) when `id` is empty or unknown. A built-in is never saved over: saving one creates a copy. A new template never takes a name already in the library: "{name} (copy)", then "{name} (copy 2)" (1.1.0; the Builder's **Save as new template** sends an empty `id`). The style must exist (`styles.notFound`). |
| `templates.duplicate` / `templates.delete` / `templates.resetBuiltIn` | `{ templateId }` | `Template` / `{}` / `Template` | Delete refused for built-ins (`templates.builtIn`). |
| `styles.list` / `styles.get` / `styles.save` / `styles.duplicate` / `styles.delete` / `styles.resetBuiltIn` | likewise | likewise | Presets behave as built-in templates; delete is refused with `styles.inUse` (detail: the templates) while a template uses the style. |
| `styles.sampleHtml` | `{ settings: StyleSettings }` | `{ html }` | The Style editor's live sample page (fixed sample minutes). |
| `providers.list` | `{}` | `{ providers: ProviderInfo[], externalAiEnabled: boolean, defaultProviderId: ProviderId \| null }` | Readiness from Settings, keys, the model manager and free video memory. No provider is constructed and nothing is sent. |
| `generation.preview` | `{ recordingId, template: Template }` | `{ payloadText, bytes, chunks, inputsUsed: InputSelection, warnings: string[], providerId, staysOnPc }` | "Preview exactly what will be sent" (for Local: "nothing leaves this PC"). Nothing is sent. |
| `generation.previewHtml` | `{ recordingId: string \| null, template: Template, styleId }` | `{ html }` | The Builder's live preview paper with skeletons; `recordingId: null` (a template edited from Settings) shows the sample title and meta line. |
| `generation.start` | `{ recordingId, template: Template, documentId?: string /* regenerate into */ }` | `{ jobId, confirmationRequired: boolean, summary: GenerationSendSummary \| null }` | Refused with `ai.disabled` (a cloud provider while external AI is off; checked before anything is read or constructed), `ai.providerNotReady` (detail: `ai.noKey`, `ai.modelNotInstalled`, `ai.notEnoughVram`), `generation.noTranscript` (no transcript, or the transcript not among the allowed inputs), `generation.busy`, `documents.notFound` (the regenerated document). With "ask before every send" a **cloud** job returns `confirmationRequired: true` and the summary, and nothing runs until `generation.confirm`; the local model sends nothing and is not asked. |
| `generation.confirm` / `generation.cancel` | `{ jobId, approved }` / `{ jobId }` | `{}` | `generation.notFound` for an unknown or finished job. Declining or cancelling a waiting job ends it with `stage: 'cancelled'`. |
| `documents.list` | `{ recordingId }` | `{ documents: DocumentSummary[] }` | Newest change first. |
| `documents.get` | `{ recordingId, documentId }` | `{ document: DocumentContent, summary: DocumentSummary }` | |
| `documents.renderHtml` | `{ recordingId, documentId, mode: 'view' \| 'print' }` | `{ html }` | `view`: the `article.paper` element only; `print`: the whole print page (`@page` rules, footnoted timestamps) the PDF is printed from. |
| `documents.create` | `{ recordingId, name, styleId }` | `DocumentSummary` | A hand-written document with one empty text module ("Notes"). |
| `documents.saveEdit` | `{ recordingId, documentId, html }` | `{ document: DocumentContent, version }` | The viewer's light edits, parsed back into blocks (`HtmlToBlocks`); markup the viewer never produces (scripts, images, forms, event attributes, no sections) is refused with `documents.unsupportedEdit` and nothing is saved. Saving unchanged markup writes nothing. Debounced by the UI. |
| `documents.rename` / `documents.duplicate` / `documents.delete` | `{ recordingId, documentId, name? }` | `DocumentSummary` / `DocumentSummary` / `{}` | Delete asks nothing on the host; the UI confirms. A copy is "{name} (copy)" unless `name` is given. |
| `documents.makeTemplate` | `{ recordingId, documentId, name }` | `Template` | The generation's template with the document's layout, headings, text sizes and links. |
| `documents.versions` / `documents.restoreVersion` | `{ recordingId, documentId }` / `{ recordingId, documentId, versionId }` | `{ versions: { id, at, reason: 'generated' \| 'edited' \| 'restored' \| 'regenerated', changes, version }[] }` / `{ document, summary }` | Empty when history is off. Newest first, and the first entry is the current content (`id: "current"`, `changes: 0`, not restorable); the others are the kept versions. `reason` says how that content came to be; `version` is its version number; `changes` counts the modules (and title) that differ from now. `DocumentSummary.versions` counts the same entries. `documents.versionNotFound` for a pruned version. |
| `documents.export` | `{ recordingId, documentId, format: 'docx' \| 'pdf' \| 'markdown', path?: string }` | `{ path, bytes, sha256 }` | Without `path`: Settings › Export's folder (or Documents), `{base} - {document name}.{ext}`, never over an existing file. A relative path or a missing folder is `export.destinationUnwritable`; a failed export `documents.exportFailed` (the document is unchanged). PDF is printed by the host through WebView2. The Export dialog's Documents row uses `export.run` with `documents` filled. |

## Events (M4)

| Event | Payload |
|---|---|
| `generation.progress` | `GenerationProgress` (at most four per second; every stage change and the final one always pass; nothing after the final one) |
| `generation.output` | `GenerationOutput`: the Live output sheet's exchange (see Live output (M4) below); its `done` comes before the job's final `generation.progress` |
| `documents.changed` | `{ recordingId, documentId, reason: 'generated' \| 'edited' \| 'created' \| 'deleted' \| 'restored' }` (a rename is `edited`); `library.changed` follows |
| `templates.changed` / `styles.changed` | `{}` |

## Settings snapshot (M4 additions)

```ts
ai: { …M3,
      defaultProviderId: ProviderId | null;    // null: the local model
      localModelId: string | null;             // the local model in effect: the installed choice, else the installed one the hardware suits, else any installed one; with none installed, the choice or the hardware's (to download); null only without local models
      localModelChosen: boolean;               // localModelId was chosen in Settings
      providers: { anthropic: { hasKey: boolean; model: string; models: string[] }; openai: { hasKey: boolean; model: string; models: string[] } } }   // model in effect; models offered
documents: { defaultTemplateId: string; defaultStyleId: string }   // "meeting-minutes" and "corporate" until chosen
```

`settings.set` takes `ai.defaultProviderId` (`'anthropic' | 'openai' | 'local' | null`), `ai.localModelId` (an installed `kind: 'llm'` catalog id, or `null` for the hardware default), `ai.providers.anthropic.model` / `ai.providers.openai.model` (a model id, or `null` for the default) and `documents.defaultTemplateId` / `defaultStyleId` (an id, or `null` for the built-in). A field left out keeps its value; `null` clears it; a bad value is `settings.invalidValue` with the field as `detail`, and nothing is written. Keys still change only through `ai.setKey` / `ai.clearKey`.

"Allow external AI services" (`ai.enabled`) governs the cloud providers only. The local model sends nothing and is available whenever its model is installed, whatever `ai.enabled` says. The share switches (`ai.share`) limit what a cloud provider receives; a template's ticks never exceed them.

Model defaults: Claude **`claude-opus-5-5`** (the claude-api skill's default and Anthropic's most capable generally available Opus) with `claude-fable-5-1` offered (Anthropic's most capable model: higher price, always thinks, needs 30-day data retention); ChatGPT `gpt-6-astra` (M4a's choice from OpenAI's documentation). The local model by hardware: Qwen3.5 4B (`qwen3.5-4b-q4`) when the discrete card has 3.3 GB free, otherwise Ministral 3 3B (`ministral-3-3b-q4`); both are `kind: 'llm'` entries of `models.list` (`engine: 'llm'`), installed like every other model.

## Error codes (M4)

`ai.disabled`, `ai.providerNotReady` (detail: the specific code), `ai.noKey`, `ai.invalidKey`, `ai.rateLimited`, `ai.network`, `ai.providerError`, `ai.contentTooLong`, `ai.modelNotInstalled`, `ai.notEnoughVram`, `ai.workerCrashed`, `generation.noTranscript`, `generation.busy`, `generation.notFound`, `templates.notFound`, `templates.builtIn`, `styles.notFound`, `styles.builtIn`, `styles.inUse`, `documents.notFound`, `documents.unsupportedEdit`, `documents.versionNotFound`, `documents.exportFailed`.

The `ai.*` provider codes reach the UI as the code of a failed generation progress event, with the provider's own §17 message ("Claude could not be reached: no network. Nothing was sent twice and no document was changed."); a failure never changes an existing document.

## Design references (M4)

Builder: DESIGN §10 and `Builder.dc.html`. Viewer: §12 and `DocView.dc.html`. Style editor: §13 and `StyleEditor.dc.html`. Error states: §17 (AI provider failed card). The "ask before every send" confirmation is a dialog (§5.19) showing the provider, the inputs, the size and the chunk count.

## Decisions (M4, at the M4d integration)

1. `DocumentContent.rows` are rows of modules `{ id, module, title, textSize, linkToTranscript, blocks }`, the engine's model (with `type` named `module`), not bare `{ blocks }`. The UI shows documents through `documents.renderHtml` only.
2. The `html` of `documents.renderHtml` (`view`), `generation.previewHtml` and `styles.sampleHtml` is the `article.paper` element only. The UI bundles the paper stylesheet itself (`ui/src/components/paper/paper.css`, a verbatim copy of `PaperCss.Stylesheet`; its CSP forbids inline `<style>`); a host test fails when the two differ.
3. `generation.previewHtml` accepts `recordingId: null`.
4. `GenerationStartResult.summary` is `{ providerId, providerName, modelLabel, inputsUsed, bytes, chunks }`.
5. `documents.versions` entries carry `version`.
6. `ExportEstimateItem` gains `documentId` (Documents rows only; left out elsewhere). In `ExportSelection.documents`, an empty `documentIds` exports every document of the recording; each is `{base} - {document name}.{ext}`.
7. `Template.documentKind` is a string, always set (the template's name when it has none).
8. `documents.rename` and `documents.duplicate` return `DocumentSummary`; `documents.delete` returns `{}`.
9. The local model catalog ids are `qwen3.5-4b-q4` and `ministral-3-3b-q4` (catalog ids may contain dots).
10. The Local provider is available whenever its model is installed, regardless of "Allow external AI services", which governs cloud providers only. Without a template or Settings provider, generation uses the local model.
11. A template's output toggles ("Also export Word / Markdown", and PDF) are honoured after generation: the files go to the project's `documents/exports/` as `{document name} v{version}.{ext}` (never over an earlier copy), each listed in History.
12. Versions follow the transcript rules: a version is kept on the first edit after a generation, creation or restore (a run of edits is one version), on regenerate and on restore; a rename keeps none. Versions live in `versions/document.{documentId}.{utc-stamp}.json` and are pruned with the transcript's.
13. A generation appends one History line (stage `minutes`): the template, the provider and model, exactly which inputs were sent and how much, where (or that nothing left the PC), that audio and video were not sent, the claims found, verified and dropped, and the time taken. Failures and cancellations get their own line.
14. `Template.modifiedAt` and `Style.modifiedAt` are `null` for a built-in that was never changed.

## Decisions (M4 integration, 0.9.0)

15. `documents.versions` lists the current content first (`id: "current"`), then the kept versions, newest first; the viewer shows the first as "Version n · current" without Restore. Before, the host listed only the kept versions while the UI took the newest of them for the current one, so after a regeneration the viewer showed version 1 as current and offered nothing to restore. `DocumentSummary.versions` counts the same entries.
16. `generation.preview`'s `inputsUsed` and the send confirmation's summary name only the inputs the payload holds: a ticked input with nothing in it (no participants, no agenda) is not "used". The record's `inputs` stay the ticks (a regeneration starts from them); its `sent` lines name what was read.
17. A failed `generation.progress` carries `code` in the UI type too; the failure card leads with what happened from it ("Claude did not accept the key", "The local model ran out of video memory").
18. End-to-end runs may point Claude or ChatGPT at a fake server through `MEMENTO_TEST_ANTHROPIC_URL` / `MEMENTO_TEST_OPENAI_URL`; only a loopback address is honoured.

## Graphics card memory (after 1.1.0)

A product owner read "0.1 GB free" on an RTX 3060 and took the reading for wrong: another app's Ollama server held the card. The reading matched the driver's (ENGINE-NOTES §J); what was missing was who held the memory. The discrete card's memory and its holders now travel with the engine and provider status.

```ts
interface GpuMemoryHolder {
  processName: string;        // "llama-server.exe": the executable holding most of this entry
  description: string;        // "Ollama (llama-server.exe)", "Ollama (llama-server.exe, started by Dictation)", "python.exe (started by Dictation)", "Windows desktop (dwm.exe)", "Memento", "Another program (process 4242)"
  bytes: number;              // dedicated video memory held
  memento: boolean;           // the app, its WebView2 processes and its worker, one entry
  startedBy: string | null;   // the app that started a runtime (Python, Node, Java, Ollama's server), unless a shell or Windows started it
}
interface GpuMemoryInfo {
  gpuName: string;
  totalBytes: number;         // DXGI's dedicated memory (5994 MiB on a "6 GB" card; the sentences round it to the size it is sold with)
  freeBytes: number | null;   // the probe's free memory: min(DXGI budget − use, card − all processes' use); null when unreadable
  usedBytes: number | null;   // all processes' dedicated use on the card (PDH \GPU Adapter Memory)
  holders: GpuMemoryHolder[]; // at most 3, largest first, each ≥ 32 MB; empty when the counters cannot be read
  summary: string;            // "The graphics card has 0.8 GB of 6 GB free. Ollama (llama-server.exe) is using 5.0 GB."
}
```

- **Where it appears.** `engine.status` / `engine.refresh`: `transcription.gpuMemory` (null without a discrete card; `speakers.gpuMemory` is always null) and `transcription.note`, the §17 sentence when the model in effect is installed but runs on the processor because the card is short: "{summary} Large v3 Turbo needs 2.5 GB on the card, so it transcribes on the processor (slower) until that memory is free. To use the card, close Ollama (llama-server.exe) or wait until it lets go of the memory, then check again." `providers.list`: Local's `gpuMemory` (also while its model is not installed) and `gpuNote` when a graphics-card model does not fit ("… Qwen3.5 4B needs 3.6 GB on the card, so it runs on the processor (several times slower) until …", or "so Ministral 3 3B writes instead until …"); `detail` starts with the same sentence, so older readers of `detail` stay complete. Cloud providers carry `null` for both. `status.footer` always carries `null` for `gpuMemory` and `note`, so the 5-second footer sample is not re-sent each time another program's use moves.
- **Holders.** From `\GPU Process Memory(*)\Dedicated Usage` (instances `pid_N_luid_0xHIGH_0xLOW_phys_N`), read once per sample, summed by process over the card's LUID (every `phys_N`), described from one Toolhelp snapshot (executable, parent, start time, file description). Ollama's executables and its `llama-server.exe` read "Ollama (…)"; `llama-server.exe` without an Ollama parent reads "llama.cpp server (…)". A runtime names the nearest ancestor that is neither a runtime nor a shell as `startedBy`; a parent that started after the child (a reused id) is ignored. Processes with the same description are added together. The fix sentence offers `startedBy` (else the description) of the largest holder that is neither Memento nor part of Windows; when Memento is the holder it says its own job is using the card instead.
- **Cost and failure.** About 10 ms per reading on the reference laptop, cached for 4 s (`engine.refresh` drops the cache); any failure reads as no holders, never as an error, and nothing leaves the PC.
- **Which card.** The display driver's hybrid flags (`D3DKMT` adapter type: `HybridDiscrete` / `HybridIntegrated`) decide which adapter is the separate card; without them, NVIDIA or ≥ 2 GB dedicated, as before. An integrated GPU is never given holders.
- **Freshness in the UI.** Opening Settings reads `settings.get` again (the models in effect follow the card's free memory); "Check again" calls `engine.refresh`, then `settings.get`, `models.list` and, in AI and privacy, `providers.list`.

## Live output (M4)

While a document is generated the host sends the exchange with the provider as `generation.output` events, for the Builder's and the viewer's **Show live output** sheet (DESIGN.md §10, §12). Nothing new is sent anywhere: the text is the requests the provider receives and the replies it gives, plus what the pipeline did in code. The host keeps none of it once sent and writes none of it; the UI keeps it in memory until the Builder or viewer that shows it is left (or the next generation starts). The generation record and "How this was made" are unchanged (requests are recorded by hash; "keep a record of what was sent" keeps the payload only, as before).

| Event | Payload |
|---|---|
| `generation.output` | `GenerationOutput`, in the order raised, for the running job only |

```ts
type GenerationOutputStep = 'segment' | 'map' | 'reduce' | 'verify' | 'grounding';
interface GenerationOutput {
  jobId: string;
  passId: string;            // 'p1', 'p2'… in order; '' for done
  kind: 'step' | 'request' | 'token' | 'reply' | 'done';
  step: GenerationOutputStep | null;   // with step and request
  title: string | null;      // with step and request: "Decisions and action items · segment 1 of 2", "Check Decisions and action items · 6 questions", "Second vote · one claim over a wider excerpt"
  text: string;              // step: what it did; request: the exact text; token: text since the previous token event of the pass; reply: the whole reply; done: ''
  streamed: boolean | null;  // with request: true for the local model (tokens follow), false for a cloud provider (the reply arrives whole)
  outputTokens: number | null;     // token: so far (decoded pieces); reply: the reply's
  promptTokens: number | null;     // reply: the request's tokens as the provider counted them
  tokensPerSecond: number | null;  // token and reply, local model only
  elapsedMs: number | null;        // step: its time; token: writing so far; reply: from the request to the reply
  stopReason: string | null;       // reply: eog, max_tokens, context, end_turn…
}
```

- **Order.** `step` (segment) → for each map request `request`, then (local) `token`s, then `reply` → `step` (reduce) → the verify requests the same way (second votes too) → `step` (grounding) → `done`. A cloud provider runs up to three requests at once, so their `request`s and `reply`s interleave; each `reply` follows its own `request`. A pass that never got a reply (cancelled, failed) has none; after `done` nothing is sent.
- **Request text.** The request's system prompt and turns as sent, each under its role (`System`, `User`, `Assistant`), separated by a blank line (`AiRequestText`): the payload "Preview exactly what will be sent" shows, cut into the request's chunk, with the pass's instructions. A grammar or JSON schema that constrains the answer is not repeated.
- **Local model.** The worker streams each answer while it is decoded: `progress` lines with phase `generating` carry the text since the previous line, coalesced to the first piece and then at most one line every 40 ms or 8 pieces (`LocalTokenCoalescer`), and an `answered` line closes each prompt with its stop reason, prompt tokens, output tokens, writing time and tokens per second, before the batch's `batch` line. Grammar-constrained passes stream their raw tokens. The host sends a pass's `request` when the worker starts reading it and its `reply` at `answered`.
- **Throttle.** The host queues every event without waiting (the worker's reader is never held up) and one reader publishes them; `token` text of a pass is held until 100 ms have passed since the last `token` event (about ten a second), and any other event first sends what is held, so a `reply` always follows all of its pass's tokens. The UI repaints at that rate.
- **Cloud providers.** Not streamed to the sheet: the `request` when it is sent, the `reply` whole on arrival; the sheet says "Replies from {provider} arrive whole". Their counts come from the provider's usage.

## Updates (H1)

Memento updates itself from the project's GitHub releases (Velopack's `releases.win.json` feed). The host checks once the page has sent `ui.ready` and then every 24 hours while it runs, never while a recording or a processing stage is queued, waiting or running (it waits until both are idle), downloads in the background and then offers a restart. Nothing installs until the person chooses "Restart to update", or the next time Memento starts. A pre-release is offered only to a pre-release build (a SemVer suffix, or a version before 0.5.0); from 0.5.0 on only full releases count. With `general.autoUpdate` off nothing is checked automatically; `updates.check` still works. A copy not installed with Setup (a build folder) never checks.

```ts
interface UpdateStatus {
  currentVersion: string;
  state: 'unavailable' | 'idle' | 'checking' | 'downloading' | 'ready' | 'failed';   // failed: only after updates.check
  availableVersion: string | null;   // while downloading or ready, and while a download is deferred
  percent: number | null;            // while downloading
  lastCheckedAt: string | null;      // ISO 8601, when the feed last answered
  message: string | null;            // after updates.check: "Memento 0.5.0 is the newest version.", why it failed, or that the download waits
  deferred: boolean;                 // a newer version waits to download until no recording or processing runs
}
```

| Method | Params | Result | Notes |
|---|---|---|---|
| `updates.status` | `{}` | `UpdateStatus` | |
| `updates.check` | `{}` | `UpdateStatus` | "Check now": answers once the feed did; a newer version then downloads in the background (once idle). A failed check is not an error: `state: 'failed'` with the reason in `message`. `updates.unavailable` for a copy that cannot update itself. |
| `updates.apply` | `{}` | `{}` | "Restart to update": Memento closes normally, the update installs and Memento starts again. `updates.notReady` when nothing is downloaded, `updates.busy` while recording. |

| Event | Payload |
|---|---|
| `updates.progress` | `UpdateStatus`, on every change (state, percent). The UI shows a toast "Restart to update to {version}" when `state` becomes `ready`. |
| `status.footer` | adds `update?: { downloading: boolean, percent: number \| null, version: string \| null }` (the footer shows "Downloading Memento {version} · 42%"). |

Settings snapshot: `general` adds `autoUpdate: boolean` (default `true`), merged like the other `general` fields.

## Error codes (H1)

`library.unavailable` (the library folder chosen in Settings is missing: its drive is not connected, or it was moved or renamed; `library.list` and `recording.start` answer it and nothing is created in its place; detail: the folder. The default library is created on first run as before. Once the folder is back, the next `library.list` opens it, recovers interrupted recordings and resumes processing), `updates.unavailable` (this copy was not installed with Setup, so it cannot update itself), `updates.notReady` (`updates.apply` with nothing downloaded), `updates.busy` (`updates.apply` while recording; the update installs at the next start instead).

## Clipboard and transcript text options (after 1.2.0)

The product owner asked for the transcript without timestamps or speakers (in any combination), for copies straight to the clipboard, and for a filtered transcript in Review. Filtering is the UI's alone (DESIGN.md §9); a filtered copy names its lines in `transcript.copy`.

```ts
interface TranscriptTextOptions {
  timestamps: boolean;                  // an [h:mm:ss] marker before every segment (true)
  speakers: boolean;                    // the speaker's name before each turn or line, and "Speakers: …" in the heading (true)
  layout: 'auto' | 'turns' | 'lines';   // auto: Markdown in paragraphs per speaker turn, text one line per segment (as before)
}
interface TranscriptCopyResult { lines: number; totalLines: number; characters: number }   // "Copied 42 of 318 lines"
interface DocumentCopyResult { characters: number; formatted: boolean }
```

- **One formatter.** `Memento.Core.Export.TranscriptText` writes the Markdown and text files of `export.run` and the text of `transcript.copy`, so a copy reads exactly like the file. The defaults (a field missing, or `options` missing or `null`) write what earlier versions wrote. Any combination is allowed; a layout other than the three answers `bridge.invalidParams` naming it. `turns` joins consecutive lines of the same speaker into one paragraph (a line without a speaker stands alone), also when the names are left out; `lines` gives every segment its own line (Markdown: its own paragraph). Lines without words are skipped. JSON and SRT keep their own structure and ignore the options. The document's Full transcript module is a block the composer builds from `transcript.json`, not this text, so the options do not apply to it.
- **In the export.** `ExportSelection.transcript.options` carries them; the UI remembers them per user (`ui/src/state/uiPrefs.ts`) and sends them with every estimate and run, and Settings › Export's defaults keep them when "Remember these choices" is on. `settings.get` answers the block with `options` filled. `manifest.json` (M3 clarification 17) gains `transcriptOptions: TranscriptTextOptions | null`: the options the Markdown and text files were written with, `null` when the export has neither. The field is additive; `schemaVersion` stays 1.
- **The clipboard is the host's.** The page never uses the browser's clipboard API: the host writes the Windows clipboard on the window's thread (WPF `Clipboard.SetDataObject`, flushed so the copy outlives Memento) with Unicode text, plus the Windows `HTML Format` (CF_HTML, `Memento.Core.Host.ClipboardHtml`: UTF-8 byte offsets of the page and of the fragment between `<!--StartFragment-->` and `<!--EndFragment-->`) when there is formatted content, and `CanUploadToCloudClipboard` = 0 so Windows' "sync across your devices" never uploads it. Clipboard history on this PC still works. Nothing of the text is logged.

| Method | Params | Result | Notes |
|---|---|---|---|
| `transcript.copy` | `{ recordingId, format: 'text' \| 'markdown', options?: TranscriptTextOptions \| null, segmentIds?: string[] \| null }` | `TranscriptCopyResult` | The transcript as its export file would read, on the clipboard as text. `segmentIds` limits it to those lines (Review's filtered view), in transcript order, and the heading names only their speakers; missing or `null` copies every line. `lines` counts the lines copied, `totalLines` the transcript's lines with words. An empty list or more than 100,000 ids is `bridge.invalidParams`; an id the transcript does not have answers `transcript.segmentNotFound` (`detail`: that id) and nothing is copied; no transcript yet is `transcript.none`; a format other than the two is `bridge.invalidParams`. Reads only. |
| `documents.copy` | `{ recordingId, documentId }` | `DocumentCopyResult` | One document twice over: as the Markdown `documents.export` writes (the clipboard's text, for any program) and as the page its PDF is printed from (`documents.renderHtml` `print`: styles, headings, tables, footnoted timestamps), so Word and Outlook paste it formatted. `documents.notFound` for an unknown document. Reads only. |

Both answer `clipboard.unavailable` when Windows does not let Memento open the clipboard (another program holds it; WPF retries for about a second first): "Windows did not let Memento use the clipboard, so the transcript was not copied. Nothing was changed. Try again in a moment; if another program keeps the clipboard open, close it first." (`detail`: Windows' code). The UI confirms a copy quietly ("Copied 42 of 318 lines as text" in the status beside Undo, or in the Export dialog's footer) and shows a refusal as a warning toast with the host's message.

## Error codes (clipboard)

`clipboard.unavailable` (Windows did not let Memento write the clipboard; nothing was copied; detail: Windows' code).

## History links (after 1.2.0)

The product owner asked to click an event in History and go back to that version of the transcript or document. A History line that changed the transcript or a document opens the stored copy of the content right after it, read-only, with "Restore this version" (the existing restore methods, one Undo step) and "Back to current" (DESIGN.md §9, §5.19 banner).

```ts
interface HistoryLink {
  index: number;                      // the line's position in Project.history (oldest first, from 0)
  kind: 'transcript' | 'document';
  documentId: string | null;          // the document the line changed; null for the transcript, or when not known
  versionId: string | null;           // 'current', a kept version's id, or null: no copy of the content right after the line is kept
  after: string;                      // the banner's words: "after speakers were identified"
}
interface HistoryLinksResult { links: HistoryLink[] }
interface DocumentVersionResult { document: DocumentContent; html: string }   // html: the viewer's paper (renderHtml, view) for that version
```

| Method | Params | Result | Notes |
|---|---|---|---|
| `history.links` | `{ recordingId }` | `HistoryLinksResult` | One entry per line that changed the transcript (`transcript` `completed`, `speakers` `completed`, and the `edited` lines "Transcript edited", "Speaker …", "Speakers merged", "Transcript version restored") or a document (`minutes` `completed`, and `edited` "Document "…" created / created as a copy / edited / restored"); other lines are not listed. Works from the files already on disk, so recordings from before have links too. `project.notFound` for an unknown recording. Reads only. |
| `transcript.getVersion` | `{ recordingId, versionId }` | `{ transcript }` | A kept version, whole (the shape of `transcript.get`'s `transcript`), to read before restoring. `transcript.versionNotFound` when it is not kept (any more). Reads only. |
| `documents.getVersion` | `{ recordingId, documentId, versionId }` | `DocumentVersionResult` | A kept version's content and its paper, drawn with the style it names. `documents.notFound`, `documents.versionNotFound`. Reads only. |

How a line finds its copy (`Memento.Core.History.HistoryLinker`): every stored copy (the current content, and each kept version while Settings › Documents › version history is on, as `transcript.versions` and `documents.versions` list them) was current from the write that defined it (its `lastChange.at`) until a write replaced it (a kept version's id is the UTC moment it was replaced). A History line is written just after its write, so it belongs to the copy current at its time; for a document, the copy written last before the line, within two minutes. Of the lines inside one copy only the last opens it: the earlier ones were changed again before a copy was kept (the transcript before speakers were identified, the first lines of a run of edits, which keeps one version). Lines whose copy was removed after the version-history days have `versionId: null`. Retention is unchanged. Restoring from the banner goes through `transcript.restoreVersion` / `documents.restoreVersion`; the version that restore keeps of the replaced content (the id new in `transcript.versions` / `documents.versions`) is what Undo restores, and Redo restores the opened version again. Without version history nothing is kept, so only `current` links open.

The document viewer's route takes the version to open: `#/document/<recordingId>/<documentId>?version=<versionId>`.
