# Memento

Local-first recording, transcription and meeting workspace for Windows.

Memento records meetings, interviews, lectures and dictation from any combination of microphones, system audio and individual applications, keeps every source as its own synchronized track, transcribes on your PC, identifies speakers, and turns the result into documents you can trust. Everything stays on your computer unless you choose to export it. External AI is optional and off by default.

> **Status:** pre-release. Memento is being built milestone by milestone; see [docs/ROADMAP.md](docs/ROADMAP.md) for what each version contains. The first public release will be `1.0.0`.

## Install

1. Open the [Releases](https://github.com/silent-diffusion/Memento/releases) page.
2. Download `MementoApp-win-Setup.exe` from the latest release.
3. Run it. Memento installs for the current user, needs no administrator rights, and updates itself from future releases.

Requirements: Windows 11 (Windows 10 version 2004 or later should also work), 64-bit. A GPU speeds up transcription but is not required.

The installer is not yet code-signed, so Windows SmartScreen may show a warning the first time. Choose "More info" → "Run anyway" if you downloaded it from this repository's Releases page.

## What it does

Version 0.4.0 records, imports, transcribes, plays back and exports:

- **Record** from microphones, everything the PC plays, or one application at a time, with each source saved as its own synchronized track and checkpointed to disk every 30 seconds. Pause, mark highlights with notes, and turn sources on or off mid-recording.
- **Never lose a recording**: a crash or power cut is repaired at the next start, and the recovery dialog says what was saved and what may be missing.
- **Store** every recording as lossless FLAC with a mix, a waveform and SHA-256 hashes, or choose smaller AAC/MP3 files that Memento converts and verifies after saving.
- **Transcribe** on your PC (Whisper, on the graphics card or the processor) with word-level timings and confidence marks, and tell speakers apart. Nothing is uploaded. Models are downloaded and checked in Settings.
- **Review** with a player, waveform, seeking, chapters, highlights, details and a full processing history, next to a transcript that follows the playhead: click a line to play it, double-click to correct it, rename speakers everywhere at once, search, mark as reviewed, and restore earlier transcript versions.
- **Find** recordings in the Library by title, people and words in their transcripts, filter by type, and delete them with a clear confirmation.
- **Import an agenda** from Word, PDF, Excel, CSV, Markdown, text or a photo of a printed page, parsed on your PC, with unsure items marked and explained; tick items off while recording. The original file is kept with the recording, and you can attach other files too.
- **Import existing audio or video** (the sound of a video) as a recording that is transcribed like any other; the original file is left as it is.
- **Export** exactly the parts you want: the mix or each track as FLAC, WAV or MP3, the transcript as JSON, Markdown, text or SRT, the details and the attachments, with a `manifest.json` of sizes and SHA-256 fingerprints.
- **Move the library** to another folder or drive, with every file checked before the old copy is removed; and complete Settings, including storage use and encrypted keys for the AI features to come.

Coming in later versions (see the [roadmap](docs/ROADMAP.md)):

- **Create documents** (minutes, summaries, action items and more) with a visual builder, where every statement links back to the moment in the recording. Requires an AI provider you enable and a key you supply.
- **Live transcript** while recording.
- **Video** capture.

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
| [docs/THIRD-PARTY.md](docs/THIRD-PARTY.md) | Bundled components and their licenses |
| [docs/SECURITY.md](docs/SECURITY.md) | Threat model, what Memento promises, how to report a vulnerability; audits in [docs/audits/](docs/audits/) |

## Privacy

Recordings, transcripts and documents are stored in a library folder on your PC (by default under your local application data, never in a cloud-synced folder). Memento makes no network connections except: downloading transcription or recognition models you choose to install, checking this repository for updates, and sending the inputs you tick to an AI provider you have enabled. Audio and video are never sent anywhere. The Microsoft Edge WebView2 runtime that draws the window has its own background connections to Microsoft (runtime updates and configuration); they carry nothing from your recordings. Report security problems privately through GitHub Security Advisories ([docs/SECURITY.md](docs/SECURITY.md)).

## License

[MIT](LICENSE)
