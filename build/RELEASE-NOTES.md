# Memento release notes

Each release has a `## <version>` section. `build/pack.ps1` puts the section for the version being
packed into the installer package, and the release workflow uses it as the GitHub release text.

## 2.6.0

Speakers you can trust and fix fast (`docs/ROADMAP.md` 2.6.0): fewer wrong speaker assignments, and faster ways to correct the ones that remain.

### Correcting speakers

- **Move a whole section at once.** Picking another speaker for a line now offers, besides **Just this line** and the merge, **This turn · 5 lines** (the run of consecutive lines of that speaker around the line) and **From here on · 23 lines** (this line and every later line of that speaker, for correcting from the top down). Each says exactly how many lines move and from when to when, and while a choice is hovered or active the lines it would move are marked in the transcript, so the count can be checked by eye first. One Undo step each. The merge now shows its line count too.
- **Swap two speakers.** When Ana's lines are really Ben's and Ben's are Ana's, the same menu offers **Swap Ana and Ben from here on · 18 + 20 lines** and **Swap Ana and Ben everywhere · 41 + 37 lines**: the two trade their lines in one step, names and colours stay, and Undo swaps them back. With Remember speakers by voice on, both named voices are learned again from the lines they hold after the swap. History says "Speakers swapped · Ana and Ben from 12:30 · 38 lines".
- **Move the lines that sound the same.** Every line was heard as one raw voice, and a voice's lines are almost always one person, so when one of them is on the wrong speaker the others usually are too. The menu now offers **This line and the 11 that sound the same** (the other lines of the same voice that are not on the chosen speaker yet), marked in the transcript while the choice is hovered. One Undo step.

### Fewer wrong speakers

- **Lines are cut where the speaker changes.** A transcript line that held two people's words ("Yeah. — So what I think is…") used to go whole to whoever talked longest in it. A fresh transcript's lines are now cut at the word where the diarizer hears the voice change, so each part gets its own speaker; a part is never shorter than two words and half a second, the first part keeps the line's id, and a line you edited, or a transcript whose speakers were identified before, is never cut (highlights, chapters and documents point at lines by id). History says "12 lines split where the speaker changes". Settings › Speakers › **Split lines where the speaker changes** turns it off for new transcripts.
- **Your corrections guide the next pass.** A line you place on a speaker (from its menu, a selection, or a swap) is remembered as yours. **Identify speakers again** keeps those lines where you put them, and the voices under them are pinned to those people: voices you pinned to one person are joined first, no grouping ever puts two of your people together (even when the count asks for fewer), and a person you named keeps the name. So one corrected line per voice fixes every other line of that voice. History says "9 corrected lines kept, 3 voices pinned".
- **Speaking now.** While recording, each name under Who spoke has a **Speaking now** button: press it while that person talks and the moment is kept with the recording ("2 marks", with a × to clear them). When the speakers are identified, the voice heard at those moments is named after that person, ahead of the order-of-first-appearance guess; names you gave in the transcript before still win. History says "1 name from Speaking now".
- **Is the count right?** When a recording had no count or names in Who spoke, People now opens with "Grouped into 4 speakers by sound. Is that right?" (and "3 participants are listed" when they are), with **Yes, 4**, an "It was…" number that regroups the voices heard at once from `voices.json`, and **Not now**. A confirmed count is one Undo step.

## 2.5.0

### Your own server

- **Settings › AI and privacy › Your own server** points Memento at an OpenAI-compatible server you already run: Ollama, LM Studio, vLLM, llama.cpp's server, or anything else that answers `/v1/chat/completions`. Enter the address (`http://localhost:11434` for Ollama), press **Test connection** to see the server's name and its models, choose a model, set the context size to what the server actually runs (Ollama's default is 4096 unless `num_ctx` was raised; a transcript is split to fit a quarter of it), and add a key only if the server asks for one. The Builder offers it as a fourth provider card beside Claude, ChatGPT and the local model, and a template or the default provider can name it.
- **A server on this PC never leaves it.** A `localhost` address works with "Allow external AI services" off, is never asked about before a send, and "How this was made" and History say "Your own server · qwen3:8b at localhost:11434 (this PC)". A server on your network or elsewhere needs "Allow external AI services" on, gets the same confirmation as the cloud services, carries the "(on your network)" or "(remote)" label, and is reached over plain http only inside your own network; a public address must be https. Settings never contacts the server by itself; only Test connection and a generation you start do.
- **It speaks what the servers speak.** Requests stream, ask for a JSON schema when the server supports one, and fall back to plain JSON with the schema in the prompt when a server refuses schemas (vLLM and llama.cpp do), remembering that for the session. Answers wrapped in `<think>` blocks or code fences are unwrapped. Requests go one at a time, since most servers run a single slot.
- **Errors name the server and the fix.** "Nothing is listening at http://localhost:11434. Nothing was sent and no document was changed. Start Ollama or LM Studio, or check the address in Settings › AI and privacy." Likewise for a server that is not OpenAI-compatible, one that asks for a key, a model it does not offer (with Choose a model leading to Settings), a context the text does not fit (with the token counts), and a redirect, which Memento refuses as it does for the cloud services.
- **Tested against fakes only.** Three fake servers shaped like Ollama, LM Studio and vLLM/llama.cpp cover the model list, the schema fallback, usage chunks, `[DONE]`, keys, unknown models, redirects, oversized answers and context overflows; the whole-session network test proves an unconfigured server is never built or contacted, and a configured loopback server receives exactly one request per pass and nothing else. The m14 end-to-end check drives the published app against a fake server with a private data root. No real server or service was contacted.


## 2.4.0

A hardening release: the rest of the 2026-10-22 usability review's recommendations, and the quick items from security audit 2's follow-up list. No new screens.

### Buttons and the Builder

- **One ghost button recipe, three sizes.** Twenty per-screen button classes that each re-drew the ghost recipe at their own height are gone. Every ghost button now takes the base recipe at 36 px, or one of three documented sizes by role: 28 px inside a list row, 32 px for the main action of a card, 48 px on the Recording stage and the Library's centred states. The buttons that sat below the design's minimum are lifted: Restore a version (26 and 30 → 32 px), Accept and Dismiss a suggestion (28 → 32), Open an attachment (28 → 32), Show video (30 → 32), and the 34 px buttons and value selects in Settings, the model cards, About and the template chooser (→ 36). A test now fails if a screen declares its own ghost recipe again.
- **The contrast corrections are design tokens.** The `text-3` and `on-accent` colours the review corrected lived as an override in the app's stylesheet; they are now in `design/tokens.css` with their measured ratios, so the design tokens and the shipped app agree.
- **The Builder asks before Back drops template changes.** With unsaved changes to a template's structure or name, every way back (the back control, Esc, Alt+←, Browser Back, the mouse's back button) asks "Leave without saving the template?" with Keep editing, Save template and Leave without saving, as the Style editor already did. Without changes Back goes straight back. Esc works in the Builder again now that it asks.

### Supply chain and file safety

- **Locked dependencies.** Every project commits a NuGet lockfile and CI restores in locked mode, so a build uses exactly the packages that were reviewed. Every GitHub Action in the workflows is pinned to a commit, with the release tag beside it, and CI builds with one named .NET SDK (8.0.426) instead of "the latest 8.0".
- **Pinned model downloads.** The ten catalog entries that pointed at a repository's `main` branch now point at a commit, so an upstream re-upload cannot make an install fail its hash check; sizes and SHA-256 are unchanged, and a catalog entry with a moving revision is refused from now on. The two speaker models are GitHub release assets and keep relying on their size and hash.
- **Every file Memento reads keeps its defaults.** A probe over every settings, project, transcript, document, template, style, catalog, export and video file schema found 100 list or object fields in 75 types that read as empty-nothing when the field was missing, and 77 defaults that were lost the same way (the fault behind the 2.1.1 and 2.1.2 settings fixes). Every field now has its default, a null in a file is repaired to it on load, a null the page sends to the host is refused by name, and a reflection test fails the build if a future field forgets its default. A transcript version without its transcript and a voices file without its clusters are still refused by name rather than guessed at.
- **The worker's native libraries are scanned too.** The import scan that covers the engines' DLLs now also covers Tesseract and Leptonica in the worker, with the one accepted exception (Tesseract's debug viewer socket client) stated in the test.
- **Tesseract 5.5.3 is not yet available** as a NuGet build; the check date is recorded and the hash-pinned language data remains the mitigation. See `docs/audits/SECURITY-AUDIT-2026-10-11.md` §10 and `docs/audits/USABILITY-REVIEW-2026-10-22.md` for what was closed and what remains.


## 2.3.1

A review of how the screens look and how you move between them (docs/audits/USABILITY-REVIEW-2026-10-22.md), with every clear-cut finding fixed. Nothing moved; things now behave as the design always said.

### Buttons and theme

- **Every button follows the theme.** Several controls added since 1.0 had no styling of their own and drew as grey browser buttons: Review › Details' Move to folder… and Delete audio…, and Record's Resume recording, whose dot was invisible. Ghost and primary buttons now carry the full recipe everywhere. In dark, the record dots and the Stop square were white on light orange at about 2.5:1; primary hover and the destructive button carried a light-theme halo; the Builder's Add a module had lost its dashed outline. All fixed, with two new tokens for the accent highlight and the destructive colour.
- Nine small controls sit on the 12 px radius step, the folder tree, window picker and People pane buttons have token focus rings, dialogs and sheets have thin themed scrollbars, Settings' footer no longer wraps on a long library path, and a stylesheet lint now fails the build on raw colours, shadows or radii outside the tokens.

### Moving between screens

- **Back returns to where you came from.** Every link from one screen to another used to lose the way back: Builder → Choose a model → Settings → Back landed in the Library with the draft orphaned; a document's timestamp → Review → Back skipped the document; Regenerate → Builder → Back went to Review. Settings now returns to whatever opened it, Review to the document whose timestamp opened it, the Builder to the document whose Regenerate opened it, and everything else keeps its designed parent, so Back never walks through Record after Stop or the Builder after Generate. The back control names its target.
- **Esc goes back**, but only after menus, popovers, fields, sheets, dialogs, Review's filter or selection and fullscreen video have taken it; never on Record, from a text field, or from the Builder with unsaved changes. **Alt+←**, the keyboard's Back key and the mouse's back button do the same.
- **The gear reopens the Settings section you were in**, and the Library remembers its scroll position and the row you left.
- **Fixed:** the Builder's unsaved changes counted as saved after a trip to Settings or the Style editor, so Esc or a template switch could drop them silently. The tray's Record now goes through the normal route.
- Memento's own release checks no longer stall on a worker watchdog test that depended on timing.

## 2.3.0

### Larger local models, using RAM beside the graphics card

- **Settings › AI and privacy › Larger models** offers **Qwen3.5 27B** (dense, 16.7 GB) and **Qwen3.5 35B-A3B** (a mixture of experts, 22 GB), both Apache-2.0, downloaded once with a free-space check and a resumable download. They run on this PC even with a small graphics card: the planner places what fits on the card and the rest in RAM. For the mixture of experts the experts stay in RAM and attention runs on the card, which is what makes the card count; plain layer offload was measured and is not used for it.
- **Memory planning that stays out of the way.** Before a larger model loads, Memento reads the free video memory (less what other programs hold) and the free RAM, keeps headroom for Windows (2 GB), the app, a running recording (more with video) and a transcription that may be waiting for the card, and refuses plainly when the model does not fit: "Qwen3.5 35B-A3B needs 25 GB of memory; this PC has 19 GB free. Close other programs or choose a smaller model." No smaller model is swapped in. Transcription keeps the card first, as always; a recording that starts while RAM is short unloads the larger model and History says so; a spill while loading or writing moves more of the model to RAM and continues.
- **You see the plan before you use it**, under the model card and on the Builder's Local card: "Experts in RAM, the rest on the graphics card · 12 tokens/s · 19 GB of RAM while writing · minutes of a 20-minute meeting in about 18 minutes." "How this was made" and History record the placement used.
- **Honest numbers from the reference laptop (6 GB card, 40 GB RAM):** the 35B-A3B wrote the test meeting's minutes in about 18 minutes and the 27B in about 65 minutes, against two and a half minutes for Qwen3.5 4B, and neither scored higher on the grounding checks than the 4B. The notes in Settings say so. They are there for people who want to try them, or who have more RAM and a bigger card.

## 2.2.0

### Choose the model behind Claude or ChatGPT

- **Settings › AI and privacy** now lists the models each service offers, under its key, with a short note on each: for Claude, `claude-opus-5-5` (the default), `claude-fable-5-1` (Anthropic's top tier; costs more and needs their 30-day data retention), `claude-sonnet-5-5` and `claude-haiku-5-5`; for ChatGPT, `gpt-6-astra` (the default), `gpt-6.1-sol` and `gpt-6-luna`. **Other model…** takes an id typed by hand, saved as typed and marked as not in Memento's list.
- The chosen model is sent with every request and named everywhere a request is described: the Builder's provider card, Preview exactly what will be sent, the send confirmation, Live output, "How this was made" (a new Model row) and History.
- A model the service does not know gets its own answer, never a silent switch to another model: "Claude does not offer a model called 'x'. Nothing was generated and no document was changed. Choose one from the list in Settings › AI and privacy, or check the id." with **Choose a model** leading to Settings.
- Long transcripts fit every listed model: the pipeline already splits a transcript into pieces, now capped at a quarter of the chosen model's context when that is smaller.

### Fixed

- **Identify speakers again, Transcribe again and retries no longer race the pass they replace.** A stage that was stopped to run again could restart before its new settings were written, so a second pass ran with the old ones and a third followed; and a request that landed as a pass ended could be lost. The run now waits for the new settings and queues the recording again when a request lands late.
- **The Library's index can no longer go back to an older state.** A recording's row was rewritten from a manifest read moments before a change, so the Library could show stages the recording no longer had. Writes and their index updates now happen one at a time.
- **A source turned on mid-recording that had not delivered audio yet now starts at the moment it was added**, not at zero, in the live list, at checkpoints and in the saved recording; and stopping a late source no longer cuts up to 100 ms from its end.
- **Formatting marks in an agenda line are always stripped**, even when the PC stalls during the match; a match cut short by a stall is retried, while hostile text is still refused.
- **A project file held open by another program for a moment** (a virus scanner reading it, for instance) is now outlasted for up to 10 seconds before a save or a delete gives up, instead of 2.
- Memento's own test suite no longer depends on elapsed time, so releases stop waiting on reruns of a slow build machine.

## 2.1.2

- **Fixed: settings still could not be changed after 2.1.1 on a PC that had run 2.1.0.** 2.1.1 read a missing date format or library view as its default, but 2.1.0 had already written them into the settings file as empty text, and an empty value was still refused, so reinstalling did not help either. Memento now repairs an empty or unknown date format or library view to its default when it reads the file, and says so in the log. Your other settings are kept as they are.

## 2.1.1

- **Fixed: no setting could be changed after updating to 2.1.0.** Every switch answered with "Date format '' is not available. Choose …" or "Library view '' is not available", and the default recording type would not change. A settings file written by 2.0.0 has no date format or library view yet; those fields read as empty instead of their defaults, and since the page sends a whole block back on any change, every save was refused. Fields added in a new version now always read as their defaults from an older file. Nothing in your settings was lost; the file is read correctly as it is.

## 2.1.0

Small things that make the Library and recordings easier to live with. Memento updates itself to it.

### Library

- **New recordings carry their date in the title**: "Untitled meeting · 2026 February 16". **Settings › General › Date format** chooses how full dates are written everywhere in Memento: 2026 February 16 (the default), 16 February 2026, February 16, 2026, 2026-02-16, or the Windows setting. Existing titles and times are untouched.
- **Folders.** The Library gains a **Folders** view beside the Timeline, remembered as you left it: a nested folder tree with counts, New, Rename, Move and Delete (deleting a folder moves its recordings up a level and deletes nothing), drag a recording onto a folder or use **Move to folder…** from the row menu or Review's details, and search and the type filters still work inside a folder. Folders are data in the recording's file; nothing on disk moves.
- **Delete audio.** Like Remove video: from Review's details or the row menu, after a confirmation that names the recording, its length, what it frees and what stays (the transcript, documents and video). The recording stays readable and Export says the audio was deleted. Refused while the recording is still being saved or processed.
- **Settings › Storage and history › Delete audio tracks older than**: a day, a week, a month, 90 days, a year, two, three, four or five years, or Never (the default), with an estimate, the same confirmation, and a sweep at start and daily that never runs while recording or processing.

### Recording

- **Resume a stopped recording.** While a recording is saving, and from Review for any recording made today that still has its tracks, **Resume recording** continues it in the same project as a new segment. The earlier tracks stay exactly as saved and keep their hashes; new track files start with their own offsets, the real time in between is shown as a pause ("Resumed at 6:13 PM · paused 4 min") on the waveform, in Review's header and in the Library row, the mix is rebuilt over both segments, and the transcript and speakers run again over the whole recording with the earlier transcript kept as a version. A crash during the new segment repairs only that segment. Imported recordings, mix-only recordings and recordings whose audio was deleted cannot be resumed, and the reason is stated.

### Updates

- **Check now** now only checks, and says either "You are on the newest version (2.1.0)" or "2.2.0 is available" with that version's release notes shown in place. **Update now** then downloads with progress and offers **Restart to update**. The automatic daily check still downloads in the background as before.

## 2.0.0

Video. Memento now records your screen, a window or your camera beside the audio, keeps every frame up to the last third of a second if the PC crashes, plays it in Review in step with the transcript, and exports it as one file with the camera inset or as separate files. Everything from 1.1.0 to 1.3.0 is in it, and Memento updates itself to it. This is the 2.0.0-beta.1 pre-release plus what its first testing found:

### Fixed since the beta

- **A quiet microphone next to loud system audio was never transcribed.** The silence detector used a fixed loudness line, so a microphone 40 dB below a playing video was marked silent from start to finish and Whisper never heard it. Silence is now judged against each track's own noise floor, every window is brought to a common level before Whisper and the speaker identification hear it, and the stored recording is untouched. On a two-track test the quiet microphone went from 0 of 18 lines to 18 of 18 with both models, with timing unchanged. **Even out source levels** (Settings › Recording › Storage, on by default) lifts a quiet track in the mix you play, up to +24 dB, and Review › More › Reprocess can **Rebuild the mix** with evened levels or as recorded. The Record screen now says "Microphone is quiet (−40 dB). Move closer or raise its level in Windows; the transcript may miss you." under a quiet source. Recordings made earlier improve with **Transcribe again**. The live draft while recording still hears one mix, so it catches less of a quiet voice spoken over loud audio; the full pass after Stop gets it all.
- **Virtual cameras and other DirectShow cameras are listed and recorded**: the OBS Virtual Camera, NDI and similar software cameras and DirectShow-only capture cards were invisible, because only Media Foundation cameras were listed. They now appear in the picker with "via DirectShow", preview, record, and end their track cleanly when the virtual camera stops ("OBS Virtual Camera stopped at 0:42"). Tested against OBS 32: 30 frames per second for about 1% of the PC.
- **Camera recording did not work at all in the beta**, through any camera: the camera picture was created without render-target access and the first frame failed as an encoder error. Fixed.
- Memento no longer waits over 30 seconds to close while a camera thumbnail is open.

### Video

- **Record the screen, a window or a camera** from the Recording session's VIDEO section, with a live preview, a picker that shows every display and window as a thumbnail (windows that keep themselves out of captures are shown but not selectable), and a camera inset in the corner you choose. The Encoder card shows the encoder (your graphics card's, or the processor's when none is free), frame rate, dropped frames, bitrate and size. Turn a source on or off, or switch windows, mid-recording.
- **Nothing is lost on a crash.** Video is written as fragmented MP4 every third of a second and plays as written; a torn file is repaired at the next start (about 2 seconds for 20 minutes). On the reference laptop a 20-second desktop recording cost 0.8% of the PC with the graphics card encoding, and a real 4-hour file seeks to any frame in under half a second.
- **In step with the audio.** Video frames carry the same clock as the audio tracks; measured over 22 minutes, the video sat one frame behind the schedule with no drift. In Review the video pane follows the mix exactly, with Audio only, Show video and Fullscreen.
- **Export** the video as **One file** (the mix with the screen and the camera inset burned in) or **Separate files**, in MP4 or the original container, with sizes shown first.
- **Settings › Recording** has the video defaults (quality Smaller/Balanced/Best, frame rate, encoder preference, default sources). **Settings › Storage and history** shows how much video takes and has **Remove video older than**; **Remove video, keep audio** on any recording frees the space and History says so.
- **Specific errors**: screen capture blocked by policy, camera privacy off, camera in use by another app (Memento waits and starts the track by itself when it is free), encoder unavailable (continues on the processor), disk full (video stops, audio continues), window closed or minimised, protected content refused.
- Windows 11 is needed for recording without the yellow capture border; Windows 10 shows the border. There is no camera on the reference laptop, so camera recording is tested with fakes only in this beta: please report what you see.

### While recording

- **Live transcript** (off by default, Settings › Transcription): a provisional draft appears 10 to 20 seconds behind the recording, written by the Small or Base model on the processor (or the card when you allow it), never competing with the full pass, which replaces the draft after Stop. On the reference laptop it costs about 8% of the PC. The draft is never saved.
- **Keep only the mix** (off by default, Settings › Recording): after processing, the separate tracks are deleted and the mix kept, with the consequences stated; existing recordings can be slimmed from Settings › Storage.
- **Keep running in the tray** and **Start with Windows** (off by default, Settings › General). The tray shows a recording in progress; signing out still closes Memento.

### Speakers and transcript

- **Known voices** (off by default, Settings › Speakers › Remember speakers by voice): naming a speaker stores a voice signature on this PC, never audio. On a later recording a match prompt offers "Sounds like Sarah · 96% match · 2 past recordings" with **Use name** or **Not Sarah**; nothing is applied silently. Known voices can be switched off one by one, forgotten, or all forgotten, and a deleted recording takes its confirmations with it.
- **Suggested chapters** in the outline, worked out on the PC from topics, pauses and speaker turns, with accept, dismiss and Accept all, all undoable.
- **Select several lines** with Ctrl+click, Shift+click or Ctrl+A, then assign a speaker, highlight, copy or mark a chapter start from the selection bar or the right-click menu.

### Documents and agenda

- **Follow-up email** module: recipients from the participants, subject, summary, decisions and action items with owners, in a neutral, warm or brief tone with your sign-off, every statement checked against the transcript; exported as its own section and copied as an email.
- **Preview exactly what will be sent** is now a full read-only sheet: provider and model, the inputs with their sizes, every request as it will be sent, search, and copy.
- **Tesseract** is the installable alternative for reading agenda photos and scans, chosen in Settings › Documents once its English data is installed; it runs inside Memento's worker so a crash in it cannot touch a recording. Windows OCR stays the default and was more accurate and five times faster on the test pages.

### Security audit 2

- The whole 2.0 surface was audited (docs/audits/SECURITY-AUDIT-2026-10-11.md): no high findings; the mediums are fixed (bounds on the MP4 reader so one bad file never stops recovery of the others, encoder-fallback parts found after a crash, thumbnail memory caps, deletions that only touch what they own, hostile project and voice files set aside instead of stopping the library) or accepted in writing with the reason. A test now proves that a whole session with video, live transcript, known voices, Tesseract, generation, copies and exports makes no network request, socket connection or DNS lookup, and that no video ever appears in anything sent to a provider.

### Known limits of this beta

- Camera recording and mixed-DPI displays are untested on real hardware. Recordings transcribed before 1.3.0 keep their old line timing until transcribed again. The suggested chapter titles are rough. A screen track starts about a second after the audio while the encoder opens; the player accounts for it.

## 1.3.0

Speakers that stick to the number you set, a transcript that stays with the audio, filters, copying to the clipboard, and History you can step back into. Memento updates itself to it.

### Fixed: speakers stick to the number you set

- **No more 84 speakers.** The diarizer's voices are now grouped by Memento, on every track, to the number you set, joining the most alike voices first and never splitting one; voices with under ten seconds of speech never become a speaker of their own. On a 92-minute meeting with four named people, 84 speakers became 4 with 97.8% of their speech on the right person (63.6% before); on a 55-minute meeting, 35 became 3 on Auto and 4 with the count set. The diarizer's output is kept with the recording, so changing the count regroups in well under a second without listening again.
- **Who spoke, per recording.** Set **how many** people spoke and/or **their names** in the details before or after recording (with **Use participants** to take the participants list), and under People in Review. Names go to the speakers in order of first appearance, and names you gave earlier carry over to the speaker that took their lines. Settings › Speakers › Expected speakers remains the default for recordings without their own value.
- **Reduce to n speakers** in People merges the most alike voices (never two named speakers together), as one Undo step. **Identify speakers again** keeps your corrected transcript as a version first, so it no longer wipes your fixes.
- **Just this line or merge.** Choosing another speaker for a line now asks **Just this line** or **Merge {old} into {new}**; the merge is one Undo step.
- **Named speakers first** in every list: the People pane, the speaker menu and its search results, and the merge menu, each by first appearance.
- **Add from speakers** in the details fills the participants from the recording's named speakers.

### Fixed: the transcript stays with the audio

- **Lines now start where the voice starts.** The highlight used to run ahead of the audio by a few seconds and lose whole stretches after long silences. Measurement showed the player, the mix and the clocks were exact: Whisper was starting each line at the beginning of the pause before it, usually on a whole second, and after a pause of 20 seconds or more it could write one sentence over and over through the real speech, which the repeat filter then dropped along with the speech. The transcription worker now shortens every silence to under a second before Whisper hears it, cuts each window between sounds into pieces under 30 seconds so Whisper never breaks a sentence itself, maps the times back and snaps each line to where sound actually starts and stops. On a recording with known timings, 29 of 30 lines start within 10 ms of the voice; on a 55-minute meeting, line starts went from −3.5 to +7 s off to within −0.3 to +0.9 s, and speech with no transcript nearby fell from 14% to 0.3%. Transcription takes about 6% longer.
- **Recordings transcribed before 1.3.0 keep their old timing** until you choose **Transcribe again** from Review's More menu.

### History

- **Step back into a version.** In Review's History, every line that produced a kept version (a transcript after transcription, speakers, edits or a restore; a document after generation or edits) is now a button. It opens that version read-only under a banner such as "Transcript as of 5:30 PM, after speakers were identified", with **Restore this version** (one Undo step) and **Back to current** (or Esc). Lines that made no version, such as recorded, saved or exported, say so when hovered. A document line opens the viewer on that version, and the viewer's Versions list opens versions the same way. Recordings from earlier versions get the links too.

### Review

- **See only one speaker.** Click a speaker's name in People and the transcript shows just their lines, with the row pressed in and their line count. Click again, **Show all** or Esc to clear; Ctrl+click adds another speaker. The **Filter** chip in the transcript header combines speakers, **Highlights**, **Uncertain words**, **Edited lines**, one chapter's span and the search text, each with its count. A line reads "Showing 42 of 318 lines · Sarah · Highlights" with **Copy** and **Show all**. Hidden lines are only hidden: playback, seeking, the arrow keys, editing in place and Undo work on the lines shown.
- **Copy to the clipboard.** The More menu has **Copy transcript** (as text or Markdown; while filtered it copies the lines shown and says so), the Export dialog has **Copy to clipboard** for the transcript and for each document, and the document viewer's More menu copies the document as text and as formatted content that Word and Outlook paste with its headings. "Copied 42 of 318 lines as text" confirms it quietly. Nothing copied is ever uploaded by Windows' cloud clipboard.

### Export

- **The transcript with or without timestamps and speakers**, in any combination, plus a paragraph choice (as the format, speaker turns, or line by line), for the text and Markdown files and for the clipboard. JSON and SRT keep their own structure. Your choices are remembered, and the export's `manifest.json` records them.

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
