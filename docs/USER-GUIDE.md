# Memento user guide

Memento records meetings, interviews, lectures and dictation on your Windows PC, keeps each sound source as its own track, writes down what was said on your PC, tells the speakers apart, and lets you export exactly the parts you need. Nothing you record leaves your PC unless you export it yourself.

This guide covers version 0.5.0.

## Contents

1. [Install](#install)
2. [Your first recording](#your-first-recording)
3. [Choosing what to record](#choosing-what-to-record)
4. [During the recording](#during-the-recording)
5. [Review and the transcript](#review-and-the-transcript)
6. [Speakers](#speakers)
7. [Agenda and attachments](#agenda-and-attachments)
8. [Importing audio or video](#importing-audio-or-video)
9. [Export](#export)
10. [Settings](#settings)
11. [Updates](#updates)
12. [Where your data lives, and how to back it up](#where-your-data-lives-and-how-to-back-it-up)
13. [When something goes wrong](#when-something-goes-wrong)
14. [Privacy](#privacy)
15. [Keyboard](#keyboard)
16. [Known limitations](#known-limitations)

## Install

1. Open the [latest release](https://github.com/silent-diffusion/Memento/releases/latest) on GitHub.
2. Download `MementoApp-win-Setup.exe`.
3. Run it. Memento installs for your Windows account only, needs no administrator rights, and starts when it is done. If the Microsoft Edge WebView2 Runtime is missing, Setup installs it first.

You need Windows 11, or Windows 10 version 2004 or later, on a 64-bit PC. A graphics card makes transcription much faster but is not required.

The installer is not code-signed yet, so Windows SmartScreen may warn you the first time. If you downloaded it from the project's Releases page, choose **More info**, then **Run anyway**.

To remove Memento, use **Settings › Apps › Installed apps** in Windows. Uninstalling removes the program but keeps your recordings, settings and models (see [Where your data lives](#where-your-data-lives-and-how-to-back-it-up)).

## Your first recording

1. Start Memento. The Library opens; on the first run it is empty.
2. Choose **Start your first recording** (later: **New recording** at the top right).
3. Type a title over "Untitled meeting", or leave it. The type next to it (Meeting, Interview, Lecture, Dictation or one of your own) only helps you find recordings later.
4. Check the sources on the left: your microphone and "Everything this PC plays" are on by default. The level bars move when there is sound.
5. Press **Start recording**. It starts at once.
6. When you are done, press the round **Stop** button. Review opens while Memento saves the tracks, then the transcript is made on your PC.

Transcription needs a model. The first time, Memento shows where to install one: **Settings › Transcription**, where each model shows its size before you download it. Large v3 Turbo is recommended with a graphics card, Small without one. A recording made before a model was installed is transcribed as soon as it is.

## Choosing what to record

The **Audio sources** list on the Record screen offers:

- **Microphones**: every microphone Windows knows, including headsets.
- **Everything this PC plays**: the sound of your speakers or headphones, for example the other people in a video call.
- **Single apps**: one program at a time, such as Zoom, Teams or a browser. Only that program's sound is recorded, not notifications or music from other programs. An app appears in the list once it has played sound; press **Add** to look again.

Each source you turn on becomes its own track, and all tracks stay in sync. A typical call is your microphone plus the call app (or plus "Everything this PC plays"). Memento remembers your last choice.

The laptop microphone also hears your speakers. That is fine: both tracks are kept and transcribed. Use headphones if you want the microphone track to hold only your own voice.

## During the recording

- The big timer shows how long has been recorded. The footer says when the last checkpoint was saved: every 30 seconds by default, everything recorded so far is on disk and would survive a crash or power cut.
- **Pause** stops writing without ending the recording; **Resume** continues. The gap is remembered, so transcript times stay right.
- **Mark highlight** (or Ctrl+M) marks the moment; type a note if you like. Highlights show in Review and attach to the transcript line.
- Turn a source on or off at any time; the other tracks are not affected.
- **Details and agenda** opens the details: participants, purpose, platform, and an agenda you can tick off as you cover it.
- If a device disappears (a headset is unplugged, a microphone is disabled), only its track stops. A message names the device and the time, and the other sources keep recording. Turn the source on again when the device is back to continue it as a new track.
- If the drive runs low on space, a banner says how much is left; recording continues and transcription waits. If the drive fills up, the recording stops at a stated time and everything up to then is kept.

Transcription and other work never slow a recording down: while you record, they wait (if **Pause when the PC is busy** is on, which it is by default).

## Review and the transcript

Review opens after you stop, or when you open a recording from the Library.

- The **player** plays the mix of all tracks. Click the waveform to jump, or use the time slider (left/right arrows: 5 seconds; with Shift: 30 seconds).
- The **transcript** follows the playback. Click a line to play it; double-click to correct it (the original wording is kept). Words the engine was unsure of have a dotted underline.
- **Search** finds words in the transcript; the Library search also finds words across all recordings.
- **Mark as reviewed** when you have checked it.
- **Chapters and highlights** are on the left; add chapters by hand at the playback position. Topics are suggested from the words on your PC.
- **Details** and **History** are on the right. History lists everything that happened to the recording: when it was recorded, saved, transcribed, recovered, and why anything failed.
- Where a track had speech but the transcript has nothing for ten seconds or more, Review says so at that place and offers **Transcribe again** with another model. You can also transcribe the whole recording again with another model or language from the More menu; with version history on, the earlier transcript is kept and can be restored from Details.

If transcription fails, Review says what happened, what was kept (a partial transcript is never thrown away) and offers the most specific fix first, such as **Retry on CPU** or **Use the Small model**. **Details** opens the History.

## Speakers

After the transcript, Memento tells the voices apart and labels them Speaker 1, Speaker 2 and so on.

- Rename a speaker once and every line changes.
- Move a single line to another speaker, add a speaker, or merge two speakers into one.
- If you know how many people spoke, set **Expected speakers** in **Settings › Speakers**; Memento then groups the voices into that many speakers.
- Speaker identification runs on the processor and needs the speaker models from **Settings › Speakers**.

## Agenda and attachments

Open **Details and agenda** (while recording) or the Details tab in Review, then drop a file on the agenda area, choose one, or paste the text. Memento reads Word, PDF, Excel, CSV, Markdown and plain text files, and photos or scans of a printed agenda (with Windows' own text recognition), all on your PC.

Items Memento is unsure of are marked with the reason, for example a heading that may have run into the next item. Fix them in the list, reorder them, then press **Done**. While recording, tick items off as you cover them. The original file is kept with the recording as an attachment; you can add other files too (up to 100 MB each).

## Importing audio or video

**Import audio or video** (in the Library's More menu, or on the first-run screen) turns an existing file into a recording: WAV, FLAC, MP3, M4A, WMA, OGG and more, and the sound track of a video file. It is stored, transcribed and given speakers like any other recording. The original file is neither copied elsewhere nor changed.

## Export

In Review, choose **Export**. Tick what you need:

- the mixed audio, or each track, as FLAC, WAV or MP3;
- the transcript as JSON, Markdown, plain text or SRT subtitles (several at once);
- the recording details, and the attachments.

The files go into a folder named after the recording, with a `manifest.json` that lists each file's size and SHA-256 fingerprint, so you can check later that nothing changed. Progress shows in the status footer. A folder Memento cannot write to is refused before anything is written. If an export fails, is cancelled, or Memento closes in the middle of it, the files it had written are removed; the recording inside Memento is never changed.

## Settings

Open Settings with the gear at the top right. Every change is saved at once.

- **General**: theme (follows Windows by default), list density, start with Windows, where the library lives (moving it copies and checks every file before removing the old copy), language, **Updates**, and **About** (version, library location and the licenses of the components inside Memento).
- **Recording**: the default sources and recording type, how often a checkpoint is saved, the storage format (lossless FLAC by default, or smaller AAC or MP3 files made after processing), and the free-space warning level (10 GB by default).
- **Transcription**: transcribe automatically or by hand, pause when the PC is busy, the model, the model to use without a graphics card, the language, word timings and the uncertainty mark.
- **Speakers**: identify speakers, the expected number, and the speaker models.
- **AI and privacy**: external AI services, off. Document generation arrives in a later version; you can already store keys, which are encrypted for your Windows account and never shown again.
- **Documents**: version history for transcripts. Templates and styles arrive with document generation.
- **Export**: the default folder and what to include.
- **Storage and history**: how much space the library uses, its largest recordings, making older recordings smaller, and rebuilding the library list.

## Updates

Memento checks this project's GitHub releases for a newer version when it starts and then once a day, but never while you record or while a recording is being processed: it waits until both are finished. A new version downloads in the background (the footer shows the progress), and then a message offers **Restart to update**. Nothing is installed until you choose it; if you do not, the update is installed the next time Memento starts. Your recordings, settings and models are kept.

**Settings › General › Updates** shows your version and when Memento last checked, and has **Check now**. Turn off **Install updates automatically** if you do not want Memento to check by itself; **Check now** still works. A check sends nothing about you or your recordings: it is a plain request for the list of releases, the same as opening the Releases page in a browser.

## Where your data lives, and how to back it up

| What | Where |
|---|---|
| Your recordings (the library) | `%LOCALAPPDATA%\Memento\Library` unless you moved it (Settings › General shows where) |
| Settings | `%LOCALAPPDATA%\Memento\settings.json` |
| Downloaded models | `%LOCALAPPDATA%\Memento\models` |
| Encrypted AI keys | `%LOCALAPPDATA%\Memento\secrets.bin` (readable only by your Windows account on this PC) |
| Logs and crash reports | `%LOCALAPPDATA%\Memento\logs` (no transcript text, no names, no keys) |
| The program itself | `%LOCALAPPDATA%\MementoApp` (replaced by updates; your data is never kept there) |

Type `%LOCALAPPDATA%\Memento` in the File Explorer address bar to open the folder.

Inside the library, each recording is a folder under `projects` with its tracks (`tracks\*.flac`), the mix, the transcript, details, attachments and its history. The folders are complete on their own: `library.db` is only an index, rebuilt from them when needed.

**To back up**, close Memento and copy the whole library folder to another drive or a backup service. **To restore**, copy it back (or to a new PC) and, if it is not in the default place, choose that folder in Settings › General › Library location. Models can always be downloaded again; keys cannot be moved to another PC and must be entered again.

The default library is deliberately not in Documents, because Documents is often synced to OneDrive. If you move the library into a synced folder, your recordings will be uploaded by that service.

## When something goes wrong

- **Memento or Windows closed during a recording** (a crash, a power cut): at the next start Memento repairs the recording and shows **Recovered an interrupted recording**, with how much was saved, how many tracks are intact and what may be missing (at most a second or so after the last save). It never asks whether to keep it.
- **Memento closed while saving, transcribing or exporting**: saving and transcription continue where they stopped at the next start. A half-finished export is removed and History says so; export again from Review.
- **A device stopped**: only its track ends; the time is shown and the others continue.
- **The library's drive is not connected** (for example a USB drive): Memento says which folder and drive it is looking for and does not record until it is back. Plug the drive in and choose **Try again**, or choose another location in Settings › General.
- **A model file is damaged**: Memento checks every model before it uses it. A damaged file is set aside, and Review offers **Download … again**; transcription then continues by itself.
- **Transcription failed**: Review explains why and offers a fix. **Retry on CPU** works on any PC, just more slowly.

If something still goes wrong, the log in `%LOCALAPPDATA%\Memento\logs` helps to find out why. It contains file paths and engine messages but no transcript text, names or keys.

## Privacy

Recordings, transcripts and everything else stay in the library folder on your PC. Memento connects to the internet only to:

- download a transcription, speaker or text-recognition model you choose to install (from the addresses listed with each model);
- check this project's GitHub releases for updates and download them, unless you turn this off (see [Updates](#updates));
- send the inputs you tick to an AI provider you have enabled with your own key (not used in this version).

Audio and video are never sent anywhere. There is no account, no telemetry and no advertising.

## Keyboard

Everything can be reached with Tab and Shift+Tab; the focused control has a visible ring. On the Record screen, Space pauses and resumes and Ctrl+M marks a highlight; Esc does not stop a recording. In dialogs, Esc cancels. Memento follows Windows' "Show animations" setting: with animations off, buttons no longer move when pressed and the processing dot stops pulsing.

## Known limitations

- **No documents yet.** Minutes, summaries and other AI documents arrive in a later version; the settings for them are there and off.
- **No live transcript** while recording; the full transcript is made afterwards.
- **No video capture**; importing a video file uses its sound.
- **Keep running in the tray** is stored but not applied yet; closing the window closes Memento.
- There is no button to stop a transcription that is running; it pauses by itself while you record or the PC is busy, and continues where it stopped.
- "Everything this PC plays" records the output device that was in use when the recording started. If you switch Windows to another output device mid-recording, sound sent to the new device is not on that track (an app source follows the app wherever it plays).
- Speaker identification can split one person into two speakers or merge two similar voices; rename, move or merge them in Review.
- English is the only interface language. Transcription detects the spoken language, or use the language you set in Settings.
- The installer is not code-signed yet.
