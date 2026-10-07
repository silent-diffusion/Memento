// Library rows (DESIGN.md §4) and grid cards (§16). Each item is one button that opens Review, with
// a sibling ⋯ button (Rename, Change type, Retry a failed stage, Delete) that appears on hover or
// focus. A transcript match shows its snippet under the meta line; a failed pill retries on click.
import type { JSX } from 'preact';
import type { RecordingSummary, StageName } from '../../bridge/types';
import { formatDuration } from '../../format/duration';
import { metaLine, stageName, stagePills, type Pill } from '../../format/recording';
import { snippetParts } from '../../format/transcript';
import { waveformBars } from '../../format/waveform';
import { ActionMenu, type MenuAction } from '../../components/Menus';
import { CheckIcon, ChevronRightIcon, MoreIcon, TypeIcon, VideoIcon } from '../../components/icons';

export interface ItemHandlers {
  open: (recording: RecordingSummary) => void;
  rename: (recording: RecordingSummary) => void;
  changeType: (recording: RecordingSummary) => void;
  remove: (recording: RecordingSummary) => void;
  /** processing.retry for a failed stage ("Transcript failed · Retry"). */
  retry: (recording: RecordingSummary, stage: StageName) => void;
}

function Pills({ pills, onRetry }: { pills: Pill[]; onRetry: (stage: StageName) => void }): JSX.Element {
  if (pills.length === 0) {
    return <span class="audio-only">Audio only</span>;
  }
  return (
    <>
      {pills.map((pill) =>
        pill.kind === 'failed' ? (
          // The row is one button, so the pill itself is not one: a click on it retries, and the row
          // menu offers the same Retry to the keyboard.
          <span
            key={pill.label}
            class="pill failed pill-retry"
            title={`Retry: ${stageName(pill.stage)}`}
            onClick={(event) => {
              event.stopPropagation();
              onRetry(pill.stage);
            }}
          >
            {pill.label}
          </span>
        ) : (
          <span key={pill.label} class={`pill ${pill.kind}`}>
            {pill.kind === 'done' ? <CheckIcon size={11} /> : null}
            {pill.label}
          </span>
        ),
      )}
    </>
  );
}

/** "…the processing card should **disappear**…" under the meta line for a transcript match. */
function MatchSnippet({ snippet, query }: { snippet: string | null; query: string }): JSX.Element | null {
  if (snippet === null || snippet === '') {
    return null;
  }
  return (
    <span class="row-snippet">
      <span class="sr">Transcript match: </span>
      {snippetParts(snippet, query).map((part, i) => (part.bold ? <b key={i}>{part.text}</b> : part.text))}
    </span>
  );
}

function actionsFor(recording: RecordingSummary, handlers: ItemHandlers): MenuAction[] {
  const failed = recording.stages.filter((s) => s.state === 'failed');
  return [
    { label: 'Rename', run: () => { handlers.rename(recording); } },
    { label: 'Change type', run: () => { handlers.changeType(recording); } },
    ...failed.map((s) => ({
      label: `Retry ${stageName(s.stage).toLocaleLowerCase()}`,
      run: () => {
        handlers.retry(recording, s.stage);
      },
    })),
    { label: 'Delete', run: () => { handlers.remove(recording); } },
  ];
}

interface ItemProps {
  recording: RecordingSummary;
  selected: boolean;
  now: Date;
  handlers: ItemHandlers;
  /** The search the list shows, for bolding a transcript match. */
  query: string;
}

export function RecordingRow({ recording, selected, now, handlers, query }: ItemProps): JSX.Element {
  return (
    <li class="lib-item">
      <button
        class={selected ? 'row lib-row selected' : 'row lib-row'}
        type="button"
        data-recording-id={recording.id}
        onClick={() => {
          handlers.open(recording);
        }}
      >
        <span class="type-tile" aria-hidden="true">
          <TypeIcon type={recording.type} size={20} />
        </span>
        <span class="row-main">
          <span class="row-title">{recording.title}</span>
          <span class="row-meta">
            {recording.hasVideo ? <VideoIcon size={14} /> : null}
            {metaLine(recording, now)}
          </span>
          <MatchSnippet snippet={recording.matchSnippet} query={query} />
        </span>
        <span class="row-pills">
          <Pills
            pills={stagePills(recording.stages)}
            onRetry={(stage) => {
              handlers.retry(recording, stage);
            }}
          />
        </span>
        <span class="row-duration mono">{formatDuration(recording.durationMs)}</span>
        <ChevronRightIcon size={18} class="row-chevron" />
      </button>
      <ActionMenu label={`More actions for ${recording.title}`} triggerClass="icon-btn row-more" actions={actionsFor(recording, handlers)}>
        <MoreIcon size={18} />
      </ActionMenu>
    </li>
  );
}

export function RecordingCard({ recording, selected, now, handlers, query }: ItemProps): JSX.Element {
  return (
    <li class="lib-card-item">
      <button
        class={selected ? 'card lib-card selected' : 'card lib-card'}
        type="button"
        data-recording-id={recording.id}
        onClick={() => {
          handlers.open(recording);
        }}
      >
        <span class="wave-strip" aria-hidden="true">
          {waveformBars(recording.id).map((height, index) => (
            <span key={index} class="wb" style={{ height: `${height}px` }} />
          ))}
        </span>
        <span class="card-body">
          <span class="card-head">
            <span class="type-tile type-tile--sm" aria-hidden="true">
              <TypeIcon type={recording.type} size={17} />
            </span>
            <span class="card-text">
              <span class="clamp card-title">{recording.title}</span>
              <span class="card-meta">
                {recording.hasVideo ? <VideoIcon size={13} /> : null}
                {metaLine(recording, now)}
              </span>
              <MatchSnippet snippet={recording.matchSnippet} query={query} />
            </span>
            <span class="mono card-duration">{formatDuration(recording.durationMs)}</span>
          </span>
          <span class="card-pills">
            <Pills
              pills={stagePills(recording.stages)}
              onRetry={(stage) => {
                handlers.retry(recording, stage);
              }}
            />
          </span>
        </span>
      </button>
      <ActionMenu label={`More actions for ${recording.title}`} triggerClass="icon-btn card-more" actions={actionsFor(recording, handlers)}>
        <MoreIcon size={18} />
      </ActionMenu>
    </li>
  );
}
