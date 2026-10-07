# Memento release notes

Each release has a `## <version>` section. `build/pack.ps1` puts the section for the version being
packed into the installer package, and the release workflow uses it as the GitHub release text.

## 0.3.0

Transcription and speakers (milestone M2). Memento now writes down what was said, on your PC.

- After you stop, each recording is transcribed on this PC: on the graphics card when there is one (Vulkan), on the processor otherwise. Nothing is uploaded. Every word keeps its timing and how sure the engine was, and words it was unsure of get a dotted underline.
- Speakers are told apart and labelled Speaker 1, 2, 3. Rename a speaker once and every line updates; move a line to another speaker, add a new one, or merge two. If you know how many people spoke, set it in Settings › Speakers and Memento groups the voices to that number, even when the microphone also picked up the loudspeakers.
- Review shows the transcript beside the player and follows the playhead. Click a line to play it, double-click to correct it (the original is kept), search it, and mark it as reviewed. Highlights you mark while recording are attached to their lines, and topics are suggested from the words.
- Where a track had speech but the transcript has nothing for ten seconds or more, Review says so at that place and offers to transcribe it again with another model. Lines the engine repeats in a loop are dropped, and History says what was dropped.
- Transcribe again with another model or language whenever you like. With version history on, the earlier transcript is kept, and you can restore any kept version from Details.
- The Library search now finds words in transcripts and shows the words around the match.
- Download transcription and speaker models in Settings › Transcription and Settings › Speakers, with their size shown first, progress while they download, and a check that the file is exactly the one published. Recordings made before a model was installed are transcribed as soon as it is.
- Transcription waits while you record or the PC is busy, and continues where it stopped; it never slows down a recording. If it fails, Review says what happened, what was kept, and offers the most specific fix first, such as Retry on CPU.

Not in this version yet: importing agenda files, export, documents and minutes, AI features, a live transcript while recording, and video capture.

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
