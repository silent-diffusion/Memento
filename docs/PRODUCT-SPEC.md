# Memento — Local-First Recording, Transcription & Meeting Workspace

## Overview

Memento is a Windows desktop application for capturing, organizing, transcribing, analyzing, editing, and transforming long-form recordings.

The application is intended to support meetings, interviews, presentations, lectures, dictation, research sessions, and general-purpose recordings.

The core philosophy is **local-first**:

* Recordings should primarily live within the application.
* Core recording and editing functionality should not depend on external AI services.
* Local processing should be preferred whenever it can provide a reliable experience.
* External AI APIs should be optional enhancements rather than requirements.
* The user should retain ownership and control of the underlying recordings, transcripts, metadata, and generated documents.

The application should make complex functionality feel simple. Users should be able to accomplish most tasks with a few obvious actions without needing to understand the underlying technical processes.

This document describes **what** the application must do. `../design/DESIGN.md` describes **how** it looks and behaves on screen (structure, tokens, components, every designed screen). The two are meant to be read together; where they overlap, this document governs behaviour and `../design/DESIGN.md` governs presentation.

---

# Core Principles

### Local First

Audio, video, transcripts, metadata, annotations, generated documents, and other recording-related information should primarily be stored within the application.

External storage is optional.

### AI Optional

AI services should never be integral to the core application. External AI is **off by default** and is turned on deliberately in Settings.

The application should remain fully useful for:

* Recording
* Playback
* Editing
* Transcription
* Speaker identification
* Metadata management
* Search
* Export

AI APIs provide additional capabilities when enabled.

Initial external AI providers:

* OpenAI / ChatGPT
* Anthropic / Claude

Additional providers may be supported later.

### Accurate by Default, Verifiable Always

A transcript, a speaker label, or a set of meeting minutes is only useful if it can be trusted. Accuracy is a product requirement, not a quality setting.

* **Transcription favours accuracy over speed** unless the user chooses otherwise. The default model is the most accurate one the hardware can run comfortably; faster or smaller models are an explicit choice.
* **Uncertainty is visible, never hidden.** Low-confidence words, uncertain speaker assignments, and uncertain agenda items are marked in the interface so the user knows exactly where to look.
* **Generated documents are grounded in the recording.** Every decision, action item, quote, and summary statement should be traceable to a moment in the transcript through a timestamp that jumps back to the source.
* **The AI must not invent.** Participants, decisions, commitments, owners, deadlines, dates, and quotes may only appear in a document if the recording or the recording's details support them. Where the material does not support a statement, the document says so ("not discussed", "not reached", "no owner was named") rather than filling the gap.
* **Corrections propagate.** Fixing a word, renaming a speaker, or editing an agenda item updates everything downstream, and regenerated documents use the corrected material.
* **The user reviews before anything leaves.** Export is a deliberate step. Nothing is sent to an external service or written outside the library without an explicit action.

### Everything Useful Is Data

The application should preserve as much useful information about a recording as practical.

This can include:

* Original audio
* Video
* Individual audio tracks
* Transcription
* Speaker identification
* Confidence scores (per word and per speaker assignment)
* Timestamps (segment and word level)
* Chapters
* Topics
* Highlights
* User corrections
* Annotations
* Meeting metadata
* Agenda, including which items were covered
* Participants
* Attachments
* AI-generated documents and the exact inputs used to generate them
* Processing history
* User-created documents
* Previous versions, when version history is enabled

Information should be editable where appropriate.

---

# Application Structure

The application is organised as a **hub and spoke**. The **Library** is home. Everything else opens as a full-screen spoke with a back control, and returning to the Library restores its scroll position, filters, and search.

| Area | Role |
|---|---|
| **Library** (home) | Every recording project, grouped by date, filterable by type, searchable across titles, transcripts, and people. List and grid views. Shows what is processing right now. |
| **Recording session** | Focused exclusively on making the recording easy and reliable: sources, levels, timer, pause, stop, highlight marking, optional live transcript, agenda. |
| **Review and transcript** | Player, waveform, speaker-labelled transcript, chapters, highlights, topics, people, details, documents, and processing history for one recording. |
| **Document builder** | Visual composition of a document template: modules, layout, per-module instructions, inputs to the AI, style, and a live preview. |
| **Document viewer** | Reading, light editing, and version management of a generated or hand-written document, with timestamps linking back to the transcript. |
| **Settings** | General, Recording, Transcription, Speakers, AI and privacy, Documents, Export, Storage and history. |

Secondary surfaces open over the current spoke rather than replacing it: the **Details and agenda** side sheet (from the Recording session), the **Export** dialog (from Review or the Document viewer), the **Style editor** (from the Builder or Settings), and confirmation dialogs.

There is no persistent sidebar and no tab strip. The guiding rule is:

> **Everything important should be only a click or two away.**

Starting a recording, opening a recording, exporting, and reaching Settings are each one click from the Library.

---

# Recording

The application should support both:

* **Audio-only recording**
* **Audio + video recording**

The user should be able to choose the desired recording mode before recording begins, and the default mode is configurable.

## The Recording Session

The recording screen does one job. It should show, at all times:

* A large timer and an unmistakable recording / paused / ready state.
* The active sources, each with a live level meter and a toggle.
* Stop, pause/resume, and **Mark highlight** controls.
* A status line confirming that the recording is being saved continuously and when the last checkpoint was written.

The title and recording type are editable in place before and during recording. Participants, purpose, platform, tags, and the agenda are edited in the **Details and agenda** sheet, which can be opened at any point without interrupting the recording.

Pressing Stop ends the session and opens the recording in Review. Stop needs no confirmation: everything is already saved.

Optional panels (each independently configurable):

* **Live transcript**, produced locally while recording. It is explicitly a rough draft and is replaced by the full transcription pass afterwards.
* **Agenda**, imported before the session, with items marked covered as the meeting proceeds. Marking is manual; local keyword matching may suggest it; it never requires an external service.

## Audio Sources

The application should expose available audio sources and allow the user to independently enable or disable them.

Potential sources include:

* Microphone
* Headset microphone
* USB microphone
* System audio
* Application audio
* Individual application audio sources
* Virtual audio devices
* Other Windows audio inputs

The application should support recording specific applications when technically possible.

For example, a user should be able to capture:

* Zoom audio
* A specific application
* System-wide audio
* Their microphone

without necessarily recording unrelated system sounds.

Permissions should be requested when the user attempts to use a source that requires them. A denied permission affects only that source; the others keep working.

Enabling or disabling a source during recording starts or stops that source's track without touching the others.

---

# Video Recording

Video recording should be optional.

The application should support recording:

* Audio only
* A specific display
* A specific window/application where supported
* Multiple displays where supported
* Screen plus selected audio sources
* Screen plus camera where supported

The exact video sources available should depend on the user's hardware and Windows capabilities.

Video should remain independent from the audio recording architecture so that users can choose whether video is necessary.

---

# Multi-Track Recording

During the initial recording, individual sources should remain as **separate synchronized tracks**.

For example:

```text
Recording
├── Microphone Track
├── Zoom/System Audio Track
├── Application Audio Track
└── Additional Audio Track
```

This should preserve maximum information during recording and allow the application to make better decisions later. Separate tracks are also what make accurate transcription and speaker identification possible: a clean microphone track and a clean application track are far easier to transcribe and attribute than a single mix.

The application may subsequently offer storage optimization through:

* Compression
* Downmixing
* Mono conversion
* Track consolidation
* Other lossless or appropriately lossy storage optimizations

Storage optimization should not unnecessarily destroy information that could be useful for transcription, speaker identification, or editing. The default after processing is lossless; downmixing older recordings is an opt-in setting.

---

# Recording Duration

Recordings should support sessions lasting several hours and, where practical, effectively indefinite recording.

The application should:

* Stream recording data to storage
* Avoid requiring the entire recording to reside in RAM
* Periodically save recording state (the checkpoint interval is configurable; 30 seconds by default)
* Recover partial recordings after interruptions and offer them on next launch
* Monitor available disk space and show free space in the status footer
* Warn the user when storage becomes limited (threshold configurable; 10 GB by default) while continuing to record

If the computer enters a state where recording cannot continue, the application should preserve everything successfully recorded up to that point and say exactly where it stopped.

If recording can safely continue during a system state change, it should do so.

---

# Recording Format

The application should support a recording format appropriate for long-duration, high-quality capture and later processing.

MP3 may be useful for certain exports, but the internal recording format should prioritize:

1. Reliability
2. Audio quality
3. Efficient processing
4. Multi-track support
5. Reasonable storage requirements
6. Compatibility with transcription and editing workflows

The exact internal format and compression strategy should be determined during technical development.

Lossless or high-quality intermediate formats should be preferred when they materially improve downstream processing.

---

# Storage

The application's primary storage location should be its own internal recording library.

A recording should be treated as a **complete project**, not simply an audio or video file.

A project may contain:

```text
Recording
├── Original media
├── Audio tracks
├── Video
├── Transcription (with confidence and word timings)
├── Speaker information
├── Metadata and details
├── Agenda (with coverage)
├── Chapters
├── Topics
├── Highlights
├── Annotations
├── Generated documents (with the inputs used)
├── User-written documents
├── Imported files and attachments
├── Processing history
└── Previous versions (when enabled)
```

All of these elements should remain associated with the original recording.

---

# External File Saving

Saving copies outside of the application should be optional and configurable.

The user should be able to enable or disable external saving in Settings.

The user should also be able to choose **which components** are exported.

Possible export selections include:

* Audio (mixed)
* Individual audio tracks
* Video
* Transcription
* Recording details and metadata
* AI-generated reports
* Meeting minutes
* Attachments
* Other generated or user-written documents

The user should not be forced to export the entire project.

## Export Location

Users should be able to configure a default external folder.

They should also have an option to ask for a destination when appropriate.

The request for an external save location should itself be toggleable.

---

# Transcription

Transcription should be a first-class capability of the application.

Local transcription should be preferred when practical.

The application should support:

* On-device transcription
* GPU acceleration when available
* CPU fallback
* Long-form audio
* Multi-hour recordings
* Chunked processing
* Background processing
* Configurable transcription behavior

Transcription should be optionally performed:

* During recording (as a rough live draft)
* After recording (the authoritative pass)
* In the background
* Manually on demand

The choice should be configurable and may depend on available system resources.

If the computer is under heavy load, the application should be able to defer or reduce transcription processing. Recording is never paused for this reason; only transcription is.

## Transcription Accuracy

* The default engine and model should be the most accurate combination the machine can run. Settings should state the model's size, whether it is installed, and whether it runs on the GPU or the CPU.
* Word-level timestamps and confidence scores should be kept whenever the engine provides them. They drive the low-confidence marking in Review.
* The user should be able to re-transcribe a recording with a different model or language setting. When version history is on, the previous transcript is kept as a version.
* Language should be detected per recording when set to Auto, and the detected language recorded.
* The live transcript shown during recording is never the final transcript. The post-recording pass replaces it.

---

# Speaker Identification

Speaker identification should be available as a configurable feature.

The system should attempt to associate transcript segments with speakers.

Possible information:

```text
Speaker 1
Speaker 2
Speaker 3
```

Users should be able to rename identified speakers.

For example:

```text
Speaker 1 → Sarah
Speaker 2 → Michael
Speaker 3 → John
```

Speaker information should remain linked to transcript segments and remain editable. Renaming a speaker updates every segment, the people list, and any documents regenerated afterwards.

Segments whose speaker assignment is uncertain should be marked so the user can confirm or reassign them. Talk-time share per speaker may be shown as a sanity check.

Speaker identification should be independently toggleable in Settings. Remembering renamed speakers across recordings (by voice) should be a separate, optional setting.

---

# Transcription Data

The internal transcription representation should be structured JSON.

It should preserve significantly more information than plain text.

Potential information includes:

* Transcript text
* Start timestamp
* End timestamp
* Speaker and speaker confidence
* Word-level text, timing, and confidence where available
* Language
* Segment information
* Source track reference
* Chapters
* Topics
* Highlights
* User corrections (what changed, when)
* User annotations
* AI-generated annotations
* Processing metadata (engine, model, version, hardware, duration)

The system should be designed so that additional information can be added without requiring a completely different transcription format.

---

# Transcript Editing

Transcriptions should be editable within the application.

Users should be able to:

* Correct transcription errors
* Edit speaker names
* Change speaker assignments
* Modify timestamps
* Edit text
* Add annotations
* Highlight sections
* Create chapters
* Add topics
* Search the transcript
* Navigate between transcript sections and corresponding media

In Review, a click on a transcript line's time plays it; a click on its words edits it in place with the caret at the click (a double-click edits too). Low-confidence words are visibly marked. Highlights and notes attach to the segment they belong to; a highlight's or chapter's name is renamed by clicking it. The speaker menu on a line searches the speakers as you type and adds a new one by name.

Every change made in Review, the document builder and the document viewer can be undone with an Undo button and Ctrl+Z, and redone with Ctrl+Y or Ctrl+Shift+Z, step by step, while that recording, template or document stays open.

Edits should remain associated with the underlying recording and, when version history is enabled, be versioned.

## Reviewing for Accuracy

The Review screen is designed to make verification fast rather than to assume the transcript is right:

* Play while reading, with the current segment following the playhead.
* Jump by chapter or highlight.
* Low-confidence words and uncertain speaker assignments are the first things to check and are marked as such.
* Corrections are applied in place and propagate immediately.
* The transcript can be filtered: clicking a speaker in the People list shows only their lines, and a Filter control combines speakers, highlighted lines, lines with uncertain words, edited lines, one chapter and the search text. Hidden lines are only hidden; playing, editing and undo work on the lines shown, and the filter lasts while the recording is open.

A transcript that has been reviewed may be marked as reviewed. Documents generated from a transcript that has not been reviewed may carry a note saying so.

---

# Version History

Version history should be available as an optional feature.

Users should be able to enable or disable version history in Settings, and choose how long versions are kept.

When enabled, the application may preserve previous versions of:

* Transcriptions
* Metadata and agenda
* Generated documents
* Meeting minutes
* AI-generated outputs
* User edits

Versions are listed where the content lives (for a document, in the Document viewer) with a Restore action. The purpose is to allow users to recover previous work while giving them control over storage consumption.

---

# Recording Types

The application should support different types of recordings.

Possible recording types include:

* Meeting
* Interview
* Presentation
* Lecture
* Dictation
* Research
* General recording
* Custom recording type

Recording types can determine which metadata and processing features are presented, which document templates are offered by default, and how the recording is filtered in the Library.

---

# Meeting Information

For a meeting, the application should be able to collect:

* Meeting title
* Participants
* Agenda
* Organization/team
* Meeting purpose
* Date/time
* Location
* Platform
* Notes
* Tags

All information should remain editable before, during, and after the recording, from the Details and agenda sheet during recording and from the Details tab in Review afterwards.

The user should not be forced to provide every field.

---

# Agenda Import

Users should be able to provide an agenda as an input to the recording project.

Possible sources include:

* Excel
* PDF
* Word documents
* Images (including a photo of a printed agenda)
* CSV
* Markdown
* Plain text (including pasted text)
* Other supported documents

The application should attempt to extract useful agenda information automatically.

## Local Processing Priority

If the agenda can be reliably parsed on-device, local processing should be preferred. Parsing happens on this PC and nothing is uploaded.

The parsed result is shown as an editable list. Items the parser is unsure about are marked, with a plain explanation of why (for example, a heading and its bullet may have been merged), and the user can fix them in place, reorder them, remove them, or add more.

## Optional AI Processing

An external AI API may be used when:

* The document is difficult to parse
* The agenda requires semantic interpretation
* The user requests higher-quality extraction
* Local processing cannot reliably determine the structure

AI-powered agenda parsing should remain optional. It sends only the agenda file, asks before sending, and is unavailable (with a pointer to Settings) when external AI is off.

The application should not require an AI API merely to import a basic agenda.

Agenda items follow the recording: they appear in the Recording session's agenda panel, in Review's Details tab, and as an input to document generation, where items not reached are reported as such rather than summarised.

---

# Topics, Chapters, Highlights & Annotations

The application should preserve useful structural information about recordings.

This can include:

### Chapters

Meaningful sections of the recording that users can navigate between.

### Topics

Subjects discussed during a particular portion of the recording.

### Highlights

Sections marked as particularly important. A highlight can be marked during recording with one control (or a shortcut) and given an optional note at the time or later.

### Annotations

Additional information attached to a timestamp, transcript segment, topic, or other piece of content.

Annotations may be:

* User-created
* Automatically generated
* AI-generated

These should remain editable, and their origin should be recorded.

---

# Processing Status

The user should always be able to see what the application is doing to a recording without opening it.

* The Library shows a **processing card** for the recording currently being processed, with one progress track per stage (stored, transcribing, speakers, minutes) and which stage is active, queued, done, or failed.
* Every recording in the Library carries **status pills** for the stages that have run or are running. Stages that were never enabled show nothing; an audio-only recording with no processing says "Audio only".
* Review's **History** tab lists every processing step with its engine, duration, and result, and, for external AI, exactly what was sent.
* The status footer reports engine readiness (GPU or CPU), paused processing, free disk space, and any lost source.

---

# AI Processing

AI should be an optional processing layer.

The core application must not depend on external AI services.

When enabled, AI can be used for tasks such as:

* Meeting minutes
* Summaries
* Executive summaries
* Action items
* Decision extraction
* Topic organization
* Agenda alignment
* Follow-up questions
* Follow-up emails
* Research analysis
* Document generation
* Transcript cleanup
* Annotation generation

Initial supported providers:

* OpenAI / ChatGPT
* Anthropic / Claude
* Local (an on-device model, downloaded and managed in Settings like the transcription models; nothing leaves the PC; no key)

The architecture should allow additional providers to be added later.

Settings should offer "ask before every send" (on by default) and "keep a record of what was sent" (on by default). API keys are stored securely and never echoed in the interface.

## Working within a small context

The local model has a small context window, and long transcripts exceed it. Generation therefore never sends a whole transcript in one request:

* The transcript is broken into chunks at natural boundaries (chapters, topics, speaker turns) sized to the provider's budget.
* Each document module is generated in its own pass over the chunks it needs, and its results are merged in time order.
* A separate verification pass asks the model to confirm each claim against the exact transcript span it cites; claims the span does not support are dropped or reported as "not discussed".
* The same pipeline is used for the external providers, with larger chunks, so documents are produced the same way whichever provider is chosen.

---

# AI Input

AI processing should have access to the structured information associated with a recording when the user permits it.

Potential inputs include:

* Transcript (with speakers)
* Meeting metadata and details
* Participants
* Agenda
* Topics
* Chapters
* Highlights
* User annotations
* Imported documents
* User instructions
* Previous generated documents

The user should have control over which information is made available to external AI services. In the Document builder this control is a checklist: the ticked items are the exact payload, and a **preview of exactly what will be sent** is available before generation.

---

# AI Data Sharing

External AI processing should be configurable in Settings.

The user should be able to control whether external services may receive:

* Transcript
* Recording details and metadata
* Participants
* Agenda and imported documents
* Highlights and notes
* Attachments

**Audio and video are never sent to an external service.** This is a hard rule of the application, not a setting.

The default configuration should prioritize useful AI functionality while avoiding data that is unlikely to meaningfully improve the requested output.

The user should be able to disable external AI processing entirely.

---

# AI Document Generation

Generated documents should be treated as part of the recording project.

For example:

```text
Recording
│
├── Transcript
├── Metadata
├── Agenda
└── Documents
      ├── Meeting Minutes (generated · v2)
      ├── Executive Summary (generated)
      └── My Notes (written)
```

Generated documents should be:

* Saved with the recording, together with the template, style, provider, time, and the exact inputs used
* Editable
* Viewable within the application
* Versionable when version history is enabled
* Exportable independently

## Grounded Generation

Document generation must respect the accuracy principle:

* Each statement that reports a decision, action, quote, or conclusion is linked to the transcript by a **timestamp** that opens Review at that moment. Timestamps survive export as footnote-style links (Word, PDF) or inline markers (Markdown).
* Action items list only commitments actually made in the recording, with the owner and deadline only if they were stated. Missing owners or dates are reported as missing.
* Agenda alignment reports items that were not reached or not discussed instead of inventing coverage.
* Participants come from the recording details and the identified speakers, not from inference.
* Per-module instructions can constrain tone and length but never override these rules.
* Regeneration after a correction uses the corrected transcript, details, and agenda.

---

# Document Viewer

Opening a generated or hand-written document shows it as a page in its chosen style, with:

* A light formatting toolbar for in-place edits (headings, emphasis, lists, tables, inserting a timestamp). Edits save as you type.
* Inline timestamp markers that jump to the transcript.
* A **How this was made** panel: template, style, provider, when it was generated, and the inputs that were sent, with a note that audio and video were not.
* **Versions** with restore, when version history is on.
* **Regenerate**, which reopens the Document builder with this template and inputs loaded.
* **Export**, which opens the Export dialog for this document.

---

# Visual Document Builder

Users should be able to define how AI-generated documents are organized.

Rather than relying solely on a written prompt, the application should provide a visual system for composing document structures.

The structure is a **list of rows**, and each row can hold one, two, or three modules side by side. Rows become columns in the generated document (two-column layouts in Word and PDF; stacked in Markdown).

Users should be able to:

* Add modules from a palette by clicking (appends a row) or dragging (places them)
* Drag modules between rows, into a new row, or **beside** another module to put them side by side
* Reorder with the keyboard or buttons as well as by dragging
* Remove modules
* Configure each module: instructions, length, text size (smaller, normal, or larger than the style's base size), and whether its points link back to the transcript
* See a **live preview** of the resulting document as they work: the real title and meta line, every row as columns, the headings in the chosen style, and a skeleton of the content each module will produce
* Choose which inputs the AI receives, the provider, the style, and the output options
* Save the arrangement as a template
* Create different templates for different recording types

A meeting-minutes template might contain:

```text
Executive Summary
↓
Meeting Purpose      │ Participants
↓
Agenda
↓
Discussion Summary
↓
Decisions            │ Action Items
↓
Open Questions       │ Next Meeting
```

Another user could arrange the same information differently.

The AI should generate the content according to the structure defined by the user. The preview is the contract: headings, order, and columns in the preview are exactly what the generated document will have.

Nothing leaves the PC until the user presses Generate.

---

# Document Modules

Potential modules include:

* Title
* Summary
* Executive Summary
* Participants
* Agenda
* Topic
* Discussion
* Decision
* Action Item
* Owner
* Deadline
* Open Question
* Quote
* Highlight
* Chapter
* Timeline
* Follow-up (including a follow-up email)
* Notes
* Full transcript (the entire transcript with speakers and timestamps, placed as data; no AI is involved)
* Custom text
* Custom AI-generated section

Each module declares the kind of content it produces (paragraph, list, table, chips, label/value, quote, timeline, transcript) so the preview can show it, and the grounding rules that apply to it (for example, an Action Item requires a traceable commitment).

Each module also carries its own text size (smaller, normal, or larger relative to the style's base size), so a Full transcript can be set small while an Executive Summary stays prominent. The setting applies in the preview, the Document viewer, and Word and PDF exports.

The module system should be extensible.

---

# Document Styling

Generated documents should support customizable styling.

Users should be able to configure things such as:

* Fonts (heading and body typeface)
* Font sizes (base size)
* Colors (headings and rules, table header fill)
* Headings (case, numbering)
* Spacing
* Emphasis
* Tables
* Lists
* Page structure (paper size, running header, page numbers)
* Other supported document styling properties

Styling should be independent from the content structure.

For example, a user could maintain:

```text
Template:
Meeting Minutes

Style:
Corporate
```

and separately:

```text
Template:
Meeting Minutes

Style:
Minimal
```

The same information architecture could therefore produce different visual outputs.

A **Style editor** shows every setting next to a live sample page; what the sample shows is what Word and PDF exports produce. Markdown exports ignore styling. Built-in styles (Corporate, Minimal, Academic) are presets of the same settings and can be duplicated and changed.

---

# AI Templates

Users should be able to save reusable AI/document templates.

A template should define:

* Which information is provided to the AI
* Which document modules are generated
* The order and row layout of those modules
* Instructions, length, and transcript-linking for each module
* The default style
* Optional processing instructions
* Output format and export options

This allows the user to establish consistent meeting-minute formats without manually reconstructing the desired output every time.

---

# User Experience Principles

The application should feel:

* Minimal
* Fast
* Obvious
* Calm
* Useful
* Easy to learn
* Powerful without being complicated
* Trustworthy: it shows its work and marks what it is unsure about

The guiding principle should be:

> **Everything important should be only a click or two away.**

Advanced functionality should exist without forcing users to interact with it.

Features such as:

* Speaker identification
* External AI
* Automatic transcription
* Version history
* External exports
* Advanced processing
* Additional analysis

should be configurable and discoverable without becoming obstacles to basic recording.

The user should be able to start a recording quickly and then watch the application progressively process and organize the material.

## Design Language

The visual design is specified in `../design/DESIGN.md`. In brief:

* **Calm studio** palette: warm neutral greys, one warm accent that means "recording", Manrope for the interface and JetBrains Mono for timecodes.
* **Neumorphism**: one matte material. Cards, buttons, and chips are the same colour as the ground and are shaped by paired light and dark shadows; inputs, wells, and selected states are pressed in. There are no hairline borders; dividers are faint grooves.
* **Tactile controls**: buttons and chips visibly depress while pressed, toggles snap, primary buttons have a convex fill. The same feedback plays for keyboard activation.
* **Light and dark are equal**, and the application follows the Windows theme setting by default.
* **The document is the exception**: previews and generated documents are rendered as white pages in both themes, styled by the document's own style rather than the application's tokens.

---

# Processing Pipeline

The overall system should conceptually follow:

```text
Capture
   ↓
Store
   ↓
Transcribe
   ↓
Identify Speakers
   ↓
Review and Correct
   ↓
Analyze
   ↓
Organize
   ↓
Generate Documents
   ↓
Verify (timestamps, versions)
   ↓
Export
```

Individual stages should be independently configurable.

The user should not have to enable every stage.

For example:

```text
Recording
   ↓
Audio only
   ↓
No transcription
```

should be perfectly valid.

Likewise:

```text
Recording
   ↓
Audio + Video
   ↓
Local transcription
   ↓
Speaker identification
   ↓
Review and correction
   ↓
AI analysis
   ↓
Meeting minutes with timestamps
```

should also be possible.

---

# Resource Awareness

The application should be aware of the computer's available resources.

This is particularly important for local processing on systems with limited GPU memory.

Processing features should be able to adapt to:

* GPU availability
* GPU memory
* CPU utilization
* RAM availability
* Disk space
* Current recording activity
* Other system load

Resource-intensive operations should not unnecessarily interfere with recording.

Where possible, processing should be deferred, reduced, or moved to the background when the system is under load, and the status footer should say so ("Transcription paused · PC is busy"). Falling back to a smaller model is offered to the user, not applied silently, because it affects accuracy.

---

# Reliability

Reliability is more important than feature count.

The application should prioritize preserving user data.

In particular:

* Recording should continue independently of optional processing.
* Transcription failure should never destroy the recording; partial transcripts are kept.
* AI API failure should never affect the underlying recording or an existing document.
* Export failure should never affect the internal project.
* Crashes should preserve as much recorded material as possible.
* Interrupted processing should be restartable.
* Partial results should be retained when practical.

The application should always treat the original recording as the authoritative source.

## Errors and Recovery

Failures are reported specifically, and nothing destructive happens without the user asking for it. The designed states are:

| Situation | Behaviour |
|---|---|
| A source stops mid-recording | A toast names the source and the time; the other tracks keep recording; reconnect is offered. |
| Low disk space | A banner states the free space, that recording continues, and that transcription is paused until there is room. |
| The drive fills during recording | Recording stops at a stated time; everything up to that point is saved and transcribes once there is room. |
| A processing stage fails | The processing card names the stage, the reason in plain words, and what was kept, and offers the most specific fix first (for example "Retry on CPU", "Use the Medium model"). |
| An interrupted recording is found at launch | A dialog states what was saved, how many tracks are intact, and what may be missing, and offers to open it. It never asks whether to keep it. |
| An export fails | An inline message states what could not be written and why, and that nothing inside the application changed. |
| An AI provider fails | An inline message states the provider and the cause, that nothing was sent twice, and that no document was changed. |
| Windows blocks a device | A message explains where to allow it and that the other sources keep working. |
| Deleting a recording | A confirmation names the recording, states exactly what is removed and its size, that exported copies are untouched, and that it cannot be undone. |

Copy for these states names the thing (source, stage, drive, provider), gives the time or amount, says what is safe, and then offers the fix.

---

# External Export

External file creation should be independently configurable.

The user should be able to select exactly what gets exported.

For example:

```text
☑ Audio (mixed)
☐ Individual tracks
☐ Video
☑ Transcript
☑ Documents
    ☑ Meeting minutes
    ☑ Action items
    ☐ My notes
☐ Recording details
☐ Attachments
```

Each export type may have its own format options, chosen in the Export dialog, which also shows the estimated size and file count, the destination folder, and whether to create a folder named after the recording. Choices can be remembered as the new default.

Potential formats include:

* Audio: FLAC, WAV, MP3
* Video: MP4 or the original container
* Transcript: JSON (structured, with speakers, timestamps, and confidence), Markdown, plain text, SRT
* Documents: Word, PDF, Markdown
* Recording details: JSON
* Attachments: original files

Export runs in the background and reports progress in the status footer.

The readable transcript (Markdown and plain text) can be written with or without timestamps and with or without speaker names, in paragraphs per speaker turn or line by line, in any combination; the choice is remembered per user and recorded in the export manifest. JSON and SRT keep their own structure.

Anything readable can also go straight to the clipboard instead of a file: the transcript as text or Markdown (with the same choices, and only the lines shown when the transcript is filtered in Review), and a document as Markdown plus formatted text that Word and Outlook paste with its formatting. The application writes the clipboard itself and asks Windows not to sync it to other devices; nothing leaves the PC.

---

# Project Integrity

A recording project should remain coherent regardless of where individual exports are saved.

The internal application project remains the authoritative source.

External files are copies or representations of project data.

This distinction allows the user to:

* Edit a transcript without manually managing files
* Regenerate meeting minutes
* Reprocess a recording
* Change AI templates
* Export the same project differently
* Recover previous versions when enabled

---

# Accessibility

* Every control is a real control: buttons, links, inputs with labels, switches with state. Icon-only controls carry an accessible name.
* Everything reachable by mouse is reachable by keyboard in reading order, with a visible focus ring. Space pauses and resumes a recording; a shortcut marks a highlight.
* Text contrast is at least 4.5:1 (3:1 for large text) in both themes. Status is never conveyed by colour alone: pills carry text, uncertain words carry an underline, speakers carry names.
* Motion respects the system's reduced-motion setting: press states keep their shadow change (it is state), but lifts, overshoots, and scaling are dropped.

---

# Future-Proofing

The system should be designed around extensible concepts rather than hard-coded workflows.

Examples include:

* Recording types
* Audio sources
* Video sources
* Processing stages
* Transcription engines and models
* Speaker identification systems
* AI providers
* AI templates
* Document modules
* Document styles
* Export formats
* Storage strategies

The application should be able to add new processing capabilities without fundamentally changing how existing recordings are stored.

---

# Product Vision

The ultimate goal is to create a **personal recording and information workspace** rather than simply a recorder or meeting-notes application.

A user should be able to:

1. Start recording almost immediately.
2. Capture exactly the audio/video sources they want.
3. Let the application automatically preserve useful information.
4. Review and correct the resulting transcript, guided to the places that need checking.
5. Understand who said what.
6. Organize the recording into topics, chapters, highlights, and annotations.
7. Provide an agenda or other source material.
8. Optionally use AI to turn the information into useful documents that cite the recording.
9. Visually control the structure and look of those documents.
10. Edit everything afterward.
11. Keep the entire project together inside the application.
12. Trust what the application produces, because it shows where every statement came from.
13. Export only the pieces they actually want outside the application.

The application should make the complicated parts happen in the background while keeping the user's experience simple:

> **Record → Process → Review → Organize → Create → Verify → Export**
