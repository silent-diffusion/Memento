# Memento — Bridge contract

The host ↔ UI contract (ARCHITECTURE.md §3). Every method and event listed here exists as a C# record in `src/Memento.Core/Bridge/Contracts/` and a TypeScript type in `ui/src/bridge/types.ts`. JSON field names are camelCase. Times are ISO 8601 with offset unless the field ends in `Ms` (milliseconds, integer). Ids are opaque strings. Lists are never null; use `[]`.

Status: **M0 methods are implemented. M1 methods and events (marked M1) are the contract both sides build against.**

## Shared types

```ts
type RecordingType = 'meeting' | 'interview' | 'presentation' | 'lecture' | 'dictation' | 'research' | 'general' | string; // any other string is a custom type name
type StageName = 'stored' | 'transcript' | 'speakers' | 'minutes';
type StageState = 'done' | 'active' | 'queued' | 'failed';
interface StageStatus { stage: StageName; state: StageState; percent: number | null; /* M1: */ label: string | null; /* e.g. "64% · local GPU", "Done", "Queued", "Transcript failed" */ }

interface RecordingSummary {                // one Library row / card
  id: string; title: string; type: RecordingType;
  createdAt: string; durationMs: number; participantCount: number; hasVideo: boolean;
  stages: StageStatus[];                    // [] means "Audio only"
  // M1:
  people: string[];                         // participant names + renamed speakers, for search and the meta line
  isProcessing: boolean;                    // any stage active or queued
  state: 'recording' | 'finalizing' | 'ready' | 'recovered' | 'failed';
}

interface AudioSource {                     // M1
  id: string;                               // stable within a session: "mic:<endpointId>", "system:<endpointId>", "app:<pid>"
  kind: 'microphone' | 'system' | 'application';
  name: string;                             // "Shure MV7", "Everything this PC plays", "Zoom"
  detail: string;                           // "USB", "Default output", "Only this app"
  isDefault: boolean;
  processId: number | null;                 // application sources only
}

interface Track { id: string; sourceId: string; sourceKind: AudioSource['kind']; name: string; file: string; /* relative to the project folder */ sampleRate: number; channels: number; durationMs: number; sha256: string | null; endedEarlyAtMs: number | null; }

interface Agenda { source: string | null; /* "agenda.docx" */ parsedLocally: boolean; items: AgendaItem[]; }
interface AgendaItem { id: string; text: string; covered: boolean; uncertain: boolean; uncertainReason: string | null; }

interface RecordingDetails {
  title: string; type: RecordingType; participants: string[]; purpose: string; platform: string;
  organization: string; location: string; notes: string; tags: string[]; agenda: Agenda;
}

interface Chapter { id: string; atMs: number; title: string; origin: 'user' | 'local' | 'ai'; }
interface Highlight { id: string; atMs: number; note: string; origin: 'user' | 'local' | 'ai'; segmentId: string | null; }
interface Topic { id: string; label: string; origin: 'user' | 'local' | 'ai'; }

interface HistoryEntry { at: string; stage: StageName | 'recorded' | 'edited' | 'exported' | 'recovered'; event: 'started' | 'progress' | 'completed' | 'failed' | 'info'; summary: string; detail: string | null; /* engine, model, device, duration, what was sent */ }

interface Project {                         // M1: everything Review needs for one recording
  summary: RecordingSummary;
  details: RecordingDetails;
  tracks: Track[];
  mixUrl: string | null;                    // https://library.memento/projects/<id>/mix.flac once finalized
  peaksUrl: string | null;                  // https://library.memento/projects/<id>/peaks.json
  chapters: Chapter[]; highlights: Highlight[]; topics: Topic[];
  history: HistoryEntry[];
  integrity: { algorithm: 'sha256'; computedAt: string | null };
  sizeBytes: number;
}
```

The host maps a second virtual host, `https://library.memento/`, to the library folder (DenyCors) so the page can stream media and peaks with plain `<audio>` and `fetch`. The UI never constructs those URLs itself; it uses the ones the host returns.

## Methods

| Method | Params | Result | Notes |
|---|---|---|---|
| `app.version` | `{}` | `{ version, osVersion, isDarkTheme }` | M0 |
| `app.openExternal` | `{ url }` | `{ opened }` | M0. https: or ms-settings: only |
| `ui.ready` | `{}` | `{}` | M0 |
| `settings.get` | `{}` | `SettingsSnapshot` | M0; M1 extends the snapshot (below) |
| `settings.set` | `Partial<SettingsSnapshot>` (nulls keep) | `SettingsSnapshot` | M0; M1 adds fields |
| `library.list` | `{ query?: string, type?: RecordingType \| 'all', sort?: 'newest' \| 'oldest' \| 'longest' \| 'title' }` | `{ recordings: RecordingSummary[], totalDurationMs, totalCount }` | M1 adds params. `query` searches titles, people and (M2) transcripts. Result reflects the filter. |
| `library.processing` | `{}` | `{ current: { recordingId, title, meta: RecordingSummary, stages: StageStatus[] } \| null, othersCount }` | M1. The processing card. |
| `project.get` | `{ recordingId }` | `Project` | M1 |
| `project.updateDetails` | `{ recordingId, details: Partial<RecordingDetails> }` | `Project` | M1. Works during recording too. |
| `project.deleteEstimate` | `{ recordingId }` | `{ title, sizeBytes, items: string[] }` | M1. Feeds the delete confirmation copy. |
| `project.delete` | `{ recordingId }` | `{}` | M1. Refused with `project.recording` while that recording is active. |
| `project.rename` | `{ recordingId, title }` | `Project` | M1 |
| `annotations.addChapter` / `updateChapter` / `removeChapter` | `{ recordingId, chapter: Partial<Chapter> }` / `{ recordingId, chapterId }` | `{ chapters }` | M1 |
| `annotations.addHighlight` / `updateHighlight` / `removeHighlight` | likewise | `{ highlights }` | M1 |
| `annotations.addTopic` / `removeTopic` | likewise | `{ topics }` | M1 |
| `sources.list` | `{}` | `{ audio: AudioSource[], videoAvailable: false }` | M1. Re-enumerates each call. |
| `recording.start` | `{ title, type, sourceIds: string[] }` | `{ sessionId, recordingId, startedAt }` | M1. Fails with `recording.noSources` if empty, `recording.sourceUnavailable` (detail names it) if one cannot open; the others are not started in that case. |
| `recording.setSource` | `{ sessionId, sourceId, enabled }` | `{ tracks: Track[] }` | M1. Starts or ends one track. |
| `recording.pause` / `recording.resume` | `{ sessionId }` | `{}` | M1 |
| `recording.markHighlight` | `{ sessionId, note?: string }` | `{ highlight: Highlight }` | M1 |
| `recording.stop` | `{ sessionId }` | `{ recordingId }` | M1. Returns when finalize has started; `recording.state` events report `finalizing` then `ready`. |
| `recording.current` | `{}` | `{ session: RecordingStatePayload \| null }` | M1. Lets the UI rejoin an active session after a reload. |
| `recovery.list` | `{}` | `{ items: { recordingId, title, startedAt, tracksIntact, tracksTotal, lastCheckpointAt, recoveredDurationMs, mayBeMissingMs }[] }` | M1. Projects repaired at launch. |
| `recovery.acknowledge` | `{ recordingId }` | `{}` | M1. Dismisses the dialog for this project. |
| `dialog.pickFolder` | `{ title, initialPath?: string }` | `{ path: string \| null }` | M1 |
| `status.get` | `{}` | `FooterStatusPayload` | M1 |

## Events

| Event | Payload | Notes |
|---|---|---|
| `theme.changed` | `{ isDark }` | M0 |
| `status.footer` | `{ engine: { ready, device }, storage: { freeBytes, lowSpace }, /* M1: */ recording: { active: boolean, lastCheckpointAt: string \| null, lostSource: string \| null }, processingPaused: string \| null /* reason */ }` | M0, extended in M1 |
| `recording.state` | `RecordingStatePayload = { sessionId, recordingId, state: 'recording' \| 'paused' \| 'finalizing' \| 'ready' \| 'stopped', startedAt, elapsedMs, tracks: Track[], lastCheckpointAt, highlightsCount }` | M1, on every change and at least every second while recording |
| `recording.levels` | `{ sessionId, levels: { sourceId: string, rms: number /* 0..1 */, peak: number }[] }` | M1, ≤ 30 per second |
| `recording.sourceLost` | `{ sessionId, sourceId, name, atMs, remaining: string[] }` | M1 |
| `recording.stoppedByHost` | `{ sessionId, recordingId, reason: 'diskFull' \| 'deviceLost' \| 'error', atMs, message }` | M1 |
| `library.changed` | `{ recordingIds: string[] }` | M1, after any project write; the UI refetches |
| `processing.progress` | `{ recordingId, stages: StageStatus[] }` | M1 (only `stored` in M1; transcript/speakers in M2) |
| `storage.lowSpace` | `{ freeBytes, thresholdBytes, recordingContinues: boolean, transcriptionPaused: boolean }` | M1, banner |

## Settings snapshot (M1)

```ts
interface SettingsSnapshot {
  theme: 'system' | 'light' | 'dark'; libraryPath: string; listDensity: 'comfortable' | 'compact';
  recording: {
    defaultType: RecordingType; defaultSourceIds: string[];            // remembered selection
    keepSeparateTracks: true;                                           // fixed in M1
    storage: { codec: 'flac' | 'aac' | 'mp3'; bitrateKbps: number | null; downmixMono: boolean; keepOnlyMix: boolean };
    checkpointSeconds: number;                                          // 30
    lowSpaceGb: number;                                                 // 10
  };
}
```

## Error codes

`invalidRequest`, `invalidJson`, `invalidParams`, `unknownMethod`, `cancelled`, `internal` (M0), plus M1: `project.notFound`, `project.recording`, `recording.noSources`, `recording.sourceUnavailable`, `recording.noSession`, `recording.diskFull`, `settings.libraryMoveUnavailable`. Messages follow DESIGN.md §17: name the thing, give the amount or time, say what is safe, offer the fix.
