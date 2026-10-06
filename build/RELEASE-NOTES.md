# Memento release notes

Each release has a `## <version>` section. `build/pack.ps1` puts the section for the version being
packed into the installer package, and the release workflow uses it as the GitHub release text.

## 0.2.0

Recording and the Library (milestone M1). Memento now records for real.

- Record from your microphone, everything the PC plays, and single apps such as Zoom or a browser, in any combination. Each source is saved as its own track, and all tracks stay in sync.
- Watch live levels for every source, pause and resume, mark highlights with a note, and turn a source on or off while recording without stopping the others.
- Everything is saved to disk as it records, with a checkpoint every 30 seconds. If Memento or Windows closes unexpectedly, the next start repairs the recording and tells you what was saved and what may be missing.
- If a device is unplugged, only its track stops; if the drive fills up, the recording stops cleanly and keeps everything up to that moment.
- When you stop, the tracks are stored as lossless FLAC with a playback mix and a fingerprint (SHA-256) of every file. In Settings › Recording you can choose smaller AAC or MP3 files instead; Memento converts them after saving, checks that they play, and only then removes the FLAC copies.
- The Library lists your recordings with search by title and people, type filters, list and grid views, and a card for the recording being processed. Deleting a recording asks first and says exactly what will be removed.
- Review a recording: play and seek it on its waveform, add chapters and highlights, edit its details, and see its full history.

Not in this version yet: transcription and speaker names, importing agenda files, documents and minutes, export, AI features, importing existing audio or video, and video capture. Their buttons say when they arrive.

## 0.1.0

First installable build (milestone M0). It sets up the foundation; recording and transcription come next.

- Installs per user with `Memento-win-Setup.exe`; no administrator rights. Installs the Microsoft Edge WebView2 Runtime if it is missing.
- Opens to the empty Library in your Windows theme, light or dark, and follows the theme when you change it.
- Shows where your data lives and how much space is free on that drive. Everything stays on this PC.
- Keeps settings in `%LOCALAPPDATA%\Memento\settings.json` and logs in `%LOCALAPPDATA%\Memento\logs`.
- Only one Memento runs at a time; starting it again brings the open window forward.

Not in this version: recording, transcription, import, Settings and documents. The buttons for them are in place but do nothing yet.
