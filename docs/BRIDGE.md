# Memento — Bridge contract

The host ↔ UI contract (ARCHITECTURE.md §3). Every method and event listed here exists as a C# record in `src/Memento.Core/Bridge/Contracts/` and a TypeScript type in `ui/src/bridge/types.ts`. JSON field names are camelCase. Times are ISO 8601 with offset unless the field ends in `Ms` (milliseconds, integer). Ids are opaque strings. Lists are never null; use `[]`.

Status: **M0, M1, M2 and M3 methods and events are implemented** (M3 in 0.4.0) in the host (`src/Memento.Core/Bridge/Methods/`) and in the UI's browser-preview mock (`ui/src/bridge/mock*.ts`). This document is what both follow. `ContractSerializationTests`, `M2ContractSerializationTests` and `M3ContractSerializationTests` pin the host's JSON, and `ContractDocumentationTests` checks that the host's error codes and stage names are exactly the ones listed here and in `types.ts`.

## Shared types

```ts
type RecordingType = 'meeting' | 'interview' | 'presentation' | 'lecture' | 'dictation' | 'research' | 'general' | string; // any other string is a custom type name
type StageName = 'stored' | 'video' | 'transcript' | 'speakers' | 'topics' | 'minutes' | 'mix' | 'optimize'; // pipeline order
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
interface WhoSpoke { count: number | null; /* 1–20, or null */ names: string[]; /* at most 20, 1–100 characters each */ marks: SpokeMark[]; /* 2.6.0: Speaking now, at most 200 */ added?: SpeakerAdded[]; /* 2.7.0: always sent; absent in a patch keeps none */ }
interface SpokeMark { name: string; /* one of names, as spelled there */ atMs: number; /* recorded time when the button was pressed */ }
interface SpeakerAdded { name: string; /* one of names; at most one per name, others dropped */ atMs: number; /* recorded time when the name was added while recording, 0 or more */ at: string; /* wall-clock ISO 8601 */ }

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
- `video` — 2.0, only for recordings with video: joins a track's parts and hashes the video files (Video (2.0) below). Listed second, run after the transcript stages.
- `mix` — after 2.0.0-beta.1, only on request (`project.rebuildMix`, Even out source levels below): mixes the stored tracks again and replaces the mix. Never queued by itself; finished, it is left out of rows like `stored`.
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
| `project.updateDetails` | `{ recordingId, details: Partial<RecordingDetails> }` | `Project` | M1. Works during recording too. After 1.2.0 `whoSpoke` replaces whole: `count` null or a whole number 1–20, `names` at most 20 of 1–100 characters (trimmed; a repeat, ignoring case, is dropped), and since 2.6.0 `marks` at most 200 Speaking now marks, each naming one of `names` (ignoring case; kept spelled as the name is) at `atMs` 0 or more; anything else answers `bridge.invalidParams` naming the problem and nothing changes. Before recording starts the UI keeps it and sends it with the other pre-start details. |
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
    storage: { codec: 'flac' | 'aac' | 'mp3'; bitrateKbps: number | null; downmixMono: boolean; keepOnlyMix: boolean; evenLevels: boolean }; // aac/mp3: the optimize stage converts after processing; evenLevels: after 2.0.0-beta.1
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
  speakerByUser?: boolean;                                                       // 2.6.0: the user placed the line on its speaker (left out while false)
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
- `speakerConfidence`: sherpa-onnx's turn score is a similarity, not a probability. The host maps it linearly (a score of 0.2 or less → 0, 0.6 or more → 1; a track with a single cluster, score −2, → 1) and multiplies it by the winning speaker's share of the speech the segment overlaps. The UI marks a speaker as uncertain below 0.7. Since 2.6.0 the confidence is also capped by the line's **voice fit** (ENGINE-NOTES.md §V): a raw diarizer voice that sounds unlike the rest of the person it was grouped into (cosine under about 0.48 to the mix of the person's other voices, when those hold at least as much speech) makes its lines uncertain too, so the marks point at wrong joins as well as unclean turns; a line the user placed (`speakerByUser`) stays at 1. History's `speakers` `completed` detail says "41 lines marked uncertain by voice".
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
| `recording.liveTranscript` | `{ sessionId, segments: { start: number, end: number, text: string }[], /* 2.0: */ state, engine, note }` | Rough draft segments for the Recording session's Live transcript card; replaced entirely by the full pass. Sent from 2.0 when Settings turns it on: see "Live transcript (2.0)". |

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
  dropMicrophoneBleed: boolean;          // 2.6.0, true: a microphone line that says what a system-audio line says at the same time is left out of a fresh transcript (BleedFilter); system audio keeps the line
}
speakers: { identify: boolean; expectedSpeakers: 'auto' | number /* 1–20 */; rememberRenamed: boolean; embeddingModelId: string; rememberVoices: boolean; splitAtSpeakerChanges: boolean }   // rememberVoices: 2.0, see "Review (2.0)"; splitAtSpeakerChanges: 2.6.0, default true (a fresh transcript's line that spans two speakers' turns is cut where the voice changes before speakers are assigned; never a line that was edited, and never on Identify speakers again)
history: { keepVersions: boolean; keepDays: number }     // true, 90
```

`settings.set` merges each of these blocks field by field: send only the fields that change (sending the whole block works too). `expectedSpeakers` is the string `"auto"` or a whole number from 1 to 20; anything else answers `settings.invalidValue`. It is the speaker count for recordings without their own (`RecordingDetails.whoSpoke`, after 1.2.0): the voices the diarizer found on all tracks are grouped until that many are left (never split), for any number of tracks.

## Error codes (M2)

`transcript.none` (no transcript yet), `transcript.segmentNotFound`, `transcript.speakerNotFound`, `transcript.versionNotFound`, `transcript.speakerInUse` (removing a speaker while lines are assigned to it; detail: its id), `transcript.cannotSplit` (2.6.0: splitting a speaker heard as one voice, or whose voices sound alike; detail: its id), `transcript.cannotJoin` (2.6.0: joining a line that has no next line, or whose next line is on another track; detail: the line's id), `models.notFound`, `models.inUse`, `models.downloadFailed` (detail: cause), `models.busy` (detail: the model downloading now), `models.noSpace`, `engine.unavailable` (detail: what to install or where to turn it on).

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
| `ai.setKey` / `ai.clearKey` | `{ provider: 'anthropic' \| 'openai' \| 'server', key }` / `{ provider }` | `{ hasKey: boolean }` | DPAPI; the key is never returned. M3 stores keys so Settings is complete; M4 uses them. 2.5.0: `server` is the optional bearer key of "Your own server" (1 to 500 characters, no spaces; see "2.5.0 — Your own server"). |
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
general:  { startWithWindows: boolean; keepRunningInTray: boolean; language: 'en' }   // tray and startup applied in 2.0 (see "Tray and Start with Windows (2.0)")
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
type ProviderId = 'anthropic' | 'openai' | 'local' | 'server';   // 'server': 2.5.0, "Your own server"
interface ProviderInfo { id: ProviderId; name: string; vendor: string /* "Anthropic", "OpenAI", "This PC", "Your own server" */; kind: 'cloud' | 'local' | 'server'; ready: boolean;
                         reason: string | null /* "External AI is off", "No key saved", "Model not installed", "Not enough video memory" */; modelLabel: string | null;
                         code: string | null /* ai.disabled | ai.noKey | ai.modelNotInstalled | ai.notEnoughVram; 2.5.0 server: ai.noServer | ai.noModel | ai.serverInsecure */; detail: string | null /* the §17 sentence, or a note when ready */; modelId: string | null /* local catalog id */;
                         modelListed: boolean | null /* after 2.1, cloud only: the model is in Memento's list (false: typed by hand) */;
                         gpuMemory: GpuMemoryInfo | null /* local only, after 1.1.0 */; gpuNote: string | null /* local only: why a card model is off the card; also starts detail */;
                         leavesPc: boolean /* 2.5.0: a generation takes the inputs off this PC */; address: string | null /* 2.5.0, server only */ }
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
| `providers.list` | `{}` | `{ providers: ProviderInfo[], externalAiEnabled: boolean, defaultProviderId: ProviderId \| null }` | Readiness from Settings, keys, the model manager and free video memory. No provider is constructed and nothing is sent. 2.5.0: in the order Claude, ChatGPT, the local model, "Your own server"; the server's readiness is worked out from its address, model and "Allow external AI services" alone (no request, no lookup; "2.5.0 — Your own server"). |
| `generation.preview` | `{ recordingId, template: Template }` | `{ payloadText, bytes, chunks, inputsUsed: InputSelection, warnings: string[], providerId, staysOnPc }` (2.0 adds `provider`, `askBeforeSend`, `inputs`, `redactions`, `passes`: see "Preview what will be sent (2.0)") | "Preview exactly what will be sent" (for Local: "nothing leaves this PC"). Nothing is sent. |
| `generation.previewHtml` | `{ recordingId: string \| null, template: Template, styleId }` | `{ html }` | The Builder's live preview paper with skeletons; `recordingId: null` (a template edited from Settings) shows the sample title and meta line. |
| `generation.start` | `{ recordingId, template: Template, documentId?: string /* regenerate into */ }` | `{ jobId, confirmationRequired: boolean, summary: GenerationSendSummary \| null }` | Refused with `ai.disabled` (a provider whose `leavesPc` is true while external AI is off: a cloud provider, or from 2.5.0 "Your own server" at an address that is not this PC; checked before anything is read or constructed), `ai.providerNotReady` (detail: `ai.noKey`, `ai.modelNotInstalled`, `ai.notEnoughVram`), `generation.noTranscript` (no transcript, or the transcript not among the allowed inputs), `generation.busy`, `documents.notFound` (the regenerated document). With "ask before every send" a job whose provider leaves the PC (`leavesPc`: a **cloud** job, or 2.5.0 a server elsewhere) returns `confirmationRequired: true` and the summary, and nothing runs until `generation.confirm`; the local model and a server on this PC send nothing off the PC and are not asked. |
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
      providers: { anthropic: AiProviderState; openai: AiProviderState; server: AiServerState /* 2.5.0 */ } }
interface AiProviderState {
  hasKey: boolean;
  model: string;                 // the model in effect: the one chosen, or the list's first
  models: string[];              // the models Settings lists, the default first
  choices: AiModelChoice[];      // after 2.1: the same models with their notes, in the same order
  custom: boolean;               // after 2.1: model was typed under "Other model…" (not in models)
  modelNote: string;             // after 2.1: model's note, or what Memento assumes about a model it does not know
}
interface AiModelChoice { id: string; note: string; contextTokens: number }   // "The fastest and cheapest.", 1000000
documents: { defaultTemplateId: string; defaultStyleId: string; ocrEngine: "windows" | "tesseract" }   // "meeting-minutes" and "corporate" until chosen; ocrEngine (2.0) is the engine in effect
```

`settings.set` takes `ai.defaultProviderId` (`'anthropic' | 'openai' | 'local' | 'server' | null`), 2.5.0 `ai.providers.server` (`AiServerPatch`, see "2.5.0 — Your own server"), `ai.localModelId` (an installed `kind: 'llm'` catalog id, or `null` for the hardware default), `ai.providers.anthropic.model` / `ai.providers.openai.model` (a model id, or `null` for the default: one of `models` or any other plausible id, saved trimmed and exactly as typed: letters, digits and `. _ : / -`, starting with a letter or digit, at most 100 characters; an empty string is refused, so none is ever written) and `documents.defaultTemplateId` / `defaultStyleId` (an id, or `null` for the built-in). A field left out keeps its value; `null` clears it; a bad value is `settings.invalidValue` with the field as `detail`, and nothing is written. Keys still change only through `ai.setKey` / `ai.clearKey`.

"Allow external AI services" (`ai.enabled`) governs the cloud providers only. The local model sends nothing and is available whenever its model is installed, whatever `ai.enabled` says. The share switches (`ai.share`) limit what a cloud provider receives; a template's ticks never exceed them. 2.5.0: both rules key on `ProviderInfo.leavesPc`, not on the kind: they also govern "Your own server" at an address that is not this PC, and not a server on this PC (loopback), which is treated like the local model.

Cloud models (after 2.1; `AiModelDefaults`, the first of each list is the default):

| Provider | Model | Note in Settings | Context | Sent with it |
|---|---|---|---|---|
| Claude | **`claude-opus-5-5`** | The most capable for long transcripts. Memento's default. | 1M | `effort: high`, `fallbacks: "default"` |
| Claude | `claude-fable-5-1` | Anthropic's top tier: the newest and most intelligent. Costs more and takes longer; the account needs 30-day data retention. | 1M | `effort: high`, `fallbacks: "default"` |
| Claude | `claude-sonnet-5-5` | Fast and strong, at about half the price of Opus. | 1M | `effort: high`, `fallbacks: "default"` |
| Claude | `claude-haiku-5-5` | The fastest and cheapest. | 1M | `effort: high` (Haiku has no server-side fallback) |
| ChatGPT | **`gpt-6-astra`** | OpenAI's most capable model. Memento's default. | 1.05M | `reasoning.effort: high` |
| ChatGPT | `gpt-6.1-sol` | Nearly as capable, at a lower cost. | 1.05M | `reasoning.effort: high` |
| ChatGPT | `gpt-6-luna` | OpenAI's most efficient: the fastest and cheapest. | 1.05M | `reasoning.effort: high` |

Sources: the claude-api skill's model table (cached 2026-10-06) and OpenAI's model pages (developers.openai.com/api/docs/models, read 2026-10-12); see ENGINE-NOTES.md. A model typed under "Other model…" is sent exactly as typed, with no effort or fallback setting (a model Memento does not know may reject them), an assumed context of 200k (Claude) or 128k (ChatGPT) and an answer limit of 32,000 or 16,384 tokens. Every cloud request carries at most 24,000 transcript tokens (less when a quarter of the model's context is smaller): a long transcript is split into segments, so no transcript is refused for its length. A model the service does not offer (HTTP 404, or 400 naming the model) fails the generation with `ai.unknownModel`: "Claude does not offer a model called 'x'. Nothing was generated and no document was changed. Choose one from the list in Settings › AI and privacy, or check the id." No other model is tried in its place.

`ProviderInfo.modelListed` (after 2.1): for Claude and ChatGPT, whether `modelLabel` is in Memento's list (`false` for a model typed by hand); `null` for the local model. A ready cloud provider's `detail` names the model, what it reads and the split: "Sends the ticked inputs to Claude (claude-haiku-5-5) when you press Generate. claude-haiku-5-5 reads up to 1M tokens a request; a long transcript is split into segments of up to 24,000 tokens, so it always fits."

The local model by hardware: Qwen3.5 4B (`qwen3.5-4b-q4`) when the discrete card has 3.3 GB free, otherwise Ministral 3 3B (`ministral-3-3b-q4`); both are `kind: 'llm'` entries of `models.list` (`engine: 'llm'`), installed like every other model.

## Error codes (M4)

`ai.disabled`, `ai.providerNotReady` (detail: the specific code), `ai.noKey`, `ai.invalidKey`, `ai.rateLimited`, `ai.network`, `ai.providerError`, `ai.contentTooLong`, `ai.unknownModel`, `ai.modelNotInstalled`, `ai.notEnoughVram`, `ai.notEnoughMemory` (after 2.2: a larger local model does not fit in RAM and video memory with the reserves kept, or was unloaded because a recording started; see "After 2.2 — Larger local models"), `ai.workerCrashed`, 2.5.0 "Your own server" (see "2.5.0 — Your own server"): `ai.noServer`, `ai.noModel`, `ai.serverInsecure`, `ai.serverUnreachable`, `ai.serverNotCompatible`, `ai.serverNeedsKey`, `ai.serverModelUnknown`; `generation.noTranscript`, `generation.busy`, `generation.notFound`, `templates.notFound`, `templates.builtIn`, `styles.notFound`, `styles.builtIn`, `styles.inUse`, `documents.notFound`, `documents.unsupportedEdit`, `documents.versionNotFound`, `documents.exportFailed`.

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
  streamed: boolean | null;  // with request: true for the local model and (2.5.0) "Your own server" (tokens follow), false for a cloud provider (the reply arrives whole)
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
- **Cloud providers.** Not streamed to the sheet: the `request` when it is sent, the `reply` whole on arrival; the sheet says "Replies from {provider} arrive whole". Their counts come from the provider's usage. 2.5.0: "Your own server" is streamed like the local model (`streamed: true`, `token` events with an estimated, growing `outputTokens`), its counts the server's usage or, without one, estimates ("2.5.0 — Your own server").

## Updates (H1)

Memento updates itself from the project's GitHub releases (Velopack's `releases.win.json` feed). The host checks once the page has sent `ui.ready` and then every 24 hours while it runs, never while a recording or a processing stage is queued, waiting or running (it waits until both are idle), downloads in the background and then offers a restart. Nothing installs until the person chooses "Restart to update", or the next time Memento starts. A pre-release is offered only to a pre-release build (a SemVer suffix, or a version before 0.5.0); from 0.5.0 on only full releases count. With `general.autoUpdate` off nothing is checked automatically; `updates.check` still works. A copy not installed with Setup (a build folder) never checks.

Manual versus automatic (2.1.0): the automatic check still downloads a newer version by itself. "Check now" (`updates.check`) only checks: a newer version becomes `state: 'available'` with `availableVersion` and `releaseNotes`, and nothing downloads until "Update now" (`updates.download`). `releaseNotes` is the release's Markdown (Velopack's `TargetFullRelease.NotesMarkdown`, the GitHub release body); the UI shows it read-only as plain text (headings, lists, paragraphs; marks, links and HTML stripped).

```ts
interface UpdateStatus {
  currentVersion: string;
  state: 'unavailable' | 'idle' | 'checking' | 'available' | 'downloading' | 'ready' | 'failed';   // failed: only after updates.check / updates.download; available (2.1.0): found by updates.check, waits for updates.download
  availableVersion: string | null;   // while available, downloading or ready, and while a download is deferred
  percent: number | null;            // while downloading
  lastCheckedAt: string | null;      // ISO 8601, when the feed last answered
  message: string | null;            // after updates.check: "You are on the newest version (2.0.0).", why it failed, or that the download waits
  deferred: boolean;                 // a newer version waits to download until no recording or processing runs
  releaseNotes?: string | null;      // 2.1.0: the release notes (Markdown) of availableVersion, when the feed has them
}
```

| Method | Params | Result | Notes |
|---|---|---|---|
| `updates.status` | `{}` | `UpdateStatus` | |
| `updates.check` | `{}` | `UpdateStatus` | "Check now": answers once the feed did. It only checks (2.1.0): a newer version is `available` with its `releaseNotes` and is not downloaded; the newest says so in `message`. A failed check is not an error: `state: 'failed'` with the reason in `message`. `updates.unavailable` for a copy that cannot update itself. |
| `updates.download` | `{}` | `UpdateStatus` | 2.1.0, "Update now": downloads the `available` version in the background (once no recording or processing runs; `deferred` meanwhile); `updates.progress` follows it to `ready`. A failed download is `state: 'failed'` with the reason in `message`. `updates.nothingToDownload` when nothing is `available`; `updates.unavailable` for a copy that cannot update itself. |
| `updates.apply` | `{}` | `{}` | "Restart to update": Memento closes normally, the update installs and Memento starts again. `updates.notReady` when nothing is downloaded, `updates.busy` while recording. |

| Event | Payload |
|---|---|
| `updates.progress` | `UpdateStatus`, on every change (state, percent). The UI shows a toast "Restart to update to {version}" when `state` becomes `ready`. |
| `status.footer` | adds `update?: { downloading: boolean, percent: number \| null, version: string \| null }` (the footer shows "Downloading Memento {version} · 42%"). |

Settings snapshot: `general` adds `autoUpdate: boolean` (default `true`), merged like the other `general` fields.

## Error codes (H1)

`library.unavailable` (the library folder chosen in Settings is missing: its drive is not connected, or it was moved or renamed; `library.list` and `recording.start` answer it and nothing is created in its place; detail: the folder. The default library is created on first run as before. Once the folder is back, the next `library.list` opens it, recovers interrupted recordings and resumes processing), `updates.unavailable` (this copy was not installed with Setup, so it cannot update itself), `updates.notReady` (`updates.apply` with nothing downloaded), `updates.busy` (`updates.apply` while recording; the update installs at the next start instead), `updates.nothingToDownload` (2.1.0: `updates.download` before Check now found a newer version; nothing was downloaded).

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


---

# Review (2.0): known voices, suggested chapters, selection mode

The deferred Review features of DESIGN.md §19, additive. The JSON is pinned by `ReviewContractSerializationTests`; parameters are read strictly (an unknown or missing field is `bridge.invalidParams`). Measurements and the thresholds: ENGINE-NOTES.md §N.

## Shared types (Review 2.0)

```ts
interface KnownVoiceInfo { id: string; name: string; recordings: number; lastConfirmedAt: string; suggest: boolean }   // never the signature
interface KnownVoicesResult { remember: boolean; voices: KnownVoiceInfo[] }        // remember: Settings › Speakers › rememberVoices; voices by name
interface VoiceRememberResult { remembered: boolean; changeId: string | null; voice: KnownVoiceInfo | null; reason: string | null }
interface VoiceMatch { speakerId: string; voiceId: string; name: string; similarity: number /* cosine 0..1 */; recordings: number }
interface ChapterSuggestion { id: string /* "sc" + atMs */; atMs: number; title: string; basis: string /* "The subject changes · 4 s pause · new speaker", or "Agenda item 2 · said 6 times in two minutes" */; fromAgenda: boolean /* 2.7.0: title and place from an agenda item */ }
```

`SettingsSnapshot.speakers` gains `rememberVoices` (default `false`; `settings.set` merges it like the other fields). The 1.x `rememberRenamed` stays in the snapshot for compatibility; it was never applied, is not shown any more and does not turn remembering on.

## Methods (Review 2.0)

| Method | Params | Result | Notes |
|---|---|---|---|
| `voices.list` | `{}` | `KnownVoicesResult` | Settings › Known voices. |
| `voices.setSuggest` | `{ voiceId, suggest }` | `KnownVoicesResult` | "Suggest this voice". A voice that is off is kept but never suggested (it still counts as the second best, so it can stop another voice's suggestion). |
| `voices.forget` | `{ voiceId }` | `KnownVoicesResult` | Removes the voice's signatures at once; not undoable, and no Undo of an earlier enrolment brings it back. |
| `voices.forgetAll` | `{}` | `KnownVoicesResult` | Deletes `voices/known.json` (also one written by a newer Memento). |
| `voices.remember` | `{ recordingId, speakerId }` | `VoiceRememberResult` | Review calls it after a rename while `rememberVoices` is on. Enrols the named speaker's voice (the mix of the voices.json voices of the lines it has now) under its name, or refines the known voice with that name and voice model; the speaker's earlier confirmation in this recording (under another name) moves to this one, and a voice left without confirmations is forgotten. Nothing changes, with `reason` saying why, when the setting is off, the speaker has no name, the recording has no voices.json, or there is under 10 s of voiced speech. `transcript.none`, `transcript.speakerNotFound`, `project.notFound`. |
| `voices.revert` | `{ changeId }` | `{}` | Undo of a `voices.remember` or `voices.acceptMatch` enrolment: the voices it touched go back as they were (keeping their Suggest switch and declines). The journal lives in memory for this run of Memento (200 changes); a change it no longer has, or one whose voice was forgotten since, answers `voices.notFound`. |
| `voices.matches` | `{ recordingId }` | `{ matches: VoiceMatch[] }` | The match prompts: unnamed speakers whose voice (10 s or more) matches a known voice at cosine 0.60 or more (0.50 when the name is one of the recording's Who spoke names or participants) and 0.10 more than any other known voice of the same model, one speaker per voice and one voice per speaker. Voices switched off, declined in this recording or already naming a speaker here are not offered. Empty while `rememberVoices` is off, without a transcript or without voices.json. |
| `voices.acceptMatch` | `{ recordingId, speakerId, voiceId }` | `{ speakers, changeId }` | "Use name": `transcript.renameSpeaker` to the voice's name, then `voices.remember` (`changeId` null when nothing was learned). Undo: `transcript.restoreSpeaker` with the old name, then `voices.revert`. |
| `voices.decline` | `{ recordingId, voiceId, declined }` | `{ matches: VoiceMatch[] }` | "Not {name}": the voice is not suggested in this recording again; `declined: false` is its Undo. Answers the recording's matches after the change. |
| `annotations.suggestChapters` | `{ recordingId }` | `{ suggestions: ChapterSuggestion[] }` | Found on the PC, no model: subject changes (two minutes of words either side of each 30-second edge), moved to the nearby line after the longest pause or with a new speaker, about one per ten minutes at most, plus the first line. Suggestions within a minute of a chapter (an accepted suggestion is a chapter) or of a dismissed one are left out. Empty for recordings under six minutes, without a complete transcript, or when nothing changes enough. 2.7.0: with an agenda, the items found in the transcript (in agenda order, where their words are said most densely, at the first line saying them) start chapters titled with the item (`fromAgenda`), replacing subject changes near them; items not found are skipped and the rest is as before. Reads only. |
| `annotations.dismissSuggestion` | `{ recordingId, atMs }` | `{ suggestions }` | Keeps `atMs` in annotations.json (`dismissedSuggestions`, an optional field read by schema v1; an older Memento keeps it as an unknown field), at most 200. A negative time is `bridge.invalidParams`. |
| `annotations.restoreSuggestion` | `{ recordingId, atMs }` | `{ suggestions }` | The Undo of a dismiss. |
| `transcript.setSegmentsSpeaker` | `{ recordingId, segmentIds, speakerId: string \| null, newSpeakerName?: string }` | `{ segments, speakers }` | Selection mode: 1–100,000 lines to one speaker (a new one with `newSpeakerName`, which counts as named) in one write; `segments` are those lines as they are now, in transcript order. Every line must exist (`transcript.segmentNotFound`, `detail` its id) and the speaker too (`transcript.speakerNotFound`), or nothing changes. History: "Speaker changed" with the line count. Undo groups the lines by the speaker they had and sets each group back (and removes a speaker it created with `transcript.removeSpeaker`). |

Accepting a suggested chapter is `annotations.addChapter` with `origin: 'local'`; Accept all is one per suggestion and one Undo step. Selection mode's Highlight is `annotations.addHighlight` per line without one (with its `segmentId`), Remove highlights `annotations.removeHighlight`, Mark as chapter start `annotations.addChapter` at the first selected line titled with its first words, and Copy `transcript.copy` with the lines' `segmentIds`; each action is one Undo step.

## Error codes (Review 2.0)

`voices.notFound` (a `voices.*` method named a known voice or an enrolment change Memento does not have; `detail` is the id), `voices.newerVersion` (`voices/known.json` was written by a newer Memento; it is left as it is and nothing changed; Forget all still deletes it).

---

# 2.0 — Live transcript, tray and keep only the mix

Decided with the 2.0 build of these deferred items (ROADMAP.md 2.0.0). Contracts: `LiveBridgeJsonContext` (host) and `ui/src/bridge/types.ts`; strict JSON pinned by `TrayAndStartupTests`, `LiveTranscriptContractTests` and `KeepOnlyMixTests`.

## Tray and Start with Windows (2.0)

Settings › General. Both off by default; nothing new leaves the PC.

```ts
general: {                                  // M3 block, two read-only fields added
  startWithWindows: boolean; keepRunningInTray: boolean; language: 'en'; autoUpdate: boolean;
  startWithWindowsAvailable: boolean;       // read only: only an installed copy (Velopack's install folder) may start with Windows
  startWithWindowsNote: string | null;      // read only: why not, as a sentence; null when available
}
```

`settings.set` accepts the two read-only fields (so the UI can send the whole block back) and ignores them. With `keepRunningInTray` on, the host shows a notification-area icon with Open Memento, New recording (Show the recording while one is in progress) and Quit Memento; closing the window hides it, and recording and processing go on. While a recording is in progress the icon carries an accent dot, and its tooltip and the first menu line give the recording's time and say it is saved as it records (DESIGN.md §17 wording; `Memento.Core.Host.TrayView`). Quit while recording stops the recording as closing Memento always has (everything so far is kept and finalized at the next start). Starting Memento again (the single-instance guard) shows the hidden window. Signing out of Windows closes Memento even with the option on.

`app.setStartup { startWithWindows: true }` (and `settings.set` with `general.startWithWindows: true`) on a copy that was not installed answers `app.startupRefused` with `startWithWindowsNote` followed by "Nothing was changed; install Memento with its installer and turn it on there."; turning it off always works. An installed copy registers the current user's `Run` value `Memento` = `"<install>\Memento.exe" --background` (Velopack's launcher, which survives updates). `--background` starts Memento minimised, or only in the tray when `keepRunningInTray` is on; a second `--background` start while Memento runs does nothing.

| Event | Payload | Notes |
|---|---|---|
| `app.openScreen` | `{ screen: 'record' }` | The tray's Record item, after the host has shown the window: the page opens the Recording session, rejoining the session in progress (recording or paused) or ready to record. |

## Live transcript (2.0)

Settings › Transcription › Live transcript while recording is `transcription.timing: 'during'` (off by default; the full pass after Stop always runs and replaces the draft). `transcription.liveOnGpu: boolean` (default `false`) lets it use the graphics card; `settings.set` merges it like the other fields.

```ts
interface RecordingLiveTranscriptPayload {
  sessionId: string;
  segments: { start: number; end: number; text: string }[];   // the newest 100 lines of the draft so far, seconds on the recording timeline
  state: 'starting' | 'listening' | 'paused' | 'unavailable' | 'failed';
  engine: string | null;   // "Local · CPU · Small", "Local · GPU · Small"; null before the model is loaded
  note: string | null;     // a §17 sentence for paused (recording paused; waiting for a full pass; turned off in Settings), unavailable, failed
}
```

Sent while a session records or is paused and the setting is on: after every heard window, on every state change, and again every few seconds while nothing changes (a page that reloads catches up). Each payload replaces the last. The lines are provisional: the page marks them so, they are never written to the project, and they are dropped when the session ends. `unavailable` when neither Small nor Base is installed (the note says to install one; Turbo, Medium and Large are never used for it); `failed` when its worker stopped twice (recording is not affected). The worker protocol gains the job kind `live` (`LiveJob`), the host line `audio` (`LiveAudio`: window, start, 16 kHz mono int16 base64) and the worker line `heard` (window, segments).

## Keep only the mix (2.0)

`recording.storage.keepOnlyMix` (Settings › Recording, off by default) now does what it says: the `optimize` stage runs when the codec is AAC or MP3 **or** this is on, after every other stage. With a smaller codec only the mix is converted; then the mix is checked against the SHA-256 in `project.json` and, only if it matches, every separate track file is removed. The manifest records it first (`project.json` `mixOnly: { at, trackIds, bytesFreed }`, an optional field within schema 3; the tracks stay listed without a hash, `integrity.files` keeps only the mix), then the files go, then History gets an `optimize` `completed` line "Kept only the mix · removed 2 separate tracks (812 MB)" whose detail says what is no longer possible. A missing or mismatched mix keeps every track with an `optimize` `info` line "Kept the separate tracks" saying why and "Nothing was removed". A recording that keeps only its mix is transcribed again from the mix as one track (`mix`).

Settings saved before 2.0 never turn it on: `settings.json` schema 2 starts `recording.storage.keepOnlyMix` off and `transcription.timing` at `after` when the file is schema 1, because both were stored while they did nothing.

```ts
interface Project { /* … */ mixOnly: { at: string; tracks: number; bytesFreed: number } | null }   // null while the tracks are kept
interface LibraryUsage { /* … */ separateTracksBytes: number; separateTracksRecordings: number; mixOnlyRecordings: number }
```

| Method | Params | Result | Notes |
|---|---|---|---|
| `storage.keepOnlyMix` | `{ recordingIds: string[] \| null }` | `{ jobId }` | Settings › Storage and history › Keep only the mix for existing recordings, after the page's confirmation (size, what is kept, cannot be undone). `null`: every stored recording that still has separate tracks and a mix. Each recording goes through the stage step above (mix checked first); busy ones are skipped. Progress and the final message through `storage.reclaimProgress` ("2 recordings keep only the mix; 1.2 GB freed. Transcripts, speakers and the mixes were not changed."). `storage.nothingToReclaim` for `[]` or when no recording has separate tracks; `project.notFound` for an unknown id; `library.busy` during a move or another reclaim. |

`export.estimate` lists `tracks` as unavailable with the reason "Only the mix was kept", and the Export dialog leaves the Individual tracks row out; Review's People pane offers Reduce but not Identify speakers again (there is nothing per track to listen to); `library.usage` counts what separate tracks use.

# 2.0 — Tesseract, the follow-up email and the preview sheet

## Tesseract for agenda photos and scans (2.0)

Settings › Documents › Agenda photos and scans. The model manager's `tesseract-eng` (`engine: 'ocr'` in `models.list`) is installed, verified and removed like every other model (`models.install`, `models.progress`, `models.remove`); nothing new on the bridge besides the setting.

- `settings.get` / `settings.set`: `documents.ocrEngine: 'windows' | 'tesseract'`. The snapshot names the engine **in effect**: `tesseract` only while its language data is installed (removing it puts Windows OCR back without changing the stored choice). `settings.set` takes `'windows'`, `'tesseract'` or `null` (Windows OCR); `'tesseract'` while `tesseract-eng` is not installed, or any other value, is `settings.invalidValue` with `detail: 'documents.ocrEngine'`, and nothing is written.
- `agenda.importFile` / `agenda.importDropped` read images and scanned PDFs with the chosen engine first and the other as the fallback (unchanged `AgendaParsePreview.ocrEngine`: `"Windows OCR"` or `"Tesseract"`). With Tesseract, words under 0.6 confidence mark their item uncertain (`uncertainReason`: the word Tesseract was unsure of), as well as the misread-character checks both engines share; the review notice says unsure words are marked.
- Failures keep the `agenda.*` codes: `agenda.ocrUnavailable` when the language data is missing or damaged (it is checked against its SHA-256 before use) or Memento.Worker cannot start; `agenda.unreadable` when Tesseract stops while reading. Nothing is imported either way.

## Follow-up email (2.0)

```ts
type EmailTone = 'neutral' | 'warm' | 'brief';
interface EmailModuleSettings { tone: EmailTone; signOff: string | null }   // signOff: one line, ≤ 80 characters
interface ModuleSettings { …M4; email?: EmailModuleSettings | null }        // followUpEmail only; the host always sends it for that module
interface DocumentModuleParams { recordingId: string; documentId: string; moduleId: string }
```

- `templates.save`, `generation.preview`, `generation.start` take `email` on a `followUpEmail` module (left out or `null`: neutral, unsigned; on another module it is ignored). An unknown tone or a sign-off of more than one line or 80 characters is `bridge.invalidParams` with `detail` `rows[r].modules[i].email.tone` / `.email.signOff`.
- The generated module is ordinary blocks: a label/value pair (To: the participants, `; `-separated, or a note that none were listed; Subject: "Follow-up: {title} ({date})", brief "Recap: {title}"), the greeting, the opening, the recap (its own map pass, verified), **Decisions**, **Action items** ("{Owner}: {task}, {due}" or "{task} (no owner named)"), "No decisions or action items were recorded." when there are none, the closing and the sign-off. Words in the tone only change those fixed sentences.

| Method | Params | Result | Notes |
|---|---|---|---|
| `documents.copyModule` | `DocumentModuleParams` | `DocumentCopyResult` | One module on the clipboard (host clipboard, as `documents.copy`): plain text without Markdown marks or timestamps (`\r\n` lines, "To: …", "- " items) and HTML of its blocks in the document's style without the module heading or timestamps, so a Follow-up email pastes into Outlook as the email. `documents.notFound`; `bridge.invalidParams` (`detail: 'moduleId'`) for a module the document does not have; `clipboard.unavailable`. Reads only. |

## Preview what will be sent (2.0)

```ts
interface PreviewProviderInfo { id: ProviderId; name: string; kind: 'cloud' | 'local' | 'server'; modelLabel: string | null }   // 'server': 2.5.0, modelLabel "qwen3:8b at localhost:11434"
interface PreviewInput { key: keyof InputSelection | 'instructions'; label: string; ticked: boolean; included: boolean;
                         characters: number; bytes: number; summary: string | null; note: string | null }   // note: "Held back: …", "Nothing to send: …", "Left out: …"
interface PreviewPass { id: string /* p1… */; step: 'map' | 'verify'; title: string; system: string; user: string;
                        characters: number; bytes: number; tokens: number /* about */; exact: boolean }
interface GenerationPreviewResult { …M4; provider: PreviewProviderInfo; askBeforeSend: boolean; inputs: PreviewInput[]; redactions: string[]; passes: PreviewPass[] }
interface GenerationCopyPreviewParams { recordingId: string; template: Template; passId?: string | null }
interface PreviewCopyResult { characters: number; passes: number }
```

- `passes` are the **map requests exactly as a generation sends them** (same tasks, chunks, system prompts, user turns and Live output titles; the retries on half a chunk and the plain re-reads happen only after an answer, so they are not listed), then one `verify` request with `exact: false` that illustrates the checks built from the answers. The grammar or JSON schema that constrains an answer is not repeated, as in Live output.
- `inputs` lists the seven checklist inputs in Builder order (and the template's instructions when it has some), ticked or not, with the size of their text in the payload. `redactions` says what is held back by the share switches, what was left out and why, and how many section-marker look-alikes were neutralised. `askBeforeSend` is Settings' switch (it holds cloud jobs only).

| Method | Params | Result | Notes |
|---|---|---|---|
| `generation.copyPreview` | `GenerationCopyPreviewParams` | `PreviewCopyResult` | Puts the preview on the clipboard as text (host clipboard): the provider, the inputs with their sizes, what is held back and "Audio and video are never sent.", then each request as `=== {title} ===`, `System`, `User`; with `passId`, that request only. The preview is composed again, so the copy matches the template as sent. `bridge.invalidParams` (`detail: 'passId'`) for an unknown pass; `clipboard.unavailable`. Nothing is sent and no provider is constructed. |

# Video (2.0)

The contract of the 2.0 video host (ENGINE-NOTES.md §M, ARCHITECTURE.md §5.x) and the video screens (DESIGN.md §18): the spike's proposal, with what the screens needed added while they were built against the browser-preview host (`ui/src/bridge/mockVideo.ts`); `ui/src/bridge/types.ts` has each shape exactly as written here. Host contracts: `VideoBridgeJsonContext` (`src/Memento.Core/Bridge/Contracts/`), strict JSON pinned by `VideoContractTests`. Video never leaves the PC: none of these methods sends anything anywhere, and no frame or thumbnail is ever given to an AI provider (the AI payload composer has no video input at all).

## Shared types (2.0 video)

```ts
type VideoSourceKind = 'display' | 'window' | 'camera';
type VideoRole = 'screen' | 'camera';                 // a display or a window is the screen track
type PipCorner = 'topLeft' | 'topRight' | 'bottomLeft' | 'bottomRight';
interface VideoSource {
  id: string;                 // 'display:\\.\DISPLAY1', 'window:1A2B3C', 'camera:<symbolic link>', 'camera:dshow:<device path, filter CLSID or name>'; stable while Memento runs
  kind: VideoSourceKind;
  name: string;               // "Display 1", the window title, the camera's name
  detail: string;             // "2560 × 1440 · 60 Hz · primary", "Zoom · 1600 × 900", "This app is excluded from capture"
  width: number; height: number; refreshRate: number;   // physical pixels; 0 for a camera (not known without opening it)
  primary: boolean;
  excluded: boolean;          // listed, not selectable: Memento's own windows and windows that keep themselves out of captures
  backend: 'mf' | 'dshow' | null;   // a camera: 'mf' (Media Foundation: webcams, capture cards Windows lists as cameras) or
                              // 'dshow' (DirectShow: the OBS Virtual Camera, NDI and other virtual or DirectShow-only cameras;
                              // the page notes "via DirectShow"); null for a display or a window
  // The host also sends protected: boolean and processName: string | null (a window's program); the page may ignore them.
}
type VideoTrackState = 'recording' | 'paused' | 'waiting' | 'ended' | 'stopped';   // paused = the recording is paused; waiting = window minimised, display disconnected, camera busy
interface VideoTrackStatus {  // DESIGN.md §18.1 Encoder card, video lane and footer
  trackId: VideoRole;
  state: VideoTrackState;
  sourceId: string;           // the source the track captures now
  sourceName: string;
  reason: string | null;      // the §18.6 sentence while waiting or after it ended
  encoder: string;            // "NVIDIA H.264 Encoder MFT", "Microsoft H.264 software encoder"
  hardware: boolean;          // "Hardware · GPU" or "Software · CPU"
  width: number; height: number; frameRate: number;
  targetBitsPerSecond: number; actualBitsPerSecond: number;
  framesWritten: number; framesDropped: number;
  bytesWritten: number; durationMs: number;
}
interface PipState { enabled: boolean; corner: PipCorner }
interface VideoSettings {     // Settings › Recording › Video defaults (§18.5): recording.video
  defaultSource: 'screen' | 'window' | 'camera' | 'screenAndCamera';   // screenAndCamera
  resolution: 'display' | '1080p' | '720p';   // display
  frameRate: 24 | 30 | 60;                     // 30
  quality: 'smaller' | 'balanced' | 'best';    // balanced: about 6, 12, 20 Mb/s at 1440p 30 fps (scaled by pixel rate^0.75)
  defaultCameraId: string | null;              // null: the first camera
  pipCorner: PipCorner;                        // bottomRight
  hardwareEncoder: boolean;                    // true; falls back to software automatically, with a notice
  includePointer: boolean;                     // true
}
type VideoProblemCode =       // recording.videoProblem's closed list; the four marked * are states, not error codes
  | 'video.displayDisconnected' /* * */ | 'video.windowClosed' /* * */ | 'video.windowMinimised' /* * */ | 'video.encoderFallback' /* * */
  | 'video.protectedContent' | 'video.diskFull' | 'video.cameraInUse' | 'video.cameraPrivacy' | 'video.captureBlocked'
  | 'video.sourceNotFound' | 'video.encoderUnavailable';
interface RecordingVideoSummary {   // RecordingSummary.video; null when the recording never had video
  roles: VideoRole[];               // the tracks the recording has (had, once removed)
  height: number;                   // the screen track's, else the camera's: "1440p" on the grid card
  removedAt: string | null;         // Remove video ran: hasVideo is false, the row shows "Video removed · audio kept"
}
interface ProjectVideo {            // Project.video; null when the recording never had video
  tracks: ProjectVideoTrack[];      // screen first; [] once removed
  pip: PipState;                    // where the inset was when recording ended; Review starts there
  sizeBytes: number;                // every video file of the recording
  removed: { at: string; bytesFreed: number } | null;
}
interface ProjectVideoTrack {
  id: VideoRole;
  sourceKind: VideoSourceKind; sourceName: string;   // the last source when it was switched
  width: number; height: number; frameRate: number;
  encoder: string; hardware: boolean;
  framesWritten: number; framesDropped: number; sizeBytes: number;
  startOffsetMs: number;            // where the track begins on the recording timeline (the file itself starts at 0, §M.3)
  durationMs: number;
  endReason: string | null;         // the §18.6 sentence when it ended before the recording ("“Zoom Meeting” was closed at 0:58:12.")
  parts: VideoPart[];
}
interface VideoPart {               // a file written by one encoder (a hardware failure or a quality change starts a new one)
  url: string | null;               // https://library.memento/<id>/tracks/screen.mp4; null while unreadable (recording, being repaired or joined) or removed
  startOffsetMs: number; durationMs: number;
}
interface RecoveredVideoTrack { trackId: VideoRole; sourceName: string; recoveredDurationMs: number }
type VideoExportFormat = 'mp4' | 'original';
type VideoExportLayout = 'oneFile' | 'twoFiles';
interface ExportVideoChoice { on: boolean; format: VideoExportFormat; layout: VideoExportLayout }
```

Additions to existing types:

```ts
type StageName = 'stored' | 'video' | 'transcript' | …;
interface RecordingSummary { /* … */ video: RecordingVideoSummary | null }
interface Project { /* … */ video: ProjectVideo | null }
interface RecordingStatePayload { /* … */ videoTracks: VideoTrackStatus[] }   // host addition: [] without video; recording.current carries it
interface ProcessingCurrent { /* … */ failure: StageFailure | null }           // library.processing: the video stage failed
interface RecoveredRecording { /* … */ videoTracks: RecoveredVideoTrack[] }    // recovery.list items; [] without video
interface LibraryUsage { /* … */ videoBytes: number; videoRecordings: number } // every video file; recordings that still have video
interface ExportSelection { /* … */ video?: ExportVideoChoice | null }         // missing or null: off
interface SourcesListResult { audio: AudioSource[]; videoAvailable: boolean }  // the type was the literal false
interface RecordingSettings { /* … */ captureVideo: boolean; video: VideoSettings }   // captureVideo: Recording mode, false = Audio only (the product default)
interface StorageReclaimSettings { reclaimOlderThanDays: number | null; removeVideoOlderThanDays: number | null }   // null = Never, else 30, 90, 180 or 365
```

## Methods (2.0 video)

| Method | Params | Result | Notes |
|---|---|---|---|
| `video.listSources` | `{}` | `{ displays: VideoSource[], windows: VideoSource[], cameras: VideoSource[] }` | Re-enumerates without opening anything (no camera light, no capture border). Displays are named "Display 1" (the primary) then left to right. Cameras are Media Foundation's capture devices, then the DirectShow video input devices Media Foundation does not list (ENGINE-NOTES.md §R): a camera both see is listed once, as `mf`. While the page keeps calling it (at least once a minute) the host lists again every 2 s and sends `video.sourcesChanged` when something came or went. |
| `video.thumbnail` | `{ sourceId, width }` | `{ dataUrl }` | One JPEG still (`data:image/jpeg;base64,…`), `width` 16–640 (CSS pixels × device pixel ratio; 320 when left out); the host also answers `width` and `height`. Cheap enough to poll: the page asks for the 560 px live preview every 500 ms and for each row thumbnail and the inset every 1000 ms, only while the Recording screen is visible and the source is on. A display or window is read with GDI (no capture session, no border; 81 ms for 320 px of a 1080p display); a camera is opened on the first request and kept open while requests keep coming, closed 3 s after the last (and when the app closes); a camera being recorded answers the track's latest picture. A `dshow` camera is opened through a DirectShow graph at up to 1280 × 720 (the OBS Virtual Camera: 86–172 ms for the first, 16–52 ms for the next); a virtual camera whose app is not sending shows what its filter shows (OBS's placeholder). Never written to disk. Errors: `video.sourceNotFound`, `video.protectedContent` (nothing is read), `video.captureBlocked`, `video.cameraPrivacy`, `video.cameraInUse`; `bridge.invalidParams` for a width out of range or an id that is not a video source. |
| `recording.setVideoSource` | `{ sessionId, role: VideoRole, sourceId: string \| null }` | `{ videoTracks: VideoTrackStatus[] }` | Starts the role's track, switches it to another source of the role's kind ("You can change this while recording; the track continues", §18.2: the track keeps its size and the new picture is letterboxed into it), or with `null` ends it. A role whose track ended (window closed, camera unplugged) starts a new track from now (stored as `screen-2` in `project.json`; still `screen` here). Errors: the video error codes (nothing changed; the other tracks and the audio are untouched), `recording.noSession`, `bridge.invalidParams` (a role that is not `screen` or `camera`, or a source of the other role's kind). |
| `recording.setPip` | `{ sessionId, enabled, corner }` | `{}` | The camera inset from now on (`corner` may be left out to keep it). Both tracks are always recorded; the live preview and Review draw the inset, and the export's one file burns it in where it was (each change is kept with its time, `project.json` `videoPip[]`). The page sends it straight after a start that has both tracks. `bridge.invalidParams` for another corner. |
| `recording.setVideoQuality` | `{ sessionId, quality: VideoSettings['quality'] }` | `{ videoTracks: VideoTrackStatus[] }` | The §18.6 encoder banner's **Lower quality**. The running tracks continue at the new quality's bitrate: each closes its file and goes on in a new part (`tracks/screen.part2.mp4`, joined by the video stage), so nothing recorded is touched. `recording.noSession`; `bridge.invalidParams` for another quality. |
| `video.openPrivacySettings` | `{ which: 'camera' \| 'screenCapture' }` | `{}` | Opens Windows Settings at `ms-settings:privacy-webcam` or `ms-settings:privacy-graphicscaptureprogrammatic`. `app.openExternal.failed` when Windows did not open it. |
| `video.remove` | `{ recordingId }` | `{ freedBytes }` | The §18.6 Remove video dialog's action, after the page's confirmation. Deletes every video file of the recording, keeps audio, transcript and documents, records `videoRemoved` in `project.json` before deleting (then `hasVideo` is false), and writes History `video` `completed` "Removed the video · 1.9 GB freed". Errors: `project.notFound`, `project.recording` (still recording or being saved), `library.busy` (its video stage runs now), `bridge.invalidParams` (nothing to remove: no video, or already removed). |
| `video.removeEstimate` | `{ recordingIds: string[] \| null, olderThanDays: number \| null }` | `{ recordings: number, bytes: number }` | What Remove video would free: the named recordings, or (ids null) every recording with video older than `olderThanDays` (null: every one). Feeds "Would free 11.3 GB now, from 3 recordings" and the Remove video dialog. Reads only. |

`recording.start` sorts its `sourceIds` by prefix: `display:` and `window:` start the `screen` track, `camera:` the `camera` track (one source per role, the first of each). At least one audio source is still required (`recording.noSources`): a recording of video alone is not designed (DESIGN.md §20). A video source that cannot start never keeps the audio from starting: the recording starts and `recording.videoProblem` says why. A camera another app holds is tried again every 2 s and its track starts by itself the moment the camera is free. The remembered `recording.defaultSourceIds` keep only the audio ids.

## Events (2.0 video)

| Event | Payload | Notes |
|---|---|---|
| `recording.videoStatus` | `{ sessionId, videoTracks: VideoTrackStatus[], pip: PipState }` | Once a second while a session with video records, beside `recording.state`, and after every `recording.setVideoSource` and `recording.setVideoQuality`. A track that is off is left out of `videoTracks`; an `ended` one stays listed until it is replaced or the session ends. `pip` is the inset as the session has it, so a page that rejoins draws it where it is. |
| `recording.videoProblem` | `{ sessionId, trackId: VideoRole, state: VideoTrackState, code: VideoProblemCode, sourceId, sourceName, detail: string \| null, message, atMs }` | The §18.6 toast, banner or card, by `code` (the page writes the sentence; the host's `message` is the fallback for a code it does not know). Audio is never affected. A problem is over when the track's `recording.videoStatus` state returns to `recording`; no event says so separately. |
| `video.sourcesChanged` | `{}` | A display, window or camera came or went while the page was listing sources; the page lists again. |

| `code` | `state` | `detail` | When |
|---|---|---|---|
| `video.displayDisconnected` | `waiting` | null | The captured display went away; the track waits for `recording.setVideoSource` with another display (or for it to come back). |
| `video.windowClosed` | `ended` | null | The captured window closed; the track ends there. |
| `video.windowMinimised` | `waiting` | null | The captured window was minimised; frames resume by themselves when it is restored. Footer only, no toast. |
| `video.protectedContent` | `ended` | null | The captured window started keeping itself out of captures (ENGINE-NOTES.md §M.1). |
| `video.encoderFallback` | `recording` | the software encoder's name | The hardware encoder failed; the track continues in software (a new part). |
| `video.diskFull` | `stopped` | null | Video stopped first because the drive is nearly full (below 1 GB); audio continues until its own 256 MB floor. |
| `video.cameraInUse` | `waiting` | the other app, or null when Windows does not record which | The camera track starts by itself the moment the camera is free. |
| `video.cameraPrivacy` | `stopped` | the switch that is off ("Let desktop apps access your camera") | |
| `video.captureBlocked` | `stopped` | null | Windows privacy settings block screen capture (or this Windows session cannot capture at all). |
| `video.sourceNotFound` | `ended` | null, or the app feeding a virtual camera ("OBS") | The display, window or camera is not there (any other loss, a camera unplugged, a file that could not be written, a camera format Memento can't read), or a virtual camera's app stopped sending ("OBS Virtual Camera stopped at 0:42: OBS is no longer sending it a picture. …"; refused at the start with "… is not started in OBS …"). |
| `video.encoderUnavailable` | `stopped` | null | No H.264 encoder (or graphics device) could be opened for the track; audio continues. |

The low-disk banner's "about 25 minutes of video at this quality" is the page's own arithmetic: free bytes over the audio tracks' rate plus the video tracks' `targetBitsPerSecond`.

## Settings snapshot (2.0 video)

`recording` (replaced whole, like the rest of the block) gains `captureVideo` and `video: VideoSettings`; a field left out takes its default. Settings saved before 2.0 read `captureVideo: false` and `video` at its defaults (`screenAndCamera`, `display`, 30, `balanced`, null, `bottomRight`, `true`, `true`). `storage` gains `removeVideoOlderThanDays`, merged field by field like `reclaimOlderThanDays` (whole days 1–3650; JSON `null` clears it to Never; left out, it keeps its value). Bad values answer `settings.invalidValue` and change nothing. Video defaults apply to the next recording.

With an age set, the host removes the video of recordings older than that (each with its History line, `videoRemoved.reason: 'age'`) once the page is ready, then once a day, and at once when the age is changed (the Settings confirmation); never while a recording runs or processing works.

## Library, projects and stages (2.0 video)

- **Timeline.** Each track is laid on the audio session's QPC timeline (§5.x): `startOffsetMs` places it (the MP4 starts at 0), a pause leaves the paused time out of the video exactly as out of the audio. Review plays the video slaved to the mix: video time = mix time − the part's `startOffsetMs`.
- **Files.** `tracks/screen.mp4`, `tracks/camera.mp4` (fragmented MP4, H.264, playable as written; `.part2.mp4` after an encoder change or a quality change, `.joined.mp4` after the video stage joined them), named in `recording.state.json` `videoTracks[]` at every checkpoint. `project.json` schema 4: `videoTracks[]`, `videoPip[]`, `videoRemoved` and `hasVideo`. 1.x builds keep all of it untouched. Every URL is served by `https://library.memento/`, which answers range requests (206), so the files seek.
- **Stage `video`.** Listed only for recordings with video, between `stored` and `transcript`; it runs beside transcription (after the transcript stages, never holding them back) and, finished, is left out of rows like `stored` and `optimize`. It joins a track's parts (copied without decoding, or encoded again when an encoder change left parts that cannot be copied together) and hashes every video file. Labels name the sub-step: "Checking", "Joining 42%", "Encoding 42% · CPU"; done "Done"; failed "Video failed". A failure carries `StageFailure` remedies `software` ("Retry with software encoder": the parts are encoded again) and `keepRaw` ("Keep raw frames": the parts stay as written, each hashed, and the stage is marked done), retried with `processing.retry { stage: 'video', remedyId }`; a retry with no remedy runs the stage again. History: `video` `started` "Checking the video", `completed` "Video checked" (size, frame rate, length, size on disk, encoder, dropped frames, ended early), `failed` "Video failed", and `completed` "Removed the video · … freed".
- **`library.processing`**: `current.failure` is set when the `video` stage failed (`message` "Finalising the video file stopped at 71%: …", `kept` "The raw frames and all audio tracks are safe, and the transcript was not affected.", the two remedies). Such a recording stays `current` until a remedy is chosen, even when no other stage is active or queued.
- **Recovery.** At launch every part named in `recording.state.json` (or the manifest) has a torn last fragment cut off and its length read back; `recovery.list` items carry `videoTracks` so the dialog says how much video came back.

## Export (2.0 video)

`ExportSelection.video` (`ExportComponent` `'video'`). `oneFile` writes `{base}.mp4`: the screen with the camera inset composed where it was while recording (`videoPip[]`), with the mixed audio as AAC 192 kb/s, encoded again (H.264 at the recorded track's bitrate, composed on the processor); a camera-only recording gives the camera with the mix. `twoFiles` writes `{base} - screen.mp4` with the mixed audio and `{base} - camera.mp4` alone, each at its own resolution; a recording with one video track gives the one file that exists, named by its role. `original` copies the recorded frames into an ordinary MP4 without encoding again (a remux, with the mix as AAC), so it is only offered with `twoFiles`; `oneFile` with `original` answers `bridge.invalidParams`. A gap before a track's first frame or after it ended is black in an MP4 (re-encoded) file, so the picture stays on the audio's timeline; a copied file keeps its start offset. `export.estimate` lists one or two `component: 'video'` items (re-encoded sizes are estimates near the recorded size, plus the audio) or `video` as unavailable with "No video" or "Video was removed". `export.progress` moves within a video file; cancel removes the partial file. File names are in `ui/src/format/export-naming.cases.json` (kind `video`, part `combined`, `screen` or `camera`).

## Error codes (2.0 video)

`video.sourceNotFound` (the display, window or camera is not there; also when this copy of Memento cannot record video), `video.protectedContent` (the window keeps itself out of captures), `video.captureBlocked` (Windows privacy settings block screen capture, or this Windows session cannot capture), `video.cameraPrivacy` (a camera privacy switch is off; the message names which), `video.cameraInUse` (another app holds the camera; named when Windows records which), `video.encoderUnavailable` (no H.264 encoder or graphics device could be opened), `video.diskFull` (video stopped first; audio continues). The four state codes of `recording.videoProblem` (`video.displayDisconnected`, `video.windowClosed`, `video.windowMinimised`, `video.encoderFallback`) are never a method's error.

---

# After 2.0.0-beta.1 — Even out source levels

A quiet microphone beside loud system audio was missed by the transcript (ENGINE-NOTES.md §Q). The transcript and the live draft now judge and hear every track at its own level, which needs nothing on the bridge; the mix gets a setting and Review an action. Contracts: `BridgeJsonContext` (host) and `ui/src/bridge/types.ts`; strict JSON pinned by `EvenLevelsContractTests`.

## Settings snapshot (even levels)

`recording.storage` gains `evenLevels: boolean` (Settings › Recording › Storage, "Even out source levels"; **on** by default, and a settings file from an earlier build reads it on). It applies when a recording's mix is built (finalize): each track's speech level is measured against its own noise floor, and a track more than 3 dB under the loudest one is lifted to it, by at most 24 dB, never turned down; the soft limiter keeps the sum below full scale. Only `mix.flac` changes; the tracks are never touched. Like the other storage fields it is replaced with the `recording` block; left out, it takes its default (on).

## Methods (even levels)

| Method | Params | Result | Notes |
|---|---|---|---|
| `project.rebuildMix` | `{ recordingId: string; evenLevels: boolean }` | `{}` | Review › More › Reprocess › "Rebuild the mix with evened levels" (`evenLevels: true`), or "Rebuild the mix as recorded" (`false`) once it was evened: the way back. Writes the request into `project.json` (`mixRebuild: { evenLevels, requestedAt }`) and queues the `mix` stage, which mixes the stored tracks again into `mix.rebuilt.flac` (verified, hashed) and `mix.rebuilt.peaks.json`, then replaces `mix.flac` and `peaks.json`, records the new hash and the gains in the manifest, and writes History: `mix` `started` "Rebuilding the mix with evened levels", `completed` "Rebuilt the mix with evened levels" (detail: "levels evened: Microphone lifted 24.0 dB (speech at −53.9 dBFS) to match System audio (−29.8 dBFS) · the tracks and the transcript were not changed. To undo it, choose Rebuild the mix as recorded in Review › More › Reprocess."), or `failed` "Rebuilding the mix failed" with a `StageFailure` ("Rebuilding the mix stopped: …", kept "The current mix and every track are unchanged.", remedy `retry`). Progress through `processing.progress` (stage `mix`, labels "Mixing 42%"). A mix that had been made smaller (AAC, MP3) is replaced by the lossless one and `optimize` is queued again when Settings still asks for a smaller format. Once the stage has run, `mixUrl` and `peaksUrl` carry `?v=<first 8 hex of the mix's SHA-256>` so the player and the waveform load the new files. `project.recording` while the recording is still being recorded or stored; `project.mixUnavailable` when it keeps only its mix or a track file is missing; `project.notFound`. |

```ts
interface Project { /* … */ mixLevels: { at: string; tracks: { trackId: string; levelDbfs: number | null; gainDb: number }[] } | null } // null: the mix sums the tracks as recorded
```

`project.json` (optional fields within schema 4, ignored by earlier builds): `mix.evenLevels: { at, tracks: [{ trackId, levelDbfs, gainDb }] }` when the mix was built with evened levels (`levelDbfs` `null` for a track without speech; `gainDb` 0 when it was left as recorded), and `mixRebuild` while a rebuild is pending. The `stored` History line's detail says what was evened ("levels evened: Microphone lifted 24.0 dB …").

## Error codes (even levels)

`project.mixUnavailable` (the mix cannot be built again: the recording keeps only its mix, or a track file is not in its folder; nothing was changed).

---

# 2.1 — Folders, dates and Delete audio

Three additions for 2.1: a date format chosen in Settings (new titles are dated with it), Library folders beside the timeline and type filters, and Delete audio (by hand and by age), built on the Remove video pattern. Contracts: `LibraryBridgeJsonContext` (host) and `ui/src/bridge/types.ts`; strict JSON pinned by `FolderTests`, `AudioRemovalTests` and `DateFormatTests`.

## Settings snapshot (2.1)

```ts
type DateFormat = 'yearMonthDay' | 'dayMonthYear' | 'monthDayYear' | 'iso' | 'windows';
interface GeneralSettings { /* … */ dateFormat: DateFormat; libraryView: 'timeline' | 'folders' }
interface StorageReclaimSettings { /* … */ deleteAudioOlderThanDays: number | null } // null = Never, else 1, 7, 30, 90, 365, 730, 1095, 1460 or 1825
```

- `general.dateFormat` (Settings › General › Date format; default `yearMonthDay`): `2026 February 16`, `16 February 2026`, `February 16, 2026`, `2026-02-16`, or the long date of Windows' region settings. It is how every full date reads: the date in a new recording's title, the Library (a date in another year), Review's header and details, History and the documents' meta line and running header. Times and timecodes are unchanged; a title already given is never rewritten. Merged field by field like the rest of `general`; an unknown preset answers `settings.invalidValue`.
- `general.libraryView` (default `timeline`): the Library's Folders / Timeline switch, remembered for this Windows user. `settings.invalidValue` for anything else.
- `storage.deleteAudioOlderThanDays` (Settings › Storage and history › Delete audio tracks older than; default Never): merged like `removeVideoOlderThanDays`; exactly the nine ages above (a day, a week, a month, 90 days, a year, two to five years), JSON `null` for Never; any other value answers `settings.invalidValue` and changes nothing. With an age set, the host deletes the audio of recordings older than that, each with its History line (`audioRemoved.reason: 'age'`), once the page is ready, then once a day, and at once when the age is changed (the Settings confirmation comes first); never while a recording runs or processing works. It is the same loop as Remove video older than.

`recording.start` with an empty title names the recording "Untitled {type} · {date}" in `general.dateFormat` ("Untitled meeting · 2026 February 16"); the Record screen sends the same title itself.

## Shared types (2.1)

```ts
interface RecordingSummary { /* … */ folder: string | null; audioRemoved: AudioRemovedInfo | null }
interface Project { /* … */ audioRemoved: AudioRemovedInfo | null } // then mixUrl and peaksUrl are null
interface AudioRemovedInfo { at: string; bytesFreed: number }
interface FolderInfo { path: string; name: string; parent: string | null; count: number; totalCount: number }
```

A **folder path** is names joined by `/` ("Clients/Acme"); `null` is the root (no folder). A name is 1–80 characters after trimming, without `/`, `\` or control characters, and not `.` or `..`; folders nest 8 deep at most; names compare without case. A folder's path is its id in every method. `count` is the recordings filed directly in it, `totalCount` those in it and every folder below it.

## Methods (2.1)

| Method | Params | Result | Notes |
|---|---|---|---|
| `library.list` | (as before) `+ folder?: string \| null` | (as before) | Left out: every folder (the timeline). `null`: only recordings at the root. A path: only the recordings filed directly in that folder. Search, the type filter and the sort apply inside it. A value that is not a folder path answers `bridge.invalidParams`. |
| `folders.list` | `{}` | `{ folders: FolderInfo[], rootCount: number }` | Every folder sorted by path (`folders.json` together with every folder a recording names, with their parents), with its counts; `rootCount` counts the recordings at the root. Reads only. |
| `folders.create` | `{ parent: string \| null, name: string }` | `{ path }` | A new empty folder. `folder.notFound` (no such parent), `folder.nameTaken` (the parent already holds that name), `folder.invalid` (a bad name, or deeper than 8). |
| `folders.rename` | `{ path, name }` | `{ path }` | Renames in place; the recordings and folders inside follow. Changing only the case is allowed. `folder.notFound`, `folder.nameTaken`, `folder.invalid`. |
| `folders.move` | `{ path, parent: string \| null }` | `{ path }` | Moves the folder with what it holds into another folder or the top level. `folder.invalid` for a move into itself or one of its own folders, or one that would nest deeper than 8; `folder.nameTaken`; `folder.notFound`. |
| `folders.delete` | `{ path }` | `{ recordingsMoved, foldersMoved }` | After the page's confirmation. Removes the folder; the recordings and the folders in it move up into its parent (the root for a top-level folder). **No recording is deleted.** `folder.notFound`. |
| `library.moveToFolder` | `{ recordingIds: string[], folder: string \| null }` | `{ moved }` | Files 1–1000 recordings in a folder or (`null`) the root; `moved` leaves out those already there. `folder.notFound`, `project.notFound`, `project.recording` (still being recorded or saved; nothing is moved), `bridge.invalidParams` (no ids). |
| `audio.remove` | `{ recordingId }` | `{ freedBytes }` | The Delete audio dialog's action, after the page's confirmation (DESIGN.md §6.2). Writes `audioRemoved` into `project.json` **first** (the tracks stay listed without their hashes; `mix`, `peaks` and the audio hashes are cleared), **then** deletes every audio file: the files the tracks and the mix name (audio files directly in `tracks/`, and the mix and peaks at the top of the folder only), and the audio leftovers in `tracks/` (`*.wav`, `*.flac`, `*.m4a`, `*.aac`, `*.mp3`, parts and `.tmp` files), then writes History `stored` `completed` "Deleted the audio · 412 MB freed" ("2 audio tracks and the mix removed. The transcript and documents are kept; the audio can't be brought back."). The transcript, documents, attachments, annotations and any video are never touched; the recording stays in the Library and opens for reading. Errors: `project.notFound`, `project.recording`, `library.busy` (a stage of this recording is queued or running: every stage reads the audio), `bridge.invalidParams` (already deleted, or no audio files). |
| `audio.removeEstimate` | `{ recordingIds: string[] \| null, olderThanDays: number \| null }` | `{ recordings, bytes }` | What Delete audio would free: the named recordings, or (ids null) every stored recording with audio older than `olderThanDays` (null: every one). Feeds Settings' "Would free 3.1 GB now, from 12 recordings" and the Delete audio dialog. Reads only. |

Folder changes and filed recordings send `library.changed` (`recordingIds` empty when only folders changed), so the page reads `folders.list` and `library.list` again. After `audio.remove`: `processing.retry` and `transcript.retranscribe` answer `bridge.invalidParams` for the recording (nothing can be heard again), export lists the mixed audio and the tracks as unavailable with "Audio was deleted", and `library.usage` no longer counts them.

**On disk.** `project.json` schema **5**: `folder` (`null` at the root) and `audioRemoved: { at, trackIds, bytesFreed, reason: 'user' | 'age' }`; the v4 → v5 step writes `folder: null` and sets an unreadable `folder` to the root. The folder is data only: filing, renaming, moving or deleting folders never moves or deletes a file. `<library>\folders.json` (`{ schemaVersion: 1, folders: string[] }`, written atomically) keeps empty folders through restarts and index rebuilds; the index (`library.db` schema 4) caches `folder` and `audioRemoved` per row.

## Error codes (2.1)

`folder.notFound` (the folder named is not in the Library any more; detail: the path), `folder.nameTaken` (a folder with that name, ignoring case, is already there; detail: the name), `folder.invalid` (the name is empty, too long or has `/` or `\`, or the move would put a folder in itself or nest deeper than 8; nothing was changed).
## Resume recording (2.1)

A stopped recording can take more segments: the Recording session's stopped state and Review's header offer **Resume recording** for a recording started **today** (local date) that was recorded (not imported), is saved (`ready` or `recovered`) and still has its separate tracks (not "Keep only the mix"), while nothing else records. The earlier tracks are finalized and read-only and are never opened again: the new session writes new track files (`tracks/mic-2.wav`, `tracks/screen-2.mp4`; the engines skip every id the project already uses) placed after the recorded time so far. Segments sit end to end on the timeline; the real time between Stop and Resume is a pause (`pauses[]`, at the segment's offset, with the gap as its duration), never recorded silence. There is no undo: a segment ends with Stop like any recording, and a crash mid-segment is recovered like any recording (only the segment's own files are repaired).

```ts
interface RecordingResumeStoppedParams { recordingId: string; sourceIds?: string[] }  // without sourceIds: the last segment's sources still connected (audio and video)
interface RecordingSegment { index: number; startedAt: string; offsetMs: number; durationMs: number; gapMs: number }
interface Project { /* … */ segments: RecordingSegment[]; canResume: boolean; resumeNote: string | null }  // [] for an import; resumeNote: why not, as a §17 sentence
interface RecordingSummary { /* … */ resumedAt: string[] }   // when each segment after the first started; [] when recorded in one go
interface RecordingStatePayload { /* … */ segment: number | null; segmentOffsetMs: number }  // segment null for a recording's first; elapsedMs and track offsets stay the session's own; total = segmentOffsetMs + elapsedMs
```

| Method | Params | Result | Notes |
|---|---|---|---|
| `recording.resumeStopped` | `RecordingResumeStoppedParams` | `{ sessionId, recordingId, startedAt }` | 2.1. Starts a new session in the same recording; it is then driven like `recording.start`'s (`recording.pause`, `recording.resume`, `recording.stop`, `recording.markHighlight` at the recording-timeline time, `recording.setSource`, video). Any processing of the recording is stopped first. On Stop only the new segment's tracks are encoded and hashed; the mix and `peaks.json` are built again over every track of every segment (`mixUrl` then carries `?v=`), and every stage that had run (`transcript`, `speakers`, `topics`, `video`, `optimize`) is queued again over the **whole recording**, because a pass over only the new segment is not available; the current transcript stays until the new one replaces it (kept as a version when history is on). History: `recorded` `started` "Recording resumed at 10:42" (detail "Segment 2, after 12:40 recorded and a pause of 8:03 · …"), `recorded` `completed` "Recorded 5:10 more · 17:50 in all", `stored` `info` "Resumed recording saved · 2 segments, 17:50 in all" (which stages run again and why). Refused with `recording.cannotResume` (the message says why: imported, not today, not saved completely, keeps only its mix, still saving), `recording.alreadyActive`, `recording.sourceUnavailable` (none of the last sources is connected, or one cannot open), `recording.noSources` (explicit `sourceIds` without audio), `recording.diskFull`, `project.notFound`, `library.busy`. |

`project.json` schema **7** (2.7.0): `details.whoSpoke.added`, when names were added while recording (`SpeakerAdded`); the v6 → v7 step writes an empty list (and replaces a value that is not a list). The speakers stage uses it to break Speaking now ties towards a voice heard after the name was added, and lists low-confidence names in History (a mark that heard another voice; a name that went to someone heard only before it was added).

`project.json` schema **6**: `segments: [{ index, startedAt, offsetMs, durationMs, gapMs, endedAt }]`; each track and video track of a later segment carries `segment` (absent for the first). The v5 → v6 step writes one segment from `createdAt` and `durationMs`. `recording.state.json` (schema 1, optional fields) gains `segment` and `segmentOffsetMs`; its tracks, pauses and video parts are on the recording timeline. `library.db` schema 5 adds `resumedAt` (the index is rebuilt from the project folders on the first start).

## Error codes (resume)

`recording.cannotResume` (`recording.resumeStopped` for a recording that cannot take another segment; nothing was changed).

# After 2.2 — Larger local models

The product owner asked for larger local models that use the computer's RAM beside a small graphics card, and for mixture-of-experts models (ENGINE-NOTES.md §T, ARCHITECTURE.md §8 "Larger models"). Two catalog entries, `qwen3.5-27b-q4` (dense) and `qwen3.5-35b-a3b-q4` (mixture of experts), are local models like the others (`kind: 'llm'`, installed through `models.install`, verified by SHA-256, resumable, the free space checked first); the host plans where their weights go before every load. Contracts: `BridgeJsonContext` / `M4BridgeJsonContext` (host) and `ui/src/bridge/types.ts`; strict JSON pinned by `M2ContractSerializationTests.ModelsListShape` and `M4ContractSerializationTests.ProvidersAndTheSendSummary`.

```ts
interface ModelInfo { /* … */ group: 'larger' | null; minRamBytes: number | null }   // larger models: their group, and the RAM and video memory together at the smallest context
interface LocalModelPlanInfo {
  modelId: string; fits: boolean;
  line: string;                 // "Runs on the graphics card with the experts of 37 of 40 layers in RAM · about 12 tokens/s · 18 GB of RAM while writing", or the §17 sentence when it does not fit
  placement: 'card' | 'split' | 'experts' | 'ram' | 'none';
  gpuLayers: number;            // llama.cpp's n_gpu_layers: blocks on the card plus the output layer
  cardBlocks: number; totalLayers: number;
  cpuExpertLayers: number;      // mixture of experts: the first blocks whose routed experts stay in RAM
  contextTokens: number; vramBytes: number; ramBytes: number;
  tokensPerSecond: number;      // estimated, or the last measured load's speed for the same placement
}
interface ProviderInfo { /* … */ memoryPlan: LocalModelPlanInfo | null /* local, a larger model in effect */; largerModels: LocalModelPlanInfo[] | null /* local: every larger model's plan, installed or not */ }
interface GenerationRecord { /* … */ placement: string | null }   // "Ran on the graphics card with its experts in RAM · 12.3 tokens/s · 19 GB of RAM"
```

- **`providers.list`.** When the local model in effect is a larger one, `modelLabel` is "{name} · graphics card and RAM" (a split or experts placement), "{name} · graphics card" or "{name} · processor", `detail` is the plan's line with the context ("…, with a 16k context. Nothing leaves this PC."), and `memoryPlan` carries the plan. A larger model that does not fit is **not ready**: `code: 'ai.notEnoughMemory'`, `reason: 'Not enough memory'`, `detail` the §17 sentence ("Qwen3.5 35B-A3B needs 25 GB of memory; this PC has 19 GB free. Close other programs or choose a smaller model."); no smaller model stands in for it (it was chosen for its size). `largerModels` lists every larger model's plan on the local provider, so Settings shows each one's line before it is first used; cloud providers carry `null` for both, and so do the small models for `memoryPlan`.
- **`generation.start`** with a larger model that does not fit is refused with `ai.providerNotReady` (detail `ai.notEnoughMemory`). Otherwise the generation plans it again when it starts and sends the worker the placement (`LocalLlmJob.placement`: `gpuLayers`, `cpuExpertLayers`, the planned RAM and video memory, the RAM the worker checks is still available before it loads, the margin; `flashAttention`). The worker reports what it loaded (`device` line: `gpuLayers`, `cpuExpertLayers`, `ramBytes`, `reduced` when less went on the card than planned), and the host keeps it per model in `%LOCALAPPDATA%\Memento\local-model-loads.json` (schema 1, sizes and speeds only, written atomically) so the next plan is corrected by it.
- **Failures.** `ai.notEnoughMemory` (failed `generation.progress`): the RAM was short when the worker was about to load ("… needs 25 GB of memory; this PC has 19 GB free. Nothing left this PC and no document was changed. Close other programs or choose a smaller model."), or a recording started while a larger model wrote and the RAM available would have left capture short (Windows' 2 GB plus the capture reserve): the model is unloaded, the generation ends with "Qwen3.5 35B-A3B was unloaded because a recording started and this PC had 1.5 GB of memory free, too little to keep both. The recording is not affected. Nothing left this PC and no document was changed. Generate the document again when the recording has finished, or choose a smaller model.", and History (stage `minutes`, event `failed`) says "Meeting minutes stopped: Qwen3.5 35B-A3B was unloaded for a recording". The small models are never unloaded for a recording. A spill out of the card's memory while a larger model loads or writes does not fail the generation: the worker loads it again with less on the card (the experts of more blocks, then blocks, in RAM; at most twice while writing, then in RAM alone when loading) and answers the prompt again.
- **Records.** A document a larger model wrote has `record.placement` (How this was made shows it as Placement), and its History line ends with the same sentence.
- `settings.set` `ai.localModelId` accepts a larger model's id like any installed local model's.

# 2.5.0 — Your own server

"Your own server" is a fourth provider: any OpenAI-compatible server the user runs, such as Ollama, LM Studio, vLLM or a llama.cpp server (ARCHITECTURE.md §8, SECURITY.md §3). Provider id `server`, `kind: 'server'`, name and vendor "Your own server". Its address, model and context are Settings (`settings.ai.server` on disk); its key, optional, is in the DPAPI store under `server`. Records: `src/Memento.Core/Bridge/Contracts` (`AiServerState`, `AiServerPatch`, `AiServerProbeParams`, `AiServerProbeResult`, the new `ProviderInfo` members), serialized by `BridgeJsonContext` (settings) and `M4BridgeJsonContext` (the probe, `providers.list`).

- **Where the inputs go.** Judged from the address text alone, never by a lookup: `localhost`, 127.0.0.0/8 and `[::1]` are this PC (`loopback: true`, `leavesPc: false`); RFC 1918, link-local (169.254.0.0/16, fe80::/10), fc00::/7, a `.local` name and a single-label host name are the local network; anything else is remote. Only a loopback server keeps everything on the PC: it runs with "Allow external AI services" off, is never held by "Ask before every send", reads what the template ticks (the share switches do not apply), and the preview says it stays on the PC (`staysOnPc: true`) and the record `stayedOnPc: true`. Any other address leaves the PC (`leavesPc: true`): it needs "Allow external AI services" (`ai.disabled` otherwise, checked before anything is read or built), gets the same send confirmation as Claude and ChatGPT, and receives only what the share switches allow. Every one of these decisions keys on `leavesPc`, never on `kind`.
- **Plain http.** `http://` is accepted for this PC and the local network only. A plain-http remote address can be saved but is not ready (`ai.serverInsecure`), and nothing is ever sent to it, not even by "Test connection".
- **Addresses.** An absolute `http`/`https` URL without a query, fragment or user name; a missing scheme reads as `http://`; a trailing `/v1` is dropped (Memento adds `/v1/chat/completions` and `/v1/models`); saved normalized without a trailing slash (`localhost:11434/v1` is saved as `http://localhost:11434`). A bad one is `settings.invalidValue` (detail `ai.providers.server.address`); the message never repeats a user name or password. A settings file with an unusable address reads as none.
- **Models.** As the server names them: letters, digits and `. _ : / - @ +`, at most 200 characters, starting with a letter or digit (`qwen3:8b`, `qwen/qwen3-8b@q4_k_m`, `hf.co/org/Model-GGUF:Q4_K_M`); otherwise `settings.invalidValue` (detail `ai.providers.server.model`). There is no list: "Test connection" shows the server's.
- **Context.** `contextTokens`, default 8,192, clamped to 2,048…1,048,576 by `settings.set` and on load (the page clamps to the same range). It must be what the server actually gives the model: Ollama gives 4,096 unless `num_ctx` was raised, and a prompt beyond the server's context is cut or refused there. Each request carries at most a quarter of it as transcript (and at most 24,000 tokens), a map answer at most a quarter of it (at most 8,000), and `max_tokens` never more than the context leaves after the prompt. Tokens are estimated, erring high (`EstimatingTokenCounter.Server`).
- **`providers.list`** (no request; the server is always listed, also with external AI off), in this order: `ai.noServer` "No server address" when none is set; `ai.serverInsecure` "Needs https"; `ai.disabled` "External AI is off" when the address leaves the PC and external AI is off; `ai.noModel` "No model chosen". Ready: `modelLabel` "qwen3:8b at localhost:11434", `modelListed: null`, `modelId: null`, `address` the saved address, `detail` "Sends the ticked inputs to qwen3:8b at http://localhost:11434 when you press Generate. The server runs on this PC, so nothing leaves it. Memento assumes the server gives the model 8,192 tokens of context, as set in Settings (Ollama gives 4,096 unless num_ctx was raised); a long transcript is split into segments of up to 2,048 tokens." ("The server is on your network, so the inputs leave this PC." / "The server is outside your network, …" otherwise).
- **Generation.** `POST {address}/v1/chat/completions`, `stream: true`, the system prompt and turns as `messages`, `max_tokens`, `temperature` as the pipeline sets it, and for a JSON answer `response_format: { type: 'json_schema', json_schema: { name, schema, strict: true } }`. A server that answers 400 (or 422) naming `response_format`, `json_schema` or a schema gets the request once more with `{ type: 'json_object' }` and the schema in the system message, and is remembered for as long as Memento runs (by address); the pipeline's per-module validation still checks every answer. `Authorization: Bearer` is sent only when a key is saved. No redirect is followed (a 3xx is a specific error), and an answer is read up to 4 MiB of text and 8 MiB per stream line or event, as for the clouds (security audit 1, SA-36 and SA-45). The send summary, the preview, the Live output, History and "How this was made" name the provider, the model and the address; the record's `modelLabel` adds where it ran: "qwen3:8b at localhost:11434 (this PC)", "… (on your network)", "… (remote)". If the address changes between the send confirmation and the run, the confirmed one is used. Requests go one at a time (many such servers run one slot; the clouds take up to three at once). The Live output streams each reply as it arrives (`streamed: true`, `token` events whose `outputTokens` is an estimate that grows from the first piece), then the `reply`. When the server sends no `usage` chunk (Ollama, and any server not asked for one) the counts are estimates: the prompt as sent and the reply's text (`EstimatingTokenCounter.Server`), so the sheet, the record and the log never show zeros; a count the server does send is kept.
- **`ai.serverProbe`** ("Test connection"): the only request Memento makes for the server without a generation, and only when the page asks. `GET {address}/v1/models` with the saved key, and `GET {address}/api/version` when the model list says it is Ollama; nothing else, no redirect, 15 seconds each. The address need not be saved and nothing is saved. An address that leaves the PC is not contacted while external AI is off (`code: 'ai.disabled'`), a plain-http remote one never (`ai.serverInsecure`); an address that does not parse is `settings.invalidValue` (detail `address`).

| Method | Params | Result | Notes |
|---|---|---|---|
| `ai.serverProbe` | `AiServerProbeParams` | `AiServerProbeResult` | `serverName`: "Ollama 0.12.3", "LM Studio", "vLLM", "llama.cpp server" (from the model list's `owned_by`), else the `Server` header, else the host and port. `models`: the ids from `/v1/models`, sorted. `code`: `null` when it worked; `ai.serverUnreachable`, `ai.serverNeedsKey`, `ai.serverNotCompatible`, `ai.noModel` (it lists none), `ai.disabled`, `ai.serverInsecure`. `detail`: "Connected to Ollama 0.12.3 at http://localhost:11434: 2 models. It runs on this PC, so nothing leaves it.", or the §17 sentence. |

Error codes and their sentences (each names the address; generation failures say what is safe and the fix, DESIGN.md §17):

| Code | When | Sentence (generation; "Test connection" leaves out the middle sentence) |
|---|---|---|
| `ai.noServer` | `providers.list`, `generation.start` (as `ai.providerNotReady` detail): no address | "No server address is set, so nothing is sent to Your own server. Enter the address of Ollama, LM Studio or another OpenAI-compatible server in Settings › AI and privacy." |
| `ai.noModel` | no model chosen; the probe: the server lists none | "No model is chosen for the server at {address}, so nothing is sent to it. Choose one in Settings › AI and privacy (Test connection lists the server's models)." |
| `ai.serverInsecure` | plain http to a remote address | "{address} is outside this PC and your local network, and Memento sends there only over https. Nothing was sent and no document was changed. Use an https:// address in Settings › AI and privacy." |
| `ai.serverUnreachable` | connection refused / host unreachable / name not found / the probe timed out | "Nothing is listening at http://localhost:11434. Nothing was sent and no document was changed. Start Ollama or LM Studio, or check the address in Settings › AI and privacy." / "The server at {address} could not be reached. … Check that the computer is on and the server is running, or check the address in Settings › AI and privacy." |
| `ai.serverNotCompatible` | 404/405/501 at the API path, an answer that is not a completion or a model list, a redirect (probe) | "The server at {address} does not answer like an OpenAI-compatible server (HTTP 404 at /v1/chat/completions). Nothing was generated and no document was changed. Check the address in Settings › AI and privacy: Memento adds /v1/chat/completions to it." |
| `ai.serverNeedsKey` | 401 or 403 | "The server at {address} asks for a key (HTTP 401). Nothing was generated and no document was changed. Add the server's key in Settings › AI and privacy." ("did not accept the saved key" when one is saved) |
| `ai.serverModelUnknown` | 404 (or 400) naming the model | "The server at {address} does not offer a model called 'qwen9:1b'. Nothing was generated and no document was changed. Choose one of its models in Settings › AI and privacy (Test connection lists them), or check the name." No other model is tried. |
| `ai.contentTooLong` | the server's context-overflow messages (vLLM, llama.cpp, LM Studio, Ollama) or 413 | "The text is too long for {model} at {address}: 9,000 tokens, the server's limit is 8,192. Nothing was sent twice and no document was changed. Set the context in Settings › AI and privacy to what the server runs, or raise it on the server." |
| `ai.network` | no answer in time | "The server at {address} did not answer within 5 minutes. Nothing was sent twice and no document was changed. A large model can take a while to load; try again, or check that the server is running." |
| `ai.providerError` | a redirect, a 5xx, another 400, an answer too long or not JSON | "The server at {address} answered with a redirect to {host} (HTTP 307), and Memento does not follow redirects, …", "The server at {address} had a problem on its side (HTTP 500). … Check the server's own window or log (a model too large for its memory is a common cause), then try again.", "The server at {address} sent an answer Memento could not read. …" |
| `ai.disabled` | an address that leaves the PC while external AI is off | "The server at {address} is not on this PC, and external AI is off, so nothing was sent and no document was changed. Turn on \"Allow external AI services\" in Settings › AI and privacy, or use a server on this PC or the local model." |

## 2.5.0 server contract

The exact shapes the host sends and accepts (the UI's `ui/src/bridge/types.ts` mirrors them; `METHOD_NAMES` gains `'ai.serverProbe'` and `ERROR_CODES` gains the seven codes below):

```ts
type ProviderId = 'anthropic' | 'openai' | 'local' | 'server';

// settings.get / settings.set result: ai.providers.server
interface AiServerState {
  hasKey: boolean;          // a bearer key is saved (ai.setKey { provider: 'server' })
  address: string | null;   // as saved: "http://localhost:11434"; null when none is set
  model: string | null;     // "qwen3:8b"; null when none is chosen
  contextTokens: number;    // 2048…1048576 (clamped), default 8192
  loopback: boolean;        // the address is this PC
  leavesPc: boolean;        // sending to it takes the inputs off this PC (false when no address is set)
  insecure: boolean;        // plain http outside this PC and the local network (refused)
}
interface AiProvidersSnapshot { anthropic: AiProviderState; openai: AiProviderState; server: AiServerState }

// settings.set params: ai.providers.server (each field optional; null clears address or model)
interface AiServerPatch { address?: string | null; model?: string | null; contextTokens?: number }
interface AiProvidersPatch { anthropic?: { model?: string | null }; openai?: { model?: string | null }; server?: AiServerPatch }

// ai.setKey / ai.clearKey
interface AiProviderParams { provider: 'anthropic' | 'openai' | 'server' }

// providers.list: ProviderInfo gains (every provider)
interface ProviderInfo { /* … */ kind: 'cloud' | 'local' | 'server'; leavesPc: boolean; address: string | null /* server only */ }
interface PreviewProviderInfo { id: ProviderId; name: string; kind: 'cloud' | 'local' | 'server'; modelLabel: string | null }

// ai.serverProbe
interface AiServerProbeParams { address: string }
interface AiServerProbeResult {
  reachable: boolean; serverName: string | null; code: string | null; detail: string | null /* always set by this host */;
  loopback: boolean; leavesPc: boolean; insecure: boolean; models: string[];
}

// new ERROR_CODES
'ai.noServer' | 'ai.noModel' | 'ai.serverInsecure' | 'ai.serverUnreachable' | 'ai.serverNotCompatible' | 'ai.serverNeedsKey' | 'ai.serverModelUnknown'
```

Serialized examples (pinned by `ServerSettingsBridgeTests` and `ServerProviderBridgeTests`): `settings.get` with nothing set gives `"server":{"hasKey":false,"address":null,"model":null,"contextTokens":8192,"loopback":false,"leavesPc":false,"insecure":false}`; a probe of a fake Ollama gives `{"reachable":true,"serverName":"Ollama 0.12.3","code":null,"detail":"Connected to Ollama 0.12.3 at http://127.0.0.1:50123: 2 models. It runs on this PC, so nothing leaves it.","loopback":true,"leavesPc":false,"insecure":false,"models":["llama3.2:3b","qwen3:8b"]}`.

# 2.6.0 — Speakers you can trust and fix fast

## Speakers you can fix fast (2.6.0)

Review's speaker menu gained ways to correct a whole section at once (ROADMAP.md 2.6.0). This turn, From here on and "This line and the {n} that sound the same" use `transcript.setSegmentsSpeaker`; the swap is one new method, and the lines that sound the same come from one new read. `METHOD_NAMES` gains `'transcript.swapSpeakers'`, `'transcript.sameVoice'`, `'transcript.splitSpeaker'`, `'transcript.splitSegment'` and `'transcript.joinSegments'`.

| Method | Params | Result | Notes |
|---|---|---|---|
| `transcript.swapSpeakers` | `{ recordingId, speakerId, otherSpeakerId, fromSegmentId?: string \| null }` | `{ segments: TranscriptSegment[], speakers: Speaker[] }` | Two speakers trade their lines in one write: every line of `speakerId` takes `otherSpeakerId` and the other way round, from `fromSegmentId` (inclusive, in transcript order) to the end, or everywhere when it is missing or `null`. The lines moved get `speakerConfidence` 1 and `speakerByUser` true (the user placed them); names, ids and colours do not change; talk times are recomputed. `segments` are the lines changed, in transcript order; when no line changes nothing is written and `segments` is empty. The same call again puts the lines back, so Review's Undo and Redo both call it (with Remember speakers by voice on, Review then calls `voices.remember` for each named speaker and `voices.revert` on Undo). Both speakers must exist (`transcript.speakerNotFound`, `detail` the id) and differ (`bridge.invalidParams`: "Choose two different speakers."); a `fromSegmentId` the transcript does not have is `transcript.segmentNotFound` (`detail` the id); nothing changes on any refusal. History: "Speakers swapped" with "Ana and Ben · 41 lines" or "Ana and Ben from 12:30 · 38 lines". |
| `transcript.sameVoice` | `{ recordingId, segmentIds: string[] }` | `{ segmentIds: string[] }` | Reads only. Every line was won by one raw diarizer voice, and a voice's lines are nearly always one person (ENGINE-NOTES.md §L), so when one is on the wrong speaker the others most likely are too. Answers every line that shares a voice with any of the asked lines (the union of the `segmentIds` of each `voices.json` cluster that holds one of them), limited to the lines the transcript still has, without the asked ones, in transcript order; whatever speaker they have now. Empty without `voices.json`, or when no voice holds an asked line. 1–100,000 ids, else `bridge.invalidParams`; a line the transcript does not have is `transcript.segmentNotFound` (`detail` the id); no transcript yet is `transcript.none`. Review asks once for a line as its speaker menu opens, keeps the answer until the transcript changes, and offers **This line and the {n} that sound the same** (n counts only lines not already on the chosen speaker; the row is left out while the answer is not there), moving them with `transcript.setSegmentsSpeaker`. |
| `transcript.splitSpeaker` | `{ recordingId, speakerId }` | `{ speakers: Speaker[], created: Speaker, segmentIds: string[] }` | "{name} is two people", the inverse of a merge: the raw voices (`voices.json` clusters with an embedding) that hold the speaker's lines now are grouped into two by sound (the two least alike seed the groups; every other voice joins the seed mix it sounds more like, the mixes are recomputed speech-weighted, up to 10 rounds), and the half with less talk time moves to a new speaker (`spkN`, "Speaker N", not renamed, the next colour) in one write; `segmentIds` are the lines moved, in transcript order. Lines the user placed (`speakerByUser`) and lines in no voice stay; moved lines keep their `speakerConfidence`. `transcript.cannotSplit` (`detail`: the speaker's id) with a §17 message when the speaker has fewer than two voices ("… was heard as one voice, so there is nothing to split by sound. Move lines with their menu instead."), when its two least alike voices are still at least 0.8 alike ("…'s voices sound alike (0.86), so Memento cannot split them by sound.") or when the other half holds only placed lines; `transcript.speakerNotFound`, `transcript.none`, `project.notFound`. History: "Speaker split" with "Ana · 23 lines to Speaker 5 by voice". Undo is `transcript.mergeSpeakers` of the created speaker into the original (exact, by id); Redo splits again. |
| `transcript.splitSegment` | `{ recordingId, segmentId, at: number }` | `{ segments: TranscriptSegment[], speakers: Speaker[] }` | One line cut in two at a word (Review: Ctrl+Enter while editing a line, at the caret), so the rest can go to another speaker. `at` is a character offset into the line's current `text`; the cut moves to the nearest word boundary, and both parts keep at least one word (`bridge.invalidParams` "Choose a place inside the line's words." when `at` is outside the text, "Both parts of a split line need at least one word." for a one-word line). `segments` are the two parts in order: the first keeps the line's id, start, speaker, `speakerConfidence` and `speakerByUser`; the second gets `{id}-2` (`-3`… past any id the transcript has), the line's end and the same speaker fields. When the line's `words` still match its text, each part takes its words and the times at the cut come from them (the first ends with its last word, the second starts with its first); otherwise the times split in proportion to the characters. A part's `confidence` is the lowest of its words, or the line's own without words. An edited line's `edited` (time and the whole original wording) goes to both parts. Talk times recomputed. A `voices.json` voice that holds the line holds both parts afterwards (so `transcript.sameVoice` still covers them); highlights and chapters keep pointing at the first part. `transcript.segmentNotFound`, `transcript.none`, `project.notFound`. History: "Line split" with `at 2:14 · "…and then" \| "Yeah, so…"`. Undo is `transcript.joinSegments` on the first part. |
| `transcript.joinSegments` | `{ recordingId, segmentId }` | `{ segment: TranscriptSegment, speakers: Speaker[], removedId: string, highlights: Highlight[] }` | The line joined with the next line in transcript order, which must be on the same track (`transcript.cannotJoin`, `detail` the line's id: "There is no line after this one." / "The next line is on another track, so the two cannot be joined.", each followed by "Nothing was changed."). The joined line keeps the first line's id, speaker, `speakerConfidence` and `speakerByUser`; its text is both texts with one space between, its `words` both lists in order (none when either line has none), its span from the first's start to the second's end, its `confidence` the lower; it reads as edited when either line was (the original is the two originals joined, or one of them when both parts came from the same edit), and two unedited lines joined stay unedited. The second line's id is gone (`removedId`); a highlight on it now points at the joined line, and `highlights` is the recording's whole list as it is now; chapters are by time and do not change; `voices.json` forgets the gone id. Talk times recomputed. `transcript.segmentNotFound`, `transcript.none`, `project.notFound`. History: "Lines joined" with `at 2:14`. Undo is `transcript.splitSegment` at the join (the offset after the first line's text). |


## Corrections guide the next pass (2.6.0)

A line the user places on a speaker carries `speakerByUser: true` (`TranscriptSegment`, left out of the JSON while false, so transcripts from before read as false). It is set by `transcript.setSegmentSpeaker`, `transcript.setSegmentsSpeaker` and `transcript.swapSpeakers` whenever the line gets a speaker (a new one included), and cleared when a line is set to no speaker. `transcript.mergeSpeakers`, `transcript.reduceSpeakers`, `transcript.restoreSpeaker` and `transcript.restoreSpeakers` act on speakers, not on lines, so every line keeps the flag it has; `transcript.editSegment` keeps it too. The speakers stage's own assignments leave it false.

What the next speakers pass (`processing.retry` on `speakers`, Review's "Identify speakers again") does with those lines, through `SpeakerAssigner` with one `SpeakerPin` per placed line (the line, the speaker's name as its label, and whether that name was given by the user):

1. Every raw voice (`voices.json` cluster) that won a placed line is pinned to the label most of its placed lines carry (by talk time).
2. Voices pinned to one label are joined before anything is compared, whatever their tracks. No join — to reach a count, by similarity, or a little voice folding in — ever puts two labels together; an unpinned voice may still join a pinned one. When the count asked for is below the number of labels, the labels win and the count is not reached.
3. A placed line keeps its label's person whatever its voice did, with `speakerConfidence` 1 and `speakerByUser` true; every other line is assigned as before, with `speakerByUser` false. A label none of whose lines a voice won is still a person, added after the others.
4. A person pinned by a name the user gave takes that name first (`renamed: true`), before the shared-lines carry-over, which never gives that name to a second speaker; Who spoke's names go to the remaining speakers as before.

History's `speakers` `completed` detail gains `9 corrected lines kept, 3 voices pinned` when any line was placed. A fresh transcript has no speakers, so a first pass has no pins (and is the only pass that cuts lines where the speaker changes).

## Speaking now (2.6.0)

`RecordingDetails.whoSpoke.marks` holds the Speaking now marks: while recording, the Record screen's details show a **Speaking now** button beside each name under Who spoke, and a press adds `{ name, atMs }` with the recording timer's time (recorded time, excluding pauses) and saves the details through `project.updateDetails` like any other edit (`whoSpoke` replaces whole, so the UI sends count, names and marks together; a name taken off the list takes its marks with it). `project.json` keeps them in `details.whoSpoke.marks` without a schema step: a file from before reads as having none.

The speakers stage uses them after it has grouped the voices: for each mark, every raw voice (`voices.json` cluster, any track) with a diarizer turn covering `atMs` votes for the mark's name on the person it was grouped into; each name goes to the person with the most votes for it (ties: the person who speaks first), one name per person and one person per name. Names are given in this order: the labels of placed lines, names carried over from the previous pass, **Speaking now marks**, then the rest of `whoSpoke.names` in order of first appearance. A mark whose moment no turn covers, or whose name is no longer listed, is ignored. The `started` History detail says `3 Speaking now marks`, and the `completed` detail `2 names from Speaking now` beside `1 name from Who spoke`.

## Add and remove screens while recording (2.8.0)

Contracts: `VideoBridgeJsonContext` (host) and `ui/src/bridge/types.ts`; pinned by `VideoAddRemoveSourceTests` and `VideoContractTests`.

| Method | Params | Result | Notes |
|---|---|---|---|
| `video.addSource` | `{ sessionId, sourceId }` | `{ videoTracks: VideoTrackStatus[] }` | The Record screen's **Add screen** while recording: starts one more display or window (a `sourceId` from `video.listSources`' displays or windows) from now, in its own capture pipeline and its own file. With no running screen track it becomes the `screen` track; otherwise it is listed by its own track id (`screen-2`, `screen-3`). Up to 4 screens record at once. Audio and the other tracks are never touched; a source that cannot start changes nothing. Errors: `video.sourceAlreadyRecorded`, `video.tooManySources`, the video start codes (`video.sourceNotFound`, `video.protectedContent`, `video.captureBlocked`, `video.encoderUnavailable`), `recording.noSession`, `bridge.invalidParams` (a camera id, or no id). |
| `video.removeSource` | `{ sessionId, trackId }` | `{ videoTracks: VideoTrackStatus[] }` | Stops one track (`screen`, `screen-2`, `camera`) from now, after the page's confirmation; its file keeps what it recorded and the track is stored with `endReason: "disabled"`. Removing the last screen is allowed: the audio keeps recording, and **Add screen** can start one again. Errors: `video.sourceNotFound` (no such track in the session), `recording.noSession`, `bridge.invalidParams` (no id). |

`VideoTrackStatus` gains `role` (`screen` or `camera`); `trackId` is the role for the first screen and the camera, as before, and the track id for an added screen. `recording.videoProblem`'s `trackId` follows the same rule, so a closed window names the screen it belonged to. Pause, resume, checkpoints, low disk space, stop and Resume recording act on every running track alike.

`project.json` schema v8 adds `videoTracks[].stoppedAtMs`: where the track stopped on the recording timeline (pauses excluded). With `startOffsetMs` it is the track's own time range. The v7 → v8 step (`VideoRangesMigration`) sets it to `endedEarlyAtMs`, else `startOffsetMs + durationMs`. `ProjectVideoTrack` gains `trackId` (unique in the recording) and `stoppedAtMs`; `id` stays the role. Review shows, at the playhead, the screen track whose range covers it (the most recently started when several do), and a calm placeholder in a gap where no screen was recorded.

## Error codes (2.8.0)

`video.sourceAlreadyRecorded` (that display or window is already being recorded; nothing changed), `video.tooManySources` (4 screens are already recording; nothing changed).
