# Memento release notes

Each release has a `## <version>` section. `build/pack.ps1` puts the section for the version being
packed into the installer package, and the release workflow uses it as the GitHub release text.

## 1.2.0

Editing on the spot, undo everywhere, a window into the model's work, and a graphics card that explains itself. Memento updates itself to it.

### Review

- **The speaker menu searches and scrolls.** Clicking a line's speaker opens a menu with a **Find or add a speaker** field at the top. Typing filters the list (names that start with the text first; case and accents ignored), the list scrolls inside the window instead of running off it, and the arrow keys, Page Up and Page Down, Enter and Esc work. The last row, **Add "…" as a new speaker**, creates the speaker and gives it the line. The People list's merge menu is the same menu.
- **Correct a line where you read it.** Clicking a line's words edits them in place with the caret where you clicked; Enter or clicking away saves (the original wording is kept, as before), Esc cancels. Clicking the line's time plays it; double-click and F2 still edit. Playback never scrolls away from a line you are editing.
- **Highlights and chapters are renamed in place**: click the name in the outline, or a highlight's note under its line. Outline rows also get a remove button.

### Undo

- **Undo and Ctrl+Z work everywhere.** An **Undo** button in the Review, Builder and document viewer headers, with Ctrl+Z to undo and Ctrl+Y or Ctrl+Shift+Z to redo. One step at a time, up to 50, kept per recording, template or document. In Review: line edits, moving a line to another speaker, adding, renaming and merging speakers (an undone merge brings the speaker back with its name and colour and gives back its lines), highlights and chapters (add, rename, remove), topics, participants' names, tags and Mark as reviewed. In the Builder: adding, moving and removing sections, rows, every section setting (a run of typing is one step) and the template's settings. In the viewer: paragraph edits and the document's name. A text field keeps the keys for its own undo while you are typing in it. The status line says "Undone: merge speakers"; if the host refuses a step, it says why and the step stays.

### Documents

- **Show live output.** While a document is generated, a **Show live output** button sits beside Cancel. It opens **Live output**: the passes in order (segment, map, reduce, verify, grounding) on the left, and for the chosen pass the exact request sent and the reply arriving token by token, with the token count and tokens per second. It follows the newest output and pauses when you scroll up or pick a pass; **Follow live output** resumes. Claude and ChatGPT replies arrive whole, and the sheet says so. The sheet stays available on the failure card and in the viewer after the generation, until you leave the document; the text is kept in memory only and nothing new leaves the PC. Streaming costs nothing measurable: the worker sends at most one line every 40 ms and the window gets about ten updates a second.

### Graphics card

- **Memento now says who is using the graphics card.** The reading was right all along (within a few megabytes of the driver's own figure), but a card held by another program read as "0.1 GB free" with no explanation, so the local model ran on the processor for no visible reason. Settings › Transcription, Settings › AI and privacy and the Builder's Local card now say, for example: "The graphics card has 0.8 GB of 6 GB free. Ollama (llama-server.exe, started by LocalDictation) is using 5.0 GB. Qwen3.5 4B needs 3.6 GB on the card, so it runs on the processor until that memory is free." Memento's own processes count as one "Memento", and a program started by another names it. **Check again** re-reads the card without a restart, so you can close the other program and see the memory come back.
- Settings re-reads its data every time it opens, so the model in effect is never stale.
- Laptops with hybrid graphics: the card is told from the integrated chip by the driver's own flags, so a chip given 2 GB or more by the firmware is no longer taken for a second card.

## 1.1.0

A small release of fixes and refinements asked for after 1.0.0. Memento updates itself to it; nothing needs to be reinstalled.

### Fixed

- **The local model you installed is the one that writes documents.** With Qwen3.5 4B installed and Ministral 3 3B not, Settings and the Builder could still name Ministral as the model, say it was not installed, and refuse to use Qwen. The model in effect is now always an installed one: the one you chose, else the recommended one, else whichever local model is installed. A graphics-card model without room on the card gives way to an installed processor model, or else runs on the processor, and Settings › AI and privacy now says which model is in use, where it runs and why.
- **The same for transcription.** With only Large v3 Turbo installed and another program holding the graphics card's memory, transcription waited for the Small model and said it was not installed. When no model is chosen in Settings, the installed model is now used, on the processor when it does not fit on the card. A model you chose yourself is still waited for.
- **Templates can be chosen, saved and found again in the Builder.** Create document opens the Builder with a **Template** list in its header: the built-in templates first, then your own. Choosing one opens it (with unsaved changes, Memento asks first). **Save template** keeps the template you are on (a built-in is saved as a copy) and **Save as new template** always makes another one. A new template is given a name no other template has, so saving twice no longer makes two with the same name.

### Review

- **The outline, the transcript and the details now scroll on their own.** On a window 1024 pixels or wider the player stays in place, the transcript scrolls under it, and the speakers and outline on the left and the details on the right each scroll separately. On a narrower window the panes stack and the page scrolls, as before. Menus inside the panes open above their trigger when there is no room below and are never cut off.
- **Skip silences** in the player, after the speed control, jumps over every pause longer than a second and a half between transcript lines, landing just before the next line. The skipped stretches are shown dimmed on the waveform, and "Skipped 4 s" appears briefly where playback landed. It never skips while you scrub or just after you seek by hand, it needs a transcript, and Memento remembers whether it is on.

### Documents and settings

- **Audio and video are shown as never sent.** In the Builder's "What the AI receives" and in Settings › What may be shared, Audio and Video are greyed rows with a lock and "never sent" instead of boxes that could not be ticked.
- **Sections already on the template are greyed in the Builder's palette**, marked "in use", and can still be dragged or added again for a second copy.

## 1.0.0

The first public release. Memento records meetings, interviews, lectures and dictation, transcribes them on your PC, tells the speakers apart, imports agendas, exports exactly what you choose, and now turns a recording into minutes, summaries and other documents in which every statement points back to the moment it was said. Everything from 0.2.0 to 0.4.0 is in it. Versions 0.5.0 and 0.9.0 were never released on their own: what they were going to bring is in this release, together with a full security review.

**If you have 0.2, 0.3 or 0.4 installed**, download and run this Setup once: those versions never check for updates. From 1.0.0 on, Memento updates itself.

### Documents

- **Create document** in Review opens the **document builder**. Start from a template (Meeting minutes, Interview notes, Lecture summary, Dictation clean-up) or build your own from 23 sections: summary, decisions, action items with owner and deadline, agenda, discussion, open questions, quotes, timeline, next meeting, your own text, a section written to your own instructions, and the **Full transcript**. Drag sections into rows of up to three side by side, give each one instructions and a length, choose whether it links its statements to the transcript, and set its **text size** (smaller, normal or larger). The preview beside it updates as you go, and a template can be saved for every recording.
- **You choose who writes it.** Three providers:
  - **Local model** (Qwen3.5 4B on a graphics card, or Ministral 3 3B on the processor), downloaded once in Settings › AI and privacy. It runs on this PC and sends nothing anywhere. It is good for a first draft with citations and review: every statement carries its timestamp and goes through the checks below, but read the document before you rely on it. A meeting takes a few minutes on a laptop graphics card (about two and a half minutes for twenty minutes of meeting on the reference laptop) and much longer on the processor.
  - **Claude** (Anthropic) and **ChatGPT** (OpenAI), with your own key. They stay off until you turn on **Allow external AI services**. Before anything is sent, **Preview exactly what will be sent** shows the text word for word, and with **Ask before every send** on (the default) Memento asks again, naming the provider, what is included and how much. Audio and video are never sent.
- **Checked against the transcript.** Each statement is checked against the part of the transcript it cites. What the transcript does not support is left out, a summary point that adds a detail nobody said is shortened to what was said, and an owner or deadline is kept only if someone named it. A section the meeting never got to says "Not discussed.", and agenda items nobody talked about say "Not reached". **How this was made** in the document lists exactly what was used, what was sent and where, what was checked and dropped, and that audio and video were not sent; History keeps the same record.
- **The document viewer** shows the result on paper. Click a timestamp to hear that moment in Review, edit a paragraph in place ("Saved" appears when it is stored), regenerate with changed instructions, and go back to any earlier version.
- **Styles** decide how documents look, never what they say: three presets (Corporate, Minimal, Academic) and your own copies, with typefaces, size, heading colour, numbered headings, table shading, spacing, Letter or A4 paper, page numbers and a running header.
- **Export** a document as **Word**, **PDF** or **Markdown** from the viewer or the Export dialog, with timestamps as footnotes in Word and PDF. A template can also keep Word, PDF and Markdown copies inside the recording each time it generates.
- Templates and styles are managed in **Settings › Documents**.

### Updates and help

- **Updates.** Memento checks this project's GitHub releases for a newer version when it starts and once a day, never while you record or while a recording is being processed, downloads it in the background (the status bar shows the progress) and offers **Restart to update**. Nothing is installed until you choose it, or until the next time Memento starts. **Settings › General › Updates** shows your version and the last check, has **Check now**, and lets you turn automatic checks off. A check sends nothing about you or your recordings.
- **About** in Settings › General: the version, where your library is, and the licenses of every component inside Memento.
- **A user guide** (docs/USER-GUIDE.md): installing, recording, review, speakers, agendas, documents, export, settings, where your data lives and how to back it up, and what to do when something goes wrong.

### Made dependable

Each of these was found by testing on a real PC, and fixed:

- A library on a USB drive that is not plugged in no longer stops Memento from starting. Memento says which drive it is waiting for, does not create an empty library in its place, does not record until the drive is back, and opens the library by itself once it is. If the drive goes away while you record, Stop ends the recording, says so, and the next start with the drive connected saves everything.
- The low-disk-space banner now shows on every screen, also on the Record screen, without a recording, and when Memento starts with a full drive.
- Model files are checked against their published fingerprint before they are used, not only after downloading (a file is checked once, and again whenever it changes). A damaged file is set aside, Review says so, and **Download … again** replaces it; transcription then continues by itself. An interrupted model download continues where it stopped.
- An export cut short by a crash or power cut is cleaned up at the next start like a failed one, and the recording's History says so.
- A recording whose saving was interrupted is now described as that in History, not as an interrupted recording.
- When the PC sleeps or Memento is frozen while recording, the microphone track now keeps the time it missed as silence, so it stays in step with the PC's sound and the apps instead of sliding earlier.
- When the PC keeps getting busy while a recording is transcribed or its speakers identified, Memento now waits longer each time before it starts again, so the work gets done instead of restarting every half minute, and History says once that it started.
- Starting a recording with other sources than last time no longer forgets the recording type you picked, and no longer puts back settings that were changed since the Record screen opened (such as the storage format).
- Opening the Record screen again in the moment a recording finished saving no longer leaves it showing "Finalizing…" for good.
- Closing Memento while it was still sending progress to its window no longer leaves a crash report behind.
- Only one Memento runs for each data folder.
- Text and labels meet a 4.5:1 contrast in both themes, button labels on the orange buttons in the dark theme included. With Windows' animations turned off, buttons no longer shrink when pressed and the processing dot stops pulsing. Every control can be reached with the keyboard and has a name; two outputs or two microphones now have switches with different names.

### Security

Before this release the whole app went through a security review (docs/audits/SECURITY-AUDIT-2026-10-07.md); every finding was fixed or is explained there. What changed for you:

- **Files you import or attach** cannot harm Memento or your PC. Word, Excel, PDF and image agendas that would unpack or render to gigabytes, nest too deeply or take too long are refused with a clear message, and no agenda reader runs longer than a minute. An audio or video import that would fill the library drive, or that claims an impossible format, is refused before anything is written.
- **Recording folders copied in from elsewhere** cannot make Memento read, change or delete files outside them, and an export never writes outside its folder or over an existing file.
- **Attachments** open with their app only when they are documents, images or media; anything else opens its folder, so you decide. Copies keep Windows' "downloaded from the internet" mark, so Office still opens them in Protected View.
- **The window** cannot ask for your microphone, camera or any other permission, cannot start a download and never loads anything from the internet. Its crash reports stay on this PC.
- **Models** download only from the sites that publish them and are checked before use. **AI keys** are encrypted for your Windows account, never written to a log, and saving one never loses the other. Requests to Claude and ChatGPT never follow a redirect elsewhere.
- **AI and your text.** The transcript, agenda and instructions are handed to the AI as material, never as orders, so a sentence in a meeting cannot change what the AI is told to do; an owner or deadline is kept only when a person who was there is named.
- **The helper program** that runs transcription and the local model is stopped when it hangs or crashes, instead of holding everything up.
- **Releases** are built with read-only access and published in a separate step. Each release lists the SHA-256 checksum of every file (`SHA256SUMS.txt`) and the components it contains (SBOM files).

Good to know:

- **The installer and updates are not code-signed.** Windows SmartScreen will warn the first time; choose **More info**, then **Run anyway**, only if you downloaded Setup from this project's Releases page. To check a download, compare `Get-FileHash MementoApp-win-Setup.exe` with `SHA256SUMS.txt` on the release page.
- **The update check is the one connection Memento makes by itself**: a request to GitHub for the list of releases when Memento starts and once a day, never while you record or while a recording is processed. Turn it off in Settings › General › Updates. Model downloads and AI providers connect only when you start them.
- **The Microsoft Edge WebView2 runtime**, which draws Memento's window, makes its own connections to Microsoft (runtime updates and configuration). Memento cannot turn them off; they carry nothing from your recordings, because the window never loads anything from the internet.
- **The local model is good for a first draft with citations and review.** Every statement points to its moment in the recording and is checked, but meaning can still come out wrong; read a document before you rely on it.

### Tested for this release

A 4-hour recording of a microphone, the PC's sound and an app with checkpoints every 30 seconds; an 8-hour simulated three-track recording; tracks rolling over to a second and third file; a microphone disabled and enabled mid-recording; the default output switched mid-recording; Memento frozen for two minutes while recording and while transcribing; low and full disks; a library drive that disappears; a 2-hour recording transcribed on the graphics card and on the processor; transcription paused, cancelled and its engine killed mid-way; Memento killed while recording, saving, transcribing, identifying speakers, finding topics, making files smaller, importing, exporting and moving the library; an update from a local feed installed through the app; documents generated with the local model on the graphics card and on the processor, edited, regenerated, restored and exported as Word, PDF and Markdown; Claude refusing a wrong key from a test server; the agenda readers fed thousands of damaged files; and the network watched during a whole session (with AI off, Memento itself connected nowhere). The numbers are in docs/ROADMAP.md.

### Known limitations

- In a 4-hour recording made while another program kept the processor at 100% the whole time, the microphone track ended about 0.7 seconds out of step with the others. This was not seen without that outside load.
- A live transcript while recording, remembering speakers by their voice, chapter suggestions and video capture come in 2.0.
- Claude and ChatGPT have so far been tried only against a test server, not with a real key.
- **Keep running in the tray** is stored but not applied yet; closing the window closes Memento.
- There is no button to stop a transcription that is running; it pauses by itself while you record and continues where it stopped.
- English is the only interface language.

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
