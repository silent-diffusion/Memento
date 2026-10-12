// Library rows (DESIGN.md §4) and grid cards (§16). Each item is one button that opens Review, with
// a sibling ⋯ button (Rename, Change type, Retry a failed stage, Delete) that appears on hover or
// focus. A transcript match shows its snippet under the meta line; a failed pill retries on click.
import type { JSX } from 'preact';
import type { RecordingSummary, StageName } from '../../bridge/types';
import { formatDuration } from '../../format/duration';
import {
  cardPills,
  isInterruptedImport,
  isVideoRemoved,
  metaLine,
  rowMetaLine,
  runningVideoStage,
  stageName,
  stagePills,
  videoSubStep,
  type Pill,
} from '../../format/recording';
import { heightLabel, rolesLabel } from '../../format/video';
import { snippetParts } from '../../format/transcript';
import { waveformBars } from '../../format/waveform';
import { ActionMenu, type MenuAction } from '../../components/Menus';
import { DRAG_RECORDING } from './FolderTree';
import { CheckIcon, ChevronRightIcon, MoreIcon, TypeIcon, VideoIcon, VideoRemovedIcon } from '../../components/icons';

export interface ItemHandlers {
  open: (recording: RecordingSummary) => void;
  rename: (recording: RecordingSummary) => void;
  changeType: (recording: RecordingSummary) => void;
  remove: (recording: RecordingSummary) => void;
  /** processing.retry for a failed stage ("Transcript failed · Retry"). */
  retry: (recording: RecordingSummary, stage: StageName) => void;
  /** 2.1: Move to folder… (DESIGN.md §6.1). */
  moveToFolder?: (recording: RecordingSummary) => void;
  /** 2.1: Delete audio… (DESIGN.md §6.2); not offered once the audio is gone. */
  deleteAudio?: (recording: RecordingSummary) => void;
}

/** 2.1: a row or card can be dragged onto a folder in the Folders view. */
function dragProps(recording: RecordingSummary): { draggable: boolean; onDragStart: (event: DragEvent) => void } {
  return {
    draggable: true,
    onDragStart: (event: DragEvent) => {
      event.dataTransfer?.setData(DRAG_RECORDING, recording.id);
    },
  };
}

function Pills({ pills, onRetry, videoRemoved = false, audioRemoved = false }: { pills: Pill[]; onRetry: (stage: StageName) => void; videoRemoved?: boolean; audioRemoved?: boolean }): JSX.Element {
  if (pills.length === 0) {
    // A removed video's caption takes the place a pill would have (DESIGN.md §18.7); 2.1: so does deleted audio.
    return <span class="audio-only">{audioRemoved ? 'Audio deleted' : videoRemoved ? 'Video removed · audio kept' : 'Audio only'}</span>;
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
            title={pill.label.endsWith('Import again') ? 'Import the same file again' : `Retry: ${stageName(pill.stage)}`}
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

/**
 * The top of a grid card (DESIGN.md §16, §18.7): the waveform strip for audio, the 96 px video strip
 * (screen schematic, camera inset, the running sub-step, "Screen + camera · 1440p") for a recording
 * with video, and the pressed-in strip with the crossed camera once its video was removed.
 */
function CardStrip({ recording }: { recording: RecordingSummary }): JSX.Element {
  const video = recording.video;
  if (video !== null && video.removedAt !== null) {
    return (
      <span class="video-strip video-strip--removed" role="img" aria-label="Video removed · audio kept">
        <VideoRemovedIcon size={16} decorative />
        <span aria-hidden="true">Video removed · audio kept</span>
      </span>
    );
  }
  if (recording.hasVideo && video !== null) {
    const caption = `${rolesLabel(video.roles)} · ${heightLabel(video.height)}`;
    const running = runningVideoStage(recording.stages);
    const step = running === null ? null : videoSubStep(running);
    const hasScreen = video.roles.includes('screen');
    const hasCamera = video.roles.includes('camera');
    return (
      <span class="video-strip" role="img" aria-label={`Includes video · ${caption}`}>
        {hasScreen ? <span class="video-strip-win" aria-hidden="true" /> : null}
        {hasCamera ? <span class={hasScreen ? 'video-strip-cam' : 'video-strip-cam video-strip-cam--solo'} aria-hidden="true" /> : null}
        {running === null ? null : (
          <span class="pill active video-strip-pill" aria-hidden="true">
            {step ?? 'Video'}
          </span>
        )}
        <span class="mono video-strip-caption" aria-hidden="true">
          {caption}
        </span>
      </span>
    );
  }
  return (
    <span class="wave-strip" aria-hidden="true">
      {waveformBars(recording.id).map((height, index) => (
        <span key={index} class="wb" style={{ height: `${height}px` }} />
      ))}
    </span>
  );
}

function actionsFor(recording: RecordingSummary, handlers: ItemHandlers): MenuAction[] {
  const failed = recording.stages.filter((s) => s.state === 'failed');
  return [
    { label: 'Rename', run: () => { handlers.rename(recording); } },
    { label: 'Change type', run: () => { handlers.changeType(recording); } },
    ...failed.map((s) => ({
      label: isInterruptedImport(s) ? 'Import again' : `Retry ${stageName(s.stage).toLocaleLowerCase()}`,
      run: () => {
        handlers.retry(recording, s.stage);
      },
    })),
    ...(handlers.moveToFolder === undefined ? [] : [{ label: 'Move to folder…', run: () => { handlers.moveToFolder?.(recording); } }]),
    ...(handlers.deleteAudio === undefined || recording.audioRemoved !== null || recording.state === 'finalizing'
      ? []
      : [{ label: 'Delete audio…', run: () => { handlers.deleteAudio?.(recording); } }]),
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
        {...dragProps(recording)}
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
            {isVideoRemoved(recording) ? (
              <span class="row-meta-removed">
                <VideoRemovedIcon size={14} decorative />
              </span>
            ) : null}
            {rowMetaLine(recording, now)}
          </span>
          <MatchSnippet snippet={recording.matchSnippet} query={query} />
        </span>
        <span class="row-pills">
          <Pills
            pills={stagePills(recording.stages)}
            videoRemoved={isVideoRemoved(recording)}
            audioRemoved={recording.audioRemoved !== null}
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
        <CardStrip recording={recording} />
        <span class="card-body">
          <span class="card-head">
            <span class="type-tile type-tile--sm" aria-hidden="true">
              <TypeIcon type={recording.type} size={17} />
            </span>
            <span class="card-text">
              <span class="clamp card-title">{recording.title}</span>
              <span class="card-meta">{metaLine(recording, now)}</span>
              <MatchSnippet snippet={recording.matchSnippet} query={query} />
            </span>
            <span class="mono card-duration">{formatDuration(recording.durationMs)}</span>
          </span>
          <span class="card-pills">
            <Pills
              pills={cardPills(recording.stages, recording.hasVideo)}
              videoRemoved={isVideoRemoved(recording)}
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
