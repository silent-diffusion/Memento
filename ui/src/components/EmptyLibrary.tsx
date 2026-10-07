import type { ComponentChildren, JSX } from 'preact';
import { DocumentIcon, MicrophoneIcon, TranscriptLinesIcon } from './icons';

interface EmptyLibraryProps {
  onStartRecording: () => void;
  onImport: () => void;
  /** M3: an import that failed, shown under the buttons (DESIGN.md §17). */
  notice?: ComponentChildren;
}

const features = [
  {
    title: 'Record what you choose',
    body: 'Your microphone, system audio or a single app like Zoom. Each source is kept as its own track.',
    Icon: MicrophoneIcon,
  },
  {
    title: 'Transcribe on this PC',
    body: 'Transcripts and speaker names are produced locally, during or after the recording. No account needed.',
    Icon: TranscriptLinesIcon,
  },
  {
    title: 'Turn it into documents',
    body: 'Minutes, summaries and action items, shaped by templates you design. AI is optional and off by default.',
    Icon: DocumentIcon,
  },
] as const;

/** First-run Library (DESIGN.md §6, renders/LibraryEmpty.dc.html). */
export function EmptyLibrary({ onStartRecording, onImport, notice = null }: EmptyLibraryProps): JSX.Element {
  return (
    <div class="empty-column">
      <div class="empty-intro">
        <span class="empty-ring" aria-hidden="true">
          <span class="empty-ring-dot" />
        </span>
        <h1 class="empty-title">Your library is empty</h1>
        <p class="empty-lead">
          Recordings, transcripts and documents live here, on this PC. Nothing leaves it unless you choose to export.
        </p>
      </div>

      <div class="empty-actions">
        <button class="btn primary start-recording" type="button" onClick={onStartRecording}>
          <span class="rec-dot rec-dot-lg" aria-hidden="true" />
          Start your first recording
        </button>
        <button class="btn ghost import-media" type="button" onClick={onImport}>
          Import audio or video
        </button>
      </div>

      {notice}

      <ul class="feature-grid" aria-label="What Memento does">
        {features.map(({ title, body, Icon }) => (
          <li class="feature-card" key={title}>
            <span class="feature-icon" aria-hidden="true">
              <Icon size={18} />
            </span>
            <h2 class="feature-title">{title}</h2>
            <p class="feature-body">{body}</p>
          </li>
        ))}
      </ul>
    </div>
  );
}
