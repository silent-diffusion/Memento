# Memento — Roadmap

Each milestone ends in an installable build from CI. A milestone is done when every acceptance item passes on a clean Windows 11 machine and the tests in CI are green.

## M0 — Scaffold and installer (0.1.0)

- Solution layout per ARCHITECTURE.md §2; `ui/` builds with Vite; the WPF host loads the built UI from the virtual host.
- Bridge in place with `app.version`, `settings.get/set`, `library.list` (empty), theme-follow events.
- Library screen in its **first-run** state, light and dark, from `design/renders/LibraryEmpty.dc.html`, with tokens.css and bundled fonts.
- Settings store, logging, crash handler, single-instance guard.
- Velopack packaging script; `release.yml` produces `Setup.exe` on a `v*` tag; `ci.yml` builds and tests on every push and PR.
- README with install, build and contribution instructions; MIT LICENSE; .gitignore; .editorconfig.

Acceptance: `Setup.exe` from CI installs on a clean machine without admin rights and opens to the empty Library in the Windows theme.

## M1 — Recording and Library — Done (0.2.0)

- Source enumeration (microphones, system loopback, per-application loopback), levels, toggles.
- Recording session screen (ready, recording, paused), timer, Mark highlight, footer status, Details and agenda sheet (details fields; agenda import comes in M3).
- Streaming multi-track WAV, checkpoints, pause gap, source loss handling, disk watch, finalize to FLAC with mixdown, peaks and hashes.
- Crash recovery dialog on launch.
- Library populated state: grouping, filters, search over titles and people, list and grid views, processing card, status pills, delete flow.
- Review screen: player, waveform, seek, chapters and highlights lists (no transcript yet), Details tab, History tab.
- Storage format options in Settings › Recording.

Acceptance: a 4-hour, three-track recording completes with bounded memory; killing the process mid-recording loses at most one checkpoint interval and recovers on launch; levels and timer never stall.

Delivered with two decisions: finalize always stores lossless FLAC, and a smaller AAC/MP3 choice is applied afterwards by a separate `optimize` stage that verifies the new files before removing the FLAC ones. Deferred: "Keep only the mix" is stored in Settings but not applied (separate tracks are always kept); the 4-hour real-device run moves to the M5 soak tests (M1 has a 30-minute three-track soak and the killed-process recovery check); "Import audio or video" on the empty Library arrives with M3.

## M2 — Transcription and speakers — Done (0.3.0)

- Model manager (catalog, download with SHA-256, install state, removal) and Settings › Transcription UI including model size, installed state and GPU/CPU.
- Whisper.net stage with word timestamps and confidences, Vulkan with CPU fallback, chunked long-form processing, pause when busy / low disk, restartable with partial results.
- Diarization stage, speaker merge across tracks, uncertain assignment marking, People list with talk-time share, rename propagating everywhere.
- Review transcript: follow playhead, click to seek, double-click to edit, low-confidence marks, speaker reassign, highlights with notes, chapters, topics (local keyword extraction), transcript search, mark as reviewed.
- Full-text search in the Library over transcripts.
- Version history for transcripts (Settings › Documents › History).
- Live transcript panel during recording (optional, rough draft).

Acceptance: a 2-hour meeting transcribes end-to-end on this machine's GPU and on CPU; edits persist and survive restart; renaming a speaker updates every segment within one frame.

Delivered with Whisper.net (Vulkan, then CPU) and sherpa-onnx diarization in a separate `Memento.Worker` process (one graphics-card job at a time, ended with Memento), coverage-gap notices from 10 s with "Transcribe again with another model", a filter for lines the engine repeats in a loop, per-window and per-track resume after a busy pause or a crash, and voices grouped across tracks when the expected speaker count is set. Deferred: the live transcript during recording (the Recording screen's card says it is off; the setting is stored), remembering renamed speakers across recordings (stored in Settings, not applied; with "Remember speakers by voice" in Later), and chapter suggestions from the transcript (chapters stay manual; topics are suggested locally). The 2-hour GPU and CPU runs move to the M5 soak tests; M2 was checked end to end with 2- and 3-minute real-device recordings and 5- and 13-minute files through `tools/TranscriptionCheck`.

## M3 — Details, agenda, settings, export (0.4.0)

- Agenda import: drop zone, file picker, paste; parsers for docx, pdf, xlsx, csv, md, txt; OCR for images (Windows OCR default, Tesseract alternative via model manager); uncertainty marks with explanations; editable list; agenda panel in Recording and Review.
- All Settings sections complete per DESIGN.md §11, with every row's description.
- Export dialog and background export: audio FLAC/WAV/MP3, individual tracks, transcript JSON/Markdown/Text/SRT, details JSON, attachments; folder naming; remember choices; export manifest with hashes; inline failure state.
- Reprocess, rename and change-type flows from Review's More menu.

Acceptance: every agenda fixture in `tests/` parses to the expected items; exports open in common tools; a failed export leaves the project untouched.

## 0.5.0 — first public release (M1 + M2 + M3)

Decided 2026-10-06: the first public release ships as soon as M3 is done, without AI documents, so that recording, transcription, review, agenda import and export are in people's hands while M4 is built. Its README and in-app copy say plainly that document generation arrives in a later version. The M5 hardening items that concern recording and transcription (device unplug, low disk, recovery, long recordings) are run before 0.5.0, not deferred to 1.0.

## M4 — AI, documents, styles (0.6.0 → 0.9.0)

- Settings › AI and privacy: enable switch (off by default), ask-before-send, keep-record, provider keys in DPAPI, sharing checklist. A **Local** provider that needs no key and never sends anything: its model is downloaded and managed in Settings through the model manager like the transcription models.
- `IAiProvider` with Anthropic, OpenAI and **Local** (an on-device LLM via llama.cpp with the Vulkan backend, model chosen by free VRAM; see ARCHITECTURE §8); payload composer; "Preview exactly what will be sent" (for Local it reads "stays on this PC").
- **Chunked, per-module generation with verification**: the transcript is split into chunks that respect chapter, topic and speaker-turn boundaries and a token budget; each module is generated in its own pass (map over chunks, then reduce) with only the inputs it needs; a separate **verification pass** asks the model to check every claim and citation of the module against the cited transcript spans and to drop or flag what is not supported; then the deterministic grounding validator runs. This pipeline is the same for every provider; for the cloud providers the chunk budget is simply larger.
- Document builder: palette, rows, drag and drop, keyboard fallbacks, module settings, live preview, inputs and output tab, save template, templates manager. Modules gain a **Text size** setting (Smaller / Normal / Larger, relative to the style's base size) that applies in the preview, the viewer and the Word and PDF exports. A **Full transcript** module places the entire transcript (speakers, timestamps, optional chapter headings) in the document as data, with no AI involved.
- Generation with structured output per module, grounding validator, timestamp references, generation record in History (including which chunks and which verification results produced each module).
- Document viewer with light editing, timestamp chips, How this was made, versions and restore, regenerate.
- Style editor with live sample page; Corporate, Minimal, Academic presets; duplicate and reset.
- Document export: Word (two-column rows, footnote timestamps), PDF via the viewer HTML, Markdown.

Acceptance: a document generated from a reviewed transcript has every decision and action item traceable to a transcript moment; unsupported items read "not discussed"; the DOCX and PDF match the preview; the Local provider produces minutes for a 1-hour meeting on the reference laptop without exceeding its VRAM and with every claim verified; a Full transcript module round-trips the transcript exactly.

## M5 — Hardening and 1.0.0

- Soak tests: 8-hour recording, low-disk simulation, device unplug, sleep/resume.
- Accessibility pass: keyboard reach, names, contrast, reduced motion.
- Performance pass: Library with 1,000 recordings, transcript with 10,000 segments.
- User documentation in `docs/`, in-app "About" with licenses of bundled components.
- Release checklist, update channel verified end-to-end through Velopack.

## Later (2.0+)

- Video capture: screen, window, camera; MP4 export.
- Additional AI providers.
- Remember speakers by voice across recordings.
- Calendar integration for prefilled titles and participants.
