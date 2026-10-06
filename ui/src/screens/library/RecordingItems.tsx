// Library rows (DESIGN.md §4) and grid cards (§16). Each item is one button that opens Review, with
// a sibling ⋯ button (Rename, Change type, Delete) that appears on hover or focus.
import type { JSX } from 'preact';
import type { RecordingSummary } from '../../bridge/types';
import { formatDuration } from '../../format/duration';
import { metaLine, stagePills, type Pill } from '../../format/recording';
import { waveformBars } from '../../format/waveform';
import { ActionMenu, type MenuAction } from '../../components/Menus';
import { CheckIcon, ChevronRightIcon, MoreIcon, TypeIcon, VideoIcon } from '../../components/icons';

export interface ItemHandlers {
  open: (recording: RecordingSummary) => void;
  rename: (recording: RecordingSummary) => void;
  changeType: (recording: RecordingSummary) => void;
  remove: (recording: RecordingSummary) => void;
}

function Pills({ pills }: { pills: Pill[] }): JSX.Element {
  if (pills.length === 0) {
    return <span class="audio-only">Audio only</span>;
  }
  return (
    <>
      {pills.map((pill) => (
        <span key={pill.label} class={`pill ${pill.kind}`}>
          {pill.kind === 'done' ? <CheckIcon size={11} /> : null}
          {pill.label}
        </span>
      ))}
    </>
  );
}

function actionsFor(recording: RecordingSummary, handlers: ItemHandlers): MenuAction[] {
  return [
    { label: 'Rename', run: () => { handlers.rename(recording); } },
    { label: 'Change type', run: () => { handlers.changeType(recording); } },
    { label: 'Delete', run: () => { handlers.remove(recording); } },
  ];
}

interface ItemProps {
  recording: RecordingSummary;
  selected: boolean;
  now: Date;
  handlers: ItemHandlers;
}

export function RecordingRow({ recording, selected, now, handlers }: ItemProps): JSX.Element {
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
        </span>
        <span class="row-pills">
          <Pills pills={stagePills(recording.stages)} />
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

export function RecordingCard({ recording, selected, now, handlers }: ItemProps): JSX.Element {
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
            </span>
            <span class="mono card-duration">{formatDuration(recording.durationMs)}</span>
          </span>
          <span class="card-pills">
            <Pills pills={stagePills(recording.stages)} />
          </span>
        </span>
      </button>
      <ActionMenu label={`More actions for ${recording.title}`} triggerClass="icon-btn card-more" actions={actionsFor(recording, handlers)}>
        <MoreIcon size={18} />
      </ActionMenu>
    </li>
  );
}
