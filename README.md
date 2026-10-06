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

- **Record** from microphones, everything the PC plays, or one application at a time, with each source saved as its own track, continuously checkpointed to disk.
- **Transcribe** on your PC with word-level timestamps and confidence marks, and identify who said what.
- **Review** with a player, waveform, editable transcript, chapters, highlights and notes. Low-confidence words and uncertain speakers are marked so you know where to look.
- **Import an agenda** from Word, PDF, Excel, CSV, Markdown, text or a photo, parsed locally.
- **Create documents** (minutes, summaries, action items and more) with a visual builder, where every statement links back to the moment in the recording. Requires an AI provider you enable and a key you supply.
- **Export** exactly the parts you want: audio, tracks, transcript, documents, details, with an integrity manifest.

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

## Privacy

Recordings, transcripts and documents are stored in a library folder on your PC (by default under your local application data, never in a cloud-synced folder). Memento makes no network connections except: downloading transcription or recognition models you choose to install, checking this repository for updates, and sending the inputs you tick to an AI provider you have enabled. Audio and video are never sent anywhere.

## License

[MIT](LICENSE)
