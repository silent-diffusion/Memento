# Memento — Architecture

This document fixes the technical decisions for the Memento Windows application. `PRODUCT-SPEC.md` says what the app must do; `../design/DESIGN.md` says how it looks; this document says how it is built. Builders follow it; changes to it are deliberate and reviewed.

## 1. Platform and stack

| Concern | Decision | Why |
|---|---|---|
| Target OS | Windows 11 (primary). Windows 10 version 2004+ is tolerated where it costs nothing. x64 only. | Per-process audio capture needs Windows 10 2004+; the product owner runs Windows 11. |
| Host | .NET 8, C#, WPF window hosting a single **WebView2** control. Self-contained publish (`win-x64`, runtime bundled). | Best-supported access to WASAPI, Media Foundation, Windows OCR and GPU inference libraries; no runtime install for end users. |
| UI | TypeScript + **Preact** + Vite, static assets embedded in the app and served through a WebView2 virtual host (`https://app.memento/`). Styling comes from `design/tokens.css` and the markup in `design/renders/`. Fonts (Manrope, JetBrains Mono; both OFL) are bundled, never fetched. | The design handoff is HTML/CSS; rebuilding it in XAML would lose fidelity. |
| Storage | One folder per recording project holding JSON and media files. A SQLite index (`library.db`, with FTS5) that can always be rebuilt from the project folders. | Local-first, user-owned, inspectable, survives index corruption. |
| Audio capture | WASAPI shared-mode, event-driven. NAudio 2.4 (the last .NET 8 release) for device and session enumeration; **our own capture loop** (hand-written COM interop, about 150 lines, no CsWin32) for every source so each packet carries its QPC and device-position timestamps. Per-process loopback via `ActivateAudioInterfaceAsync` + `AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK` with `INCLUDE_TARGET_PROCESS_TREE`. Endpoint loopback delivers **no packets during silence**, so the writer inserts clock-timed silence for it; process loopback streams zeros continuously and is preferred when the user picks apps. | Verified in the 2026-10 spikes (ENGINE-NOTES.md §C). |
| Audio encode/decode | Windows **Media Foundation** built-in codecs: FLAC (bit-exact round trip verified), MP3, AAC (m4a), WAV. No bundled ffmpeg, no libFLAC. The FLAC output type is obtained from the FLAC MFT (`SetInputType` → `GetOutputAvailableType(0)`), input is int16/int24 PCM (float is converted first), and FLAC is encoded **only from finished WAV tracks** because the MF FLAC sink writes nothing until it finalizes. | Zero native dependencies (ENGINE-NOTES.md §A). |
| Transcription | **Whisper.net** 1.9 (whisper.cpp) with runtime order `[Vulkan, Cpu]`; **no CUDA** (it brings no speed gain on the reference GPU, needs ~690 MB of extra DLLs and aborts the process when it fails to load). Runs in a **separate worker process** so a native assert cannot take down the app and VRAM is released cleanly. Default model `large-v3-turbo` on a GPU with ≥ 2.5 GB free VRAM, `small` on CPU; the discrete GPU is chosen explicitly; a short punctuated prompt is always passed; DTW timestamps are never enabled; a coverage check flags stretches of speech energy with no transcript. The VC++ runtime DLLs are shipped app-locally. Models are downloaded on first use, never bundled. | Measured RTF 0.038 for large-v3-turbo on Vulkan vs 1.03 on CPU (ENGINE-NOTES.md §D). |
| Speaker identification | **sherpa-onnx** 1.13 offline diarization: pyannote `segmentation-3.0` + `nemo_en_titanet_small` embeddings, automatic clustering with threshold 0.8 (user-set expected speaker count maps to a fixed cluster count), `ComputeConfidence` on; run per track, merged by time. | Separated two readers exactly and kept one reader whole; RTF 0.10 on CPU; 22 MB native footprint (ENGINE-NOTES.md §E). |
| OCR (agenda photos) | **Windows.Media.Ocr** by default: input upscaled so body text is ≥ 24 px, line boxes built from word boxes, results always shown for review (no confidence is available). **Tesseract** (downloadable language data, per-word confidence) as the alternative engine and the fallback when the OCR language is not installed in Windows. | Built-in is free and local; the alternative satisfies the "open-source, downloadable, managed in Settings" rule (ENGINE-NOTES.md §F). |
| Agenda parsers | DocumentFormat.OpenXml (docx, xlsx), PdfPig (pdf), built-in CSV/Markdown/plain text. | All MIT/Apache, pure managed. |
| Document export | DOCX via DocumentFormat.OpenXml; PDF by rendering the same HTML the viewer shows through WebView2 `PrintToPdfAsync`; Markdown by serializer. | The preview is the contract: PDF comes from the exact preview HTML. |
| External AI | `IAiProvider` abstraction with Anthropic (Messages API) and OpenAI implementations. Off by default. | Spec requirement; extensible to more providers. |
| Secrets | API keys encrypted with Windows DPAPI (user scope) in `%LOCALAPPDATA%\Memento\secrets.bin`. Never in settings.json, logs, exports or project folders. | Keys must never be echoed or leak into a shared project. |
| Logging | Serilog, rolling files in `%LOCALAPPDATA%\Memento\logs\`. Logs may contain file paths and engine diagnostics; they must never contain transcript text, document text, participant names or keys. | Diagnosable without leaking content. |
| Installer and updates | **Velopack**: `Setup.exe` one-click install per user, delta updates from GitHub Releases, no admin rights. WebView2 Evergreen runtime bootstrapped if missing. Unsigned for now. The Velopack pack id is `MementoApp` (installer `MementoApp-win-Setup.exe`, installed to `%LOCALAPPDATA%\MementoApp`) because Velopack replaces or deletes its whole install folder, and the data root is `%LOCALAPPDATA%\Memento`. The id is permanent. | Matches "download and install with one click". |
| CI | GitHub Actions on `windows-latest`: build, test, lint on every PR; tag `v*` publishes a release with `Setup.exe` and the Velopack update feed. | Every release is reproducible from a tag. |
| Versioning | SemVer. `0.y.z` until the first public release `1.0.0`. | |

**Engine philosophy.** Every engine (transcription, diarization, OCR, and later anything else) is selected in Settings from a catalog of built-in and downloadable open-source options. A single **Model manager** in Core handles download, SHA-256 verification, storage under `%LOCALAPPDATA%\Memento\models\<engine>\`, removal, and reporting installed state and size. New engines register with the catalog; they do not add UI outside Settings.

**Not in 1.0:** video capture (screen, window, camera). The project schema reserves fields for it so adding it later does not change how recordings are stored.

## 2. Solution layout

```
Memento/
├── Memento.sln
├── src/
│   ├── Memento.App/            WPF host: window, WebView2, theme following, single instance, tray, bridge wiring, crash handler
│   ├── Memento.Core/           Domain model, project store, library index, settings, model manager, processing orchestrator, bridge contracts
│   ├── Memento.Audio/          Capture (WASAPI, process loopback), level meters, streaming WAV writer, MF encode/decode, mixdown, peaks
│   ├── Memento.Transcription/  Whisper.net engine adapter, diarization adapter, engine catalog entries
│   ├── Memento.Documents/      Agenda parsers + OCR, document block model, templates, styles, DOCX/PDF/Markdown export
│   └── Memento.AI/             IAiProvider, Anthropic and OpenAI clients, payload composer, grounding validator
├── ui/                         Vite + TypeScript + Preact app (screens, components, bridge client, tokens.css)
├── tests/
│   ├── Memento.Core.Tests/     xUnit
│   ├── Memento.Audio.Tests/
│   ├── Memento.Documents.Tests/
│   ├── Memento.AI.Tests/
│   └── (ui tests live in ui/ with Vitest)
├── build/                      Velopack packaging script, icons, release notes template
├── .github/workflows/          ci.yml, release.yml
├── design/                     Design handoff (reference; read-only for builders)
└── docs/                       PRODUCT-SPEC.md, ARCHITECTURE.md, ROADMAP.md, user docs
```

Dependency direction: `App → Core ← Audio, Transcription, Documents, AI`. Feature projects depend on Core's interfaces; Core never references them. App composes everything with `Microsoft.Extensions.DependencyInjection` and `Microsoft.Extensions.Hosting`.

## 3. Host ↔ UI bridge

One channel: WebView2 `postMessage` in both directions, JSON only.

- **Request/response** (UI → host): `{ "id": 17, "method": "library.list", "params": { ... } }` answered by `{ "id": 17, "result": { ... } }` or `{ "id": 17, "error": { "code": "...", "message": "...", "detail": "..." } }`.
- **Events** (host → UI): `{ "event": "recording.levels", "payload": { ... } }`. High-rate events (levels, playhead, progress) are throttled on the host to ≤ 30 per second.
- Method names are `area.verb` (`library.list`, `recording.start`, `transcript.editSegment`, `settings.get`, `export.run`). Every method and event has a C# record in `Memento.Core/Bridge/Contracts/` and a matching TypeScript type in `ui/src/bridge/types.ts`. Keep the two in sync by hand and cover each with a serialization test.
- The host validates every incoming message; the UI never receives file-system paths it did not ask for and never receives API keys in any form (only "a key is saved" booleans).
- Dialogs that need the OS (file pickers, folder pickers, "open Windows settings") are host methods.

The UI is a single page. Navigation is in-page (hub and spoke per the design) and the Library's scroll position, filters and search are kept in UI state when a spoke opens.

## 4. Data on disk

Default library location: `%LOCALAPPDATA%\Memento\Library`. **Not** Documents, because on Windows 11 Documents is often synced to OneDrive, which would silently upload recordings. The location is changeable in Settings; moving the library is a copy-then-verify-then-delete operation with progress.

```
Library/
├── library.db                       SQLite index: recordings, people, FTS over titles/transcripts/people. Rebuildable.
└── projects/
    └── 20261006-100000-k3f9ab/      id = local date-time + 6 random base32 chars
        ├── project.json             manifest (schema v1): title, type, created (UTC + local offset), details, participants, agenda, tags, tracks[], status, integrity
        ├── tracks/
        │   ├── mic.flac             one file per source; .wav while recording, encoded at finalize
        │   └── system.flac
        ├── mix.flac                 playback/export mixdown, written at finalize
        ├── peaks.json               waveform peaks for the UI
        ├── transcript.json          segments, words, confidences, speakers, language, engine metadata
        ├── annotations.json         chapters, highlights, topics, notes (with origin: user / local / ai)
        ├── documents/
        │   └── <docid>.json         block content + generation record (template, style, provider, inputs, time)
        ├── templates/ styles/       project-local overrides, if any (global ones live under %LOCALAPPDATA%\Memento)
        ├── attachments/             imported agenda files and other attachments, original bytes
        ├── versions/                previous versions when history is on: <file>.<utc-stamp>.json
        ├── history.jsonl            append-only processing log (one JSON object per line)
        └── recording.state.json     exists only while a recording is in progress; drives crash recovery
```

Rules:
- `project.json` carries `schemaVersion`. Readers accept older versions and migrate forward; writers always write the current version. Unknown fields are preserved on round-trip.
- Writes are atomic: write to `<name>.tmp`, flush, then `File.Move(overwrite: true)`.
- Original tracks are never modified after finalize. Storage optimisation (downmix, lossy) writes new files and records the operation and the resulting hashes in `history.jsonl`; the user opts in.
- `integrity` in the manifest lists SHA-256 for every track and the mix, computed at finalize. Exports write a `manifest.json` beside the files with the same hashes.
- Timestamps are ISO 8601 with offset; durations are milliseconds; transcript times are seconds as decimals.

## 5. Recording pipeline

1. **Sources.** Enumerate: capture endpoints (microphones), render endpoints for system loopback, running processes with audio sessions for per-app loopback. Each enabled source becomes a **track** with its own capture client, ring buffer and writer.
2. **Capture.** WASAPI shared mode, event-driven, through one shared capture loop that records each packet's QPC and device-position timestamps. The endpoint mix format (float32 48 kHz on current Windows) is kept per track; resampling happens only at mixdown. Tracks share a session start time taken from QPC; each track's first packet is stamped so start-up latency (100–350 ms, different per stream) is corrected. Endpoint loopback produces no packets while nothing plays, so the writer inserts clock-timed silence to keep that track aligned. Drift (frames written vs clock) is measured and logged at every checkpoint.
3. **Writing.** Each track streams to `tracks/<source>.wav` as **int24 PCM** (float32 converted on the write path; it is what the FLAC encoder takes and saves 25% disk). Every **checkpoint** (30 s default): flush all writers to disk (`FlushFileBuffers`), patch the RIFF size fields, flush again, write `recording.state.json` (session start, tracks, bytes written, last checkpoint time). Writers also flush their managed buffer once per second so a process crash loses under 200 ms. Classic RIFF stops at 4 GiB, so a track rolls over to `tracks/<source>.part2.wav` at 3.5 GiB; finalize joins the parts into one FLAC. RAM use is bounded by the ring buffers.
4. **Pause/resume** stops writing but keeps capture clients alive; the gap is recorded in the manifest so transcript timings stay continuous.
5. **Source change mid-recording** starts or stops one track only. A lost device marks the track ended at that time and raises the toast; other tracks continue.
6. **Stop / finalize.** Close writers, encode each track to FLAC (or the user-selected storage format), write `mix.flac` and `peaks.json`, hash, update the manifest, delete `recording.state.json`, append `history.jsonl`, index in SQLite, then queue processing stages. The MF FLAC sink buffers the whole encode in `%TEMP%` and writes the output only at the end, so finalize checks free space there first and, if encoding fails for any reason, keeps the WAV tracks and reports it rather than leaving the project without media.
7. **Disk watch.** Free space is sampled every 5 s. Below the warning threshold (10 GB default) a banner appears and transcription pauses; if a write fails for lack of space the recording stops cleanly, everything written is kept, and the exact stop time is reported.
8. **Recovery.** On launch, any project with `recording.state.json` is repaired (RIFF headers rewritten from file length), finalized, and offered in the recovery dialog.
9. **Live transcript** (optional) runs on a separate low-priority worker over the mixed stream in 10 s windows with a small model; it is discarded when the full pass completes.

Storage format options (Settings › Recording): **Lossless FLAC** (default), or a smaller format after processing: AAC or MP3 at a chosen bitrate, optional mono downmix, optional "keep only the mix". The transcript is always produced from the lossless files before any lossy conversion.

## 6. Processing pipeline

A single **orchestrator** in Core runs stages per project: `Stored → Transcribe → Speakers → (Documents on demand)`. Each stage is an `IProcessingStage` with `RunAsync(project, progress, ct)`, is restartable, writes partial results, and appends to `history.jsonl` on start, progress milestones, completion and failure (with engine, model, device, duration).

- One GPU stage runs at a time. Transcription pauses when a recording is active and "pause when busy" is on, or when free space is low.
- Resource probe: GPU presence and VRAM (via Vulkan or DXGI), CPU load, RAM. The default model is the most accurate one that fits; a smaller model is offered, never applied silently.
- Failure keeps everything produced so far and offers the most specific remedy first (`Retry on CPU`, `Use the Medium model`).

## 7. Transcript model

`transcript.json` (schema v1):

```jsonc
{
  "schemaVersion": 1,
  "language": "en", "languageDetected": true,
  "engine": { "name": "whisper.cpp", "model": "large-v3-turbo", "device": "GPU (Vulkan)", "version": "...", "durationMs": 123456 },
  "speakers": [ { "id": "spk1", "name": "Speaker 1", "renamed": false, "color": 1 } ],
  "segments": [
    { "id": "s0001", "start": 12.40, "end": 15.92, "track": "system", "speaker": "spk1", "speakerConfidence": 0.82,
      "text": "…", "confidence": 0.91,
      "words": [ { "w": "…", "s": 12.40, "e": 12.71, "c": 0.97 } ],
      "edited": { "at": "2026-10-06T10:12:00+01:00", "original": "…" } }
  ],
  "reviewed": false
}
```

Low-confidence threshold for the dotted underline: word confidence < 0.6 (settable). Uncertain speaker: `speakerConfidence < 0.7`. Renaming a speaker changes `speakers[].name` only; segments reference the id.

## 8. Documents, templates, styles

- **Template**: ordered rows, each with 1–3 modules; per module: type, instructions, length, linkToTranscript. Plus inputs checklist, provider, default style, output options.
- **Style**: the Style editor settings (type, colour, structure, page). Built-ins Corporate, Minimal, Academic are presets.
- **Document**: a block model (`heading`, `paragraph`, `list`, `table`, `chips`, `labelValue`, `quote`, `timeline`) where any text run can carry a `t` timestamp reference (seconds) that resolves to a transcript moment. The viewer renders blocks to HTML with the style; DOCX and Markdown exporters consume the same blocks.
- **Generation**: the composer builds the payload from the ticked inputs only (never audio or video), shows it on request, sends it once, and expects structured output per module. The **grounding validator** checks every timestamp cites a real transcript time, that action items have an in-transcript source, and replaces unsupported claims with the spec's "not discussed / not reached / no owner was named" phrasing. The generation record stores the exact payload hash and, when "keep a record" is on, the payload itself.

## 9. Reliability rules every builder follows

1. Recording never waits on anything else. No stage, dialog or network call may block the capture threads.
2. Never delete or overwrite user data except through the designed Delete flow. Finalized tracks are read-only.
3. All file writes are atomic; all JSON has `schemaVersion`.
4. Every failure is specific: name the thing, the time or amount, what is safe, then the fix. No bare "Something went wrong".
5. Nothing leaves the PC without an explicit user action, and audio/video never leave at all.
6. Unhandled exceptions flush recording checkpoints before the process exits, and write a crash report to the logs folder.
7. No personal data in the repository: no real names, emails, paths with user names, keys, or recordings. Test fixtures are synthetic.
8. Every bridge method, parser, exporter and the grounding validator has unit tests. Long-recording soak tests run before a release.

## 10. Accessibility and theming

- The WebView2 content follows the Windows app theme (`UISettings.ColorValuesChanged`) and switches live by toggling the `dark` class on the root; Settings can force light or dark.
- All controls are real HTML controls with names, per DESIGN.md §Accessibility. Reduced motion follows the OS setting.
- Window minimum size 1024 × 700. The WPF window uses the standard Windows title bar.
