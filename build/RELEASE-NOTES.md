# Memento release notes

Each release has a `## <version>` section. `build/pack.ps1` puts the section for the version being
packed into the installer package, and the release workflow uses it as the GitHub release text.

## 0.5.0

The first public release. Memento records, transcribes on your PC, tells speakers apart, imports agendas and exports what you choose; everything from 0.2.0 to 0.4.0 is in it, and this release spent its time on making that dependable. Documents written with AI (minutes, summaries) come in a later version.

**If you have 0.2, 0.3 or 0.4 installed**, download and run this Setup once: those versions never check for updates. From 0.5.0 on, Memento updates itself.

New in this release:

- **Updates.** Memento checks GitHub for a newer version when it starts and once a day, never while you record or while a recording is being processed, downloads it in the background (the status bar shows the progress) and offers **Restart to update**. Nothing is installed until you choose it, or until the next time Memento starts. Settings › General › Updates shows your version and the last check, has **Check now**, and lets you turn automatic checks off. A check sends nothing about you or your recordings.
- **About** in Settings › General: the version, where your library is, and the licenses of every component inside Memento.
- **A user guide** (docs/USER-GUIDE.md): installing, recording, review, speakers, agendas, export, settings, where your data lives and how to back it up, and what to do when something goes wrong.

Hardening (each found by testing this release on a real PC, and fixed):

- A library on a USB drive that is not plugged in no longer stops Memento from starting. Memento says which drive it is waiting for, does not create an empty library in its place, does not record until the drive is back, and opens the library by itself once it is. If the drive goes away while you record, Stop ends the recording, says so, and the next start with the drive connected saves everything.
- The low-disk-space banner now shows on every screen, also on the Record screen, without a recording, and when Memento starts with a full drive.
- Model files are checked against their published fingerprint before every use, not only after downloading. A damaged file is set aside, Review says so, and **Download … again** replaces it; transcription then continues by itself. An interrupted model download continues where it stopped.
- An export cut short by a crash or power cut is cleaned up at the next start like a failed one, and the recording's History says so.
- A recording whose saving was interrupted is now described as that in History, not as an interrupted recording.
- Text and labels meet a 4.5:1 contrast in both themes, button labels on the orange buttons in the dark theme included. With Windows' animations turned off, buttons no longer shrink when pressed and the processing dot stops pulsing. Every control can be reached with the keyboard and has a name; two outputs or two microphones now have switches with different names.
- Security: an agenda file that would unpack to gigabytes is refused; a scanned PDF page is never rendered larger than 10,000 pixels; attachments open with their app only when they are documents, images or media (anything else opens its folder) and keep Windows' "downloaded from the internet" mark; an export can never write outside its folder, whatever a recording's files say; the page cannot ask for any permission or start a download; saving one AI key while the key file is locked no longer loses the other; an import that would fill the drive is refused first.

Tested for this release: a 4-hour recording of a microphone, the PC's sound and an app with checkpoints every 30 seconds; an 8-hour simulated three-track recording; tracks rolling over to a second and third file; a microphone disabled and enabled mid-recording; the default output switched mid-recording; Memento frozen for two minutes while recording and while transcribing; low and full disks; a library drive that disappears; a 2-hour recording transcribed on the graphics card and on the processor; transcription paused, cancelled and its engine killed mid-way; Memento killed while recording, saving, transcribing, identifying speakers, finding topics, making files smaller, importing, exporting and moving the library. The numbers are in docs/ROADMAP.md (H1).

Still missing: documents and every AI feature (their settings are there and off), a live transcript while recording, video capture, keeping Memento in the tray, and a button to stop a running transcription (it pauses by itself while you record and continues where it stopped). The installer is not code-signed yet, so Windows SmartScreen may warn the first time.

## 0.4.0

Agendas, attachments, importing and export (milestone M3). This release also contains everything listed under 0.3.0 below, which was not released on its own.

- **Import an agenda** in the Details sheet: drop a file on it, choose one, or paste the text. Word, PDF, Excel, CSV, Markdown and plain text are read on this PC, and so is a photo or scan of a printed agenda (with Windows' own text recognition). Items Memento is unsure of are marked with the reason, for example a heading that may have run into the next item; fix them in the list, reorder them, then press Done. The agenda shows in Review › Details with where it came from, and you can tick items off while recording.
- **Attachments**: the original agenda file is kept with the recording, and you can add other files (up to 100 MB each), open them and remove them from Review › Details.
- **Import existing audio or video** from the Library: WAV, FLAC, MP3, M4A, WMA, OGG and more, and the sound of a video file. The file becomes a recording that is stored, transcribed and given speakers like any other; the original file is not copied or changed. If Memento closes while it imports, the Library says "Import interrupted" and offers Import again.
- **Export copies** from Review: the mixed audio and each track as FLAC, WAV or MP3, the transcript as JSON, Markdown, text and SRT subtitles (several at once), the recording details and the attachments, in a folder named after the recording. A `manifest.json` beside the files lists each file with its size and SHA-256 fingerprint. Progress shows in the status bar, and a message offers Open folder when it is done. A folder Memento cannot write to is refused before anything is written, and the recording inside Memento is never changed.
- **Move the library** to another folder or drive from Settings › General: every file is copied, checked against its fingerprint, and only then removed from the old place. Recording waits while the library moves, and the move waits while you record.
- **Settings are complete**: General (start with Windows, library location), AI and privacy (external AI stays off; keys you add are stored encrypted for your Windows account and shown only as dots), Export (default folder and what to include), Storage and history (how much space the library uses, the largest recordings, making older recordings smaller, rebuilding the library list).
- Transcription on the graphics card no longer waits when the processor is busy with other programs (it still waits while you record, if you asked it to). Models the current settings need can no longer be removed by mistake; their cards say "Needed by the current settings".

Not in this version yet: documents and minutes, and every AI feature (the settings for them are there, off); a live transcript while recording; video capture; keeping Memento in the tray.

## 0.3.0 (not released on its own; ships in 0.4.0)

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
