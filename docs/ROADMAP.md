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

## M2 — Transcription and speakers (0.3.0)

- Model manager (catalog, download with SHA-256, install state, removal) and Settings › Transcription UI including model size, installed state and GPU/CPU.
- Whisper.net stage with word timestamps and confidences, Vulkan with CPU fallback, chunked long-form processing, pause when busy / low disk, restartable with partial results.
- Diarization stage, speaker merge across tracks, uncertain assignment marking, People list with talk-time share, rename propagating everywhere.
- Review transcript: follow playhead, click to seek, double-click to edit, low-confidence marks, speaker reassign, highlights with notes, chapters, topics (local keyword extraction), transcript search, mark as reviewed.
- Full-text search in the Library over transcripts.
- Version history for transcripts (Settings › Documents › History).
- Live transcript panel during recording (optional, rough draft).

Acceptance: a 2-hour meeting transcribes end-to-end on this machine's GPU and on CPU; edits persist and survive restart; renaming a speaker updates every segment within one frame.

## M3 — Details, agenda, settings, export (0.4.0)

- Agenda import: drop zone, file picker, paste; parsers for docx, pdf, xlsx, csv, md, txt; OCR for images (Windows OCR default, Tesseract alternative via model manager); uncertainty marks with explanations; editable list; agenda panel in Recording and Review.
- All Settings sections complete per DESIGN.md §11, with every row's description.
- Export dialog and background export: audio FLAC/WAV/MP3, individual tracks, transcript JSON/Markdown/Text/SRT, details JSON, attachments; folder naming; remember choices; export manifest with hashes; inline failure state.
- Reprocess, rename and change-type flows from Review's More menu.

Acceptance: every agenda fixture in `tests/` parses to the expected items; exports open in common tools; a failed export leaves the project untouched.

## M4 — AI, documents, styles (0.5.0)

- Settings › AI and privacy: enable switch (off by default), ask-before-send, keep-record, provider keys in DPAPI, sharing checklist.
- `IAiProvider` with Anthropic and OpenAI; payload composer; "Preview exactly what will be sent".
- Document builder: palette, rows, drag and drop, keyboard fallbacks, module settings, live preview, inputs and output tab, save template, templates manager.
- Generation with structured output per module, grounding validator, timestamp references, generation record in History.
- Document viewer with light editing, timestamp chips, How this was made, versions and restore, regenerate.
- Style editor with live sample page; Corporate, Minimal, Academic presets; duplicate and reset.
- Document export: Word (two-column rows, footnote timestamps), PDF via the viewer HTML, Markdown.

Acceptance: a document generated from a reviewed transcript has every decision and action item traceable to a transcript moment; unsupported items read "not discussed"; the DOCX and PDF match the preview.

## M5 — Hardening and 1.0.0

- Soak tests: 8-hour recording, low-disk simulation, device unplug, sleep/resume.
- Accessibility pass: keyboard reach, names, contrast, reduced motion.
- Performance pass: Library with 1,000 recordings, transcript with 10,000 segments.
- User documentation in `docs/`, in-app "About" with licenses of bundled components.
- Release checklist, update channel verified end-to-end through Velopack.

## Later (2.0+)

- Video capture: screen, window, camera; MP4 export.
- Additional AI providers; local LLM option through the model manager.
- Remember speakers by voice across recordings.
- Calendar integration for prefilled titles and participants.
