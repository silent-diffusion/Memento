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

## M3 — Details, agenda, settings, export — Done (0.4.0)

- Agenda import: drop zone, file picker, paste; parsers for docx, pdf, xlsx, csv, md, txt; OCR for images (Windows OCR default, Tesseract alternative via model manager); uncertainty marks with explanations; editable list; agenda panel in Recording and Review.
- All Settings sections complete per DESIGN.md §11, with every row's description.
- Export dialog and background export: audio FLAC/WAV/MP3, individual tracks, transcript JSON/Markdown/Text/SRT, details JSON, attachments; folder naming; remember choices; export manifest with hashes; inline failure state.
- Reprocess, rename and change-type flows from Review's More menu.

Acceptance: every agenda fixture in `tests/` parses to the expected items; exports open in common tools; a failed export leaves the project untouched.

Delivered with agenda import from files, photos (Windows OCR; Tesseract as the alternative engine) and pasted text into the Details sheet, the original kept as an attachment (`project.json` schema v2 types the attachment index), attachments in Review, "Import audio or video" (Media Foundation decoding to the normal stored and transcribed pipeline, with "Import again" after an import cut short), the Export dialog with several transcript formats at once and a SHA-256 `manifest.json`, a verified library move that refuses to run alongside a recording (and the other way round), and every Settings section. 0.3.0 was not tagged on its own and ships in this release. Deferred: Documents in the Export dialog and the AI features behind Settings › AI and privacy (M4; keys can be stored now, nothing is sent); keeping Memento in the tray (stored, applied in M5); removing old video (no video capture yet); the live transcript and video capture (as in M2). Checked end to end through the real UI with `tools/e2e/m3-flow.mjs` (a LibriVox public-domain MP3 imported and transcribed, a Word agenda with an uncertain item, two attachments, an export verified against its manifest, an unwritable destination, keys, a library move out and back, a 60-second microphone and system recording), and `m1-flow.mjs` and `m2-flow.mjs` again.

## 0.5.0 — first public release (M1 + M2 + M3)

Decided 2026-10-06: the first public release ships as soon as M3 is done, without AI documents, so that recording, transcription, review, agenda import and export are in people's hands while M4 is built. Its README and in-app copy say plainly that document generation arrives in a later version. The M5 hardening items that concern recording and transcription (device unplug, low disk, recovery, long recordings) are run before 0.5.0, not deferred to 1.0.

## H1 — Recording-side hardening before 0.5.0

Runs right after 0.4.0, because 0.5.0 is the first release people will rely on for real meetings.

- **Soak**: a 4-hour real-device recording (mic + system + one app) with 30 s checkpoints; an 8-hour simulated three-track recording; memory and handle counts flat; file rollover at 3.5 GiB exercised with a small threshold.
- **Device events**: unplug and replug a USB microphone mid-recording; change the default output device; a Bluetooth headset disconnecting; sleep and resume (modern standby) during recording and during transcription. Every case must keep the other tracks, report the exact time, and never crash.
- **Storage**: low-space banner and transcription pause at the threshold; a drive that fills during recording stops cleanly with everything kept; library on a removable drive that disappears.
- **Processing**: a 2-hour file transcribes end to end on GPU and on CPU; worker crash, cancel and busy-pause mid-window; interrupted model downloads resume; a corrupt model file is detected by hash and re-downloaded.
- **Recovery**: kill at every stage (recording, finalizing, optimize, transcript, speakers, import) and relaunch; each case ends in a consistent project with a History entry and the designed dialog or card.
- **Accessibility**: keyboard reach and names on every shipped screen; contrast check in both themes; reduced-motion run.
- **Docs**: `docs/USER-GUIDE.md` (install, first recording, sources, review, agenda, export, settings, where data lives, how to back up); in-app About with version, library path and the bundled licenses from THIRD-PARTY.md.
- **Security pass 1** (the parts of the audit below that touch shipped code: WebView2 and bridge hardening, parsers, downloads, updater, secrets, logs).
- Tag **v0.5.0** as a full GitHub release (not a pre-release) with the installer and notes.

## M4 — AI, documents, styles (0.6.0 → 0.9.0)

Split so that agents can run in parallel, each ending in a build:

- **M4a Providers and settings (0.6.0)**: `IAiProvider`; Anthropic and OpenAI clients (keys from DPAPI, TLS, retries, rate-limit handling, no key or content in logs); the **Local** provider as a `Memento.Worker` job (LLamaSharp Vulkan + CPU; catalog entries for Qwen3.5-4B and Ministral-3-3B with verified templates; VRAM budget, spill watch, warm-up, cancel, unload); Settings › AI and privacy fully live; the payload composer and "Preview exactly what will be sent".
- **M4b Document model and exports (0.6.0)**: the block model with timestamp runs; templates and styles storage with the three presets; Word export via Open XML (two-column rows, footnote timestamps, per-module text size), PDF via the viewer HTML through WebView2 `PrintToPdfAsync`, Markdown; the Full transcript module as data; the Export dialog's Documents row.
- **M4c Builder, Style editor and Document viewer UI (0.7.0)**: DESIGN §10, §12, §13 with drag and drop, rows, per-module instructions, length, text size and transcript linking, live preview skeletons, inputs and output tab, templates and styles manager, viewer with light editing, timestamp chips, "How this was made", versions.
- **M4d Generation pipeline (0.8.0)**: segment → map (grammar-constrained JSON) → reduce in code with citation repair → verify per claim → grounding validator → record; per-module runs with progress and cancel; agenda alignment; "not discussed" handling; the generation record in History; regenerate after corrections.
- **M4 integration (0.9.0)**: end to end with the Local provider on the reference laptop and with a cloud provider when the product owner supplies a key; quality checks on real recordings; copy and error states per §17.

## M5 — Hardening and 1.0.0

- Everything in H1 repeated on the full product, plus: document generation under low VRAM, provider failures (network down, invalid key, rate limit) with the designed inline cards, export of documents in every format, 1,000-recording Library and 10,000-segment transcript performance, templates with 20 modules.
- **Security audit 1** (full; see below) with every finding fixed or accepted in writing.
- Code signing: ask the product owner again before 1.0; wire Azure Trusted Signing or a certificate into `release.yml` if provided.
- Release checklist in `docs/RELEASING.md`; update channel verified from 0.5.0 → 1.0.0 on a clean machine.
- Tag **v1.0.0**.

## Security audit (before 1.0.0, repeated before 2.0.0)

Scope: the whole repository and the shipped installer, as a local-first desktop app that may hold privileged recordings. Deliverables: `docs/SECURITY.md` (threat model, what the app promises, how to report issues) and `docs/audits/SECURITY-AUDIT-<date>.md` (findings with severity, status, and the commit that fixed each).

1. **Threat model**: assets (recordings, transcripts, documents, keys), attackers (malicious files a user imports, a compromised model or update server, another local user, malware on the PC, a hostile transcript or agenda text aimed at the AI prompts), and what is out of scope.
2. **WebView2 and bridge**: CSP, virtual-host scope (`app.memento` read-only app files; `library.memento` limited to projects), navigation and new-window blocking, message origin checks, size and depth limits, every method's parameter validation, no host objects, dev tools off in Release, no remote content ever.
3. **File parsers and decoders**: DOCX/XLSX (zip bombs, external entities, macros ignored), PDF (malformed objects, huge pages), images (decompression bombs, EXIF), CSV (formula injection on export), media import (Media Foundation on hostile files), agenda text; size and time limits; fuzz the parsers with mutated fixtures.
4. **Downloads and updates**: model catalog URLs pinned to known hosts, HTTPS only, SHA-256 verified before use, partial files never loaded; Velopack feed integrity and what an unsigned installer means (documented honestly until signing exists).
5. **Secrets and privacy**: DPAPI usage, no keys in logs, exports, crash reports or settings; logs contain no transcript or document text; the "nothing leaves the PC" promise verified by capturing network traffic during a full session with AI off (expect none) and with AI on (only the chosen provider).
6. **Local AI**: prompt-injection resistance of the map and verify prompts (transcript text cannot change instructions or exfiltrate), the worker's job object and lock, no shell or file access from model output.
7. **Native interop**: review every P/Invoke and COM call (buffer sizes, lifetimes, error paths), worker protocol parsing, PE import scan of shipped DLLs.
8. **Supply chain**: `dotnet list package --vulnerable`, `npm audit`, pinned versions and lockfiles, Dependabot, an SBOM (CycloneDX) attached to each release, license re-check.
9. **Filesystem**: path handling for export and library move (no traversal, long paths, reserved names), atomic writes, permissions of the data root, safe deletion only through designed flows.
10. **Process**: findings triaged by severity; high and critical fixed before the release; a regression test per fix where possible.

## 2.0.0 — Video and the deferred features

Start right after 1.0.0 ships. Design work comes first, as new artboards in `design/` following DESIGN.md §18.

- **Video capture**: display and window capture via `Windows.Graphics.Capture`, camera via `Windows.Media.Capture`, H.264 + AAC into MP4 through the Media Foundation sink writer, written with the same checkpoint discipline as audio (fragmented MP4 so a crash keeps everything up to the last fragment), synchronized with the audio timeline, selectable per source in the Recording session's VIDEO section, picture-in-picture camera option, storage settings "remove video older than", video in the Review player, MP4 export and the Export dialog's Video row, Library grid video strips. Hardware encoder when available, software fallback, resolution and frame-rate settings.
- **Deferred from 1.0**: live transcript during recording (small model, 10 s windows, never competing with the full pass), remember renamed speakers by voice (embedding enrolment and matching across recordings, opt-in), chapter suggestions from topics and silence, keep-only-mix storage option, keep running in tray and start with Windows, Tesseract OCR as the installable alternative engine with per-word confidence, multi-select and bulk export or delete, context menus, small-window layouts below 1024 px.
- **AI**: additional providers behind the same interface; follow-up email module; "Preview what will be sent" as a full read-only sheet.
- **Security audit 2** on the 2.0 surface (video pipeline, camera and screen permissions, new codecs).
- Calendar integration for prefilled titles and participants is a 2.x candidate, not a 2.0 commitment.
