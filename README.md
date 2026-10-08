# Memento

Local-first recording, transcription and meeting workspace for Windows.

Memento records meetings, interviews, lectures and dictation from any combination of microphones, system audio and individual applications, keeps every source as its own synchronized track, transcribes on your PC, identifies speakers, and turns the result into documents you can trust. Everything stays on your computer unless you choose to export it. External AI is optional and off by default.

> **Status:** 1.0.0 is released: recording, transcription, speakers, agendas, import, export, documents and automatic updates are ready to rely on. What comes next is in [docs/ROADMAP.md](docs/ROADMAP.md) (2.0: video, a live transcript, speakers remembered by voice). The [user guide](docs/USER-GUIDE.md) explains everything step by step.

## Install

1. Open the [latest release](https://github.com/silent-diffusion/Memento/releases/latest).
2. Download `MementoApp-win-Setup.exe`.
3. Run it. Memento installs for the current user and needs no administrator rights. It checks for new versions, downloads them in the background and installs them when you restart it (see [Updates](docs/USER-GUIDE.md#updates)).

Requirements: Windows 11 (Windows 10 version 2004 or later should also work), 64-bit. A GPU speeds up transcription and the local AI model but is not required.

The installer is not yet code-signed, so Windows SmartScreen may show a warning the first time. Choose "More info" → "Run anyway" if you downloaded it from this repository's Releases page. To check a download, compare its SHA-256 (`Get-FileHash MementoApp-win-Setup.exe`) with `SHA256SUMS.txt` on the release page.

## What it does

Version 1.0.0:

- **Record** from microphones, everything the PC plays, or one application at a time, with each source saved as its own synchronized track and checkpointed to disk every 30 seconds. Pause, mark highlights with notes, and turn sources on or off mid-recording.
- **Never lose a recording**: a crash or power cut is repaired at the next start, and the recovery dialog says what was saved and what may be missing. A device that disappears ends only its own track; a full disk or a missing library drive stops cleanly with everything kept.
- **Store** every recording as lossless FLAC with a mix, a waveform and SHA-256 hashes, or choose smaller AAC/MP3 files that Memento converts and verifies after saving.
- **Transcribe** on your PC (Whisper, on the graphics card or the processor) with word-level timings and confidence marks. Nothing is uploaded. Models are downloaded in Settings and checked against their published SHA-256 before use.
- **Tell speakers apart** on your PC: rename a speaker once and every line follows, move lines, merge or add speakers, or set how many people spoke.
- **Review** with a player, waveform, seeking, chapters, highlights, details and a full processing history, next to a transcript that follows the playhead: click a line to play it, double-click to correct it, search, mark as reviewed, and restore earlier transcript versions.
- **Find** recordings in the Library by title, people and words in their transcripts, filter by type, and delete them with a clear confirmation.
- **Import an agenda** from Word, PDF, Excel, CSV, Markdown, text or a photo of a printed page, parsed on your PC, with unsure items marked and explained; tick items off while recording.
- **Attach files** to a recording (the agenda's original is kept automatically); documents, images and media open with their app, anything else opens its folder.
- **Import existing audio or video** (the sound of a video) as a recording that is transcribed like any other; the original file is left as it is.
- **Create documents**: minutes, summaries, action items, decisions, quotes, the full transcript and more, laid out in a visual builder from templates you can change, styled with presets or your own styles, and edited, regenerated and versioned in a document viewer. Every statement links back to its moment in the recording and is checked against the transcript; "How this was made" lists what was used and sent. Three providers: a **local model** that runs on your PC and sends nothing (good for a first draft with citations and review), or **Claude** or **ChatGPT** with your own key, off until you allow external AI, with a preview of exactly what will be sent.
- **Export** exactly the parts you want: the mix or each track as FLAC, WAV or MP3, the transcript as JSON, Markdown, text or SRT, documents as Word, PDF or Markdown, the details and the attachments, with a `manifest.json` of sizes and SHA-256 fingerprints.
- **Update itself** from this repository's GitHub releases: checked at start and once a day (never while recording or processing), downloaded in the background, installed when you restart. You can turn it off in Settings › General › Updates.
- **Move the library** to another folder or drive, with every file checked before the old copy is removed; and complete Settings, including storage use, encrypted AI keys and About with the licenses of every component.

Coming in 2.0 (see the [roadmap](docs/ROADMAP.md)): video capture, a live transcript while recording, speakers remembered by their voice across recordings, and chapter suggestions.

## Build from source

Prerequisites: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0), [Node.js LTS](https://nodejs.org/), Windows 10/11.

```bash
git clone https://github.com/silent-diffusion/Memento.git
cd Memento
cd ui && npm ci && npm run build && cd ..
dotnet build Memento.sln
dotnet run --project src/Memento.App
```

Run the tests:

```bash
dotnet test Memento.sln
cd ui && npm test
```

## Documentation

| Document | Purpose |
|---|---|
| [docs/PRODUCT-SPEC.md](docs/PRODUCT-SPEC.md) | What the application must do |
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | How it is built: stack, storage, pipelines, reliability rules |
| [docs/ROADMAP.md](docs/ROADMAP.md) | Milestones and acceptance criteria |
| [design/DESIGN.md](design/DESIGN.md) | Design handoff: tokens, components, every screen |
| [docs/USER-GUIDE.md](docs/USER-GUIDE.md) | How to use Memento: recording, review, documents, export, settings, backups, recovery |
| [docs/RELEASING.md](docs/RELEASING.md) | The release checklist |
| [docs/THIRD-PARTY.md](docs/THIRD-PARTY.md) | Bundled components and their licenses |
| [docs/SECURITY.md](docs/SECURITY.md) | Threat model, what Memento promises, how to report a vulnerability; audits in [docs/audits/](docs/audits/) |

## Privacy

Recordings, transcripts and documents are stored in a library folder on your PC (by default under your local application data, never in a cloud-synced folder). Memento makes no network connections except:

- **the update check**, the one connection Memento makes by itself: it asks this repository's GitHub releases for a newer version when it starts and once a day (never while recording or processing) and downloads it. The check sends nothing about you or your recordings. Turn it off in Settings › General › Updates ("Check now" still works);
- downloading a transcription, speaker, text-recognition or local AI model you choose to install;
- sending the inputs you tick to Claude or ChatGPT, only after you allow external AI services and supply your own key (the local model sends nothing).

Audio and video are never sent anywhere. There is no account and no telemetry. The Microsoft Edge WebView2 runtime that draws the window has its own background connections to Microsoft (runtime updates and configuration) that Memento cannot turn off; they carry nothing from your recordings, because the window never loads anything from the internet. Report security problems privately through GitHub Security Advisories ([docs/SECURITY.md](docs/SECURITY.md)).

## License

[MIT](LICENSE)
