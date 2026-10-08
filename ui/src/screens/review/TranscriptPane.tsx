// The transcript area of Review (DESIGN.md §9, §5.12, §17): the label row (current chapter, hint,
// Mark as reviewed), the state of the transcript stage when there is no transcript yet (or a
// failed pass), and the segments as a windowed list that follows the playhead.
import type { JSX } from 'preact';
import { useEffect, useLayoutEffect, useMemo, useRef, useState } from 'preact/hooks';
import type { CoverageGap, Highlight, ModelInfo, Project, Speaker, StageFailure, StageStatus, TranscriptSegment } from '../../bridge/types';
import { CheckIcon, TranscriptLinesIcon } from '../../components/icons';
import { formatDuration } from '../../format/duration';
import { gapNoticeText, gapPlacement, isPausedLabel, otherModelFor, segmentIndexAt, transcribingText } from '../../format/transcript';
import { openSettings } from '../../state/actions';
import { useServices } from '../../state/context';
import { computeWindow, RowHeights } from '../../format/virtualList';
import { createFollowPlayhead, followScrollDelta, type FollowPlayhead } from '../../state/followPlayhead';
import type { PlayerApi } from './Player';
import { TranscriptSegmentRow, type SegmentHandlers } from './TranscriptSegment';
import type { ReviewActions } from './reviewActions';
import type { SearchApi, TranscriptApi } from './useTranscript';

/** A typical segment's height before it is measured. */
const ESTIMATED_ROW = 76;
const OVERSCAN = 8;
const NO_HIGHLIGHTS: readonly Highlight[] = [];
const NO_GAPS: readonly CoverageGap[] = [];

/** The host's label for a stage whose model is not installed (BRIDGE.md M2 clarification 14). */
const WAITING_FOR_MODEL = 'Waiting for a model';

const FAILED_LABEL: Partial<Record<StageFailure['stage'], string>> = {
  transcript: 'Transcription failed',
  speakers: 'Speaker identification failed',
};

export interface TranscriptPaneProps {
  project: Project;
  api: TranscriptApi;
  search: SearchApi;
  player: PlayerApi;
  chapterName: string;
  /** Recording or saving: the transcript waits for the stored tracks. */
  inProgress: boolean;
  onBackToRecording: () => void;
  onShowHistory: () => void;
  onError: (title: string, message: string) => void;
  /** Filled by the pane: scrolls a segment into view (search jumps). */
  revealRef: { current: ((segmentId: string) => void) | null };
  /** Every change goes through Review's actions, which register it with Undo. */
  actions: ReviewActions;
  /** A line or a name edited in place was saved ("Saved" beside the Undo button). */
  onSaved: () => void;
}

/** The line being edited: the draft, the text it started from, and where the caret starts. */
interface EditingLine {
  segmentId: string;
  draft: string;
  /** The text when editing began: saving an unchanged draft writes nothing. */
  startText: string;
  caret: number | null;
  saving: boolean;
}

/** The bottom edge of the sticky header and player strip, in viewport coordinates. */
function stickyBottom(): number {
  const header = document.querySelector('.app-header')?.getBoundingClientRect().bottom ?? 0;
  const player = document.querySelector('.player')?.getBoundingClientRect().bottom ?? 0;
  return Math.max(0, header, player);
}

/** The transcript's own scrolling area (DESIGN.md §9), wrapped around the pane by ReviewScreen. */
export const TRANSCRIPT_SCROLLER = 'review-scroll';

/**
 * Where the transcript scrolls: its own area while the three panes sit side by side, or null for
 * the page when they stack (below 1024 px the area does not scroll and the page does).
 */
function scrollerOf(list: HTMLElement | null): HTMLElement | null {
  const el = list?.closest<HTMLElement>(`.${TRANSCRIPT_SCROLLER}`) ?? null;
  if (el === null) {
    return null;
  }
  const overflow = getComputedStyle(el).overflowY;
  return overflow === 'auto' || overflow === 'scroll' ? el : null;
}

/** The visible band of the transcript in viewport coordinates. */
function visibleBand(list: HTMLElement | null): { top: number; bottom: number } {
  const scroller = scrollerOf(list);
  if (scroller === null) {
    return { top: stickyBottom(), bottom: window.innerHeight };
  }
  const box = scroller.getBoundingClientRect();
  return { top: Math.max(0, box.top), bottom: Math.min(window.innerHeight, box.bottom) };
}

function scrollTranscriptBy(list: HTMLElement | null, top: number, behavior: ScrollBehavior): void {
  const scroller = scrollerOf(list);
  if (scroller === null) {
    window.scrollBy({ top, behavior });
  } else {
    scroller.scrollBy({ top, behavior });
  }
}

function prefersReducedMotion(): boolean {
  return typeof window.matchMedia === 'function' && window.matchMedia('(prefers-reduced-motion: reduce)').matches;
}

function ProgressCard({ title, text, stage, children }: { title: string; text: string; stage: StageStatus | null; children?: JSX.Element | null }): JSX.Element {
  const percent = stage?.percent ?? null;
  return (
    <div class="tx-empty">
      <span class="tx-empty-tile" aria-hidden="true">
        <TranscriptLinesIcon size={20} />
      </span>
      <h2 class="tx-empty-title">{title}</h2>
      {percent === null ? null : (
        <div
          class="tx-progress"
          role="progressbar"
          aria-label={title}
          aria-valuemin={0}
          aria-valuemax={100}
          aria-valuenow={Math.round(percent)}
          aria-valuetext={title}
        >
          <div class="bar active" style={{ width: `${Math.max(0, Math.min(100, percent))}%` }} />
        </div>
      )}
      <p class="tx-empty-text">{text}</p>
      {children ?? null}
    </div>
  );
}

function FailedCard({
  failure,
  waiting,
  onRemedy,
  onDetails,
  onSettings,
}: {
  failure: StageFailure;
  /** The stage only waits for a model to be installed (label "Waiting for a model"): nothing went wrong. */
  waiting: boolean;
  onRemedy: (remedyId: string) => void;
  onDetails: () => void;
  onSettings: () => void;
}): JSX.Element {
  const label = waiting ? WAITING_FOR_MODEL : (FAILED_LABEL[failure.stage] ?? 'Processing failed');
  return (
    <section class={waiting ? 'tx-failed tx-failed--waiting' : 'tx-failed'} aria-labelledby="tx-failed-label">
      <div class="tx-failed-text">
        <div id="tx-failed-label" class="tx-failed-label">
          <span class="tx-failed-dot" aria-hidden="true" />
          {label}
        </div>
        <p class="tx-failed-body">
          {failure.message} {failure.kept}
        </p>
      </div>
      <div class="tx-failed-actions">
        {waiting ? (
          <button class="btn p" type="button" onClick={onSettings}>
            {failure.stage === 'speakers' ? 'Open Settings › Speakers' : 'Open Settings › Transcription'}
          </button>
        ) : null}
        {failure.remedies.map((remedy, i) => (
          <button
            key={remedy.id}
            class={i === 0 && !waiting ? 'btn p' : 'btn g'}
            type="button"
            onClick={() => {
              onRemedy(remedy.id);
            }}
          >
            {remedy.label}
          </button>
        ))}
        <button class="btn g" type="button" onClick={onDetails}>
          Details
        </button>
      </div>
    </section>
  );
}

/** What a coverage notice offers: transcribe again with another installed model. */
export interface GapAction {
  label: string;
  run: () => void;
}

/**
 * A coverage notice at the gap's place in the transcript (BRIDGE.md, `Transcript.coverageGaps`):
 * accent-soft, like the other still-safe conditions (DESIGN.md §2.1, §5.19).
 */
function GapNotice({ gap, action }: { gap: CoverageGap; action: GapAction | null }): JSX.Element {
  const stop = (event: Event): void => {
    event.stopPropagation();
  };
  return (
    <div class="tx-gap" role="note" onClick={stop} onDblClick={stop}>
      <span class="tx-gap-text">
        {gapNoticeText(gap)}
        {action === null ? ' Install another model in Settings › Transcription to transcribe it again.' : null}
      </span>
      {action === null ? null : (
        <button class="btn ghost small-btn tx-gap-action" type="button" onClick={action.run}>
          {action.label}
        </button>
      )}
    </div>
  );
}

/** A thin line above the segments while a new pass or the speakers stage runs. */
function RunningLine({ stages }: { stages: StageStatus[] }): JSX.Element | null {
  const transcript = stages.find((s) => s.stage === 'transcript' && (s.state === 'active' || s.state === 'queued'));
  const speakers = stages.find((s) => s.stage === 'speakers' && s.state === 'active');
  if (transcript !== undefined) {
    const text =
      transcript.state === 'queued'
        ? 'Queued to transcribe again. This transcript stays until the new one is ready.'
        : isPausedLabel(transcript.label)
          ? `${transcript.label ?? 'Paused'}. This transcript stays until the new one is ready.`
          : `${transcribingText(transcript).replace(/^Transcribing/, 'Transcribing again')}. This transcript stays until the new one is ready.`;
    return (
      <p class="tx-running" role="status">
        <span class="pulse tx-running-dot" aria-hidden="true" />
        {text}
      </p>
    );
  }
  if (speakers !== undefined) {
    return (
      <p class="tx-running" role="status">
        <span class="pulse tx-running-dot" aria-hidden="true" />
        Identifying speakers{speakers.percent === null ? '' : ` ${Math.round(speakers.percent)}%`}. Names update when it finishes.
      </p>
    );
  }
  return null;
}

interface ListProps {
  segments: TranscriptSegment[];
  speakers: Speaker[];
  threshold: number;
  currentIndex: number;
  query: string;
  currentMatch: { segmentId: string; occurrence: number } | null;
  highlightsBySegment: Map<string, Highlight[]>;
  editing: EditingLine | null;
  handlers: SegmentHandlers;
  follow: FollowPlayhead;
  revealRef: TranscriptPaneProps['revealRef'];
  focusRef: { current: ((index: number) => void) | null };
  /** Coverage notices by the index of the segment they come before (`segments.length`: after the last). */
  gaps: Map<number, CoverageGap[]>;
  gapAction: GapAction | null;
}

/** The segments, windowed: only the rows near the viewport are in the DOM, spacers stand for the rest. */
function TranscriptList({
  segments,
  speakers,
  threshold,
  currentIndex,
  query,
  currentMatch,
  highlightsBySegment,
  editing,
  handlers,
  follow,
  revealRef,
  focusRef,
  gaps,
  gapAction,
}: ListProps): JSX.Element {
  const listRef = useRef<HTMLDivElement | null>(null);
  const heightsRef = useRef<RowHeights | null>(null);
  heightsRef.current ??= new RowHeights(segments.length, ESTIMATED_ROW);
  const heights = heightsRef.current;
  heights.resize(segments.length);
  const [view, setView] = useState({ top: 0, bottom: typeof window === 'undefined' ? 1200 : window.innerHeight });
  const [, setMeasured] = useState(0);
  const [focusIndex, setFocusIndex] = useState<number | null>(null);
  const pendingReveal = useRef<{ index: number; tries: number } | null>(null);
  const speakerById = useMemo(() => new Map(speakers.map((s) => [s.id, s])), [speakers]);
  const editingIndex = editing === null ? -1 : segments.findIndex((s) => s.id === editing.segmentId);

  /** The visible part of the list in list coordinates, from the transcript's (or the page's) scroll position. */
  const measureView = (): void => {
    const list = listRef.current;
    if (list === null) {
      return;
    }
    const rect = list.getBoundingClientRect();
    if (rect.height === 0 && rect.top === 0) {
      return; // Not laid out (hidden, or a test without layout): keep the current view.
    }
    const band = visibleBand(list);
    const top = Math.max(0, band.top - rect.top);
    const bottom = Math.max(top, band.bottom - rect.top);
    setView((v) => (Math.abs(v.top - top) < 1 && Math.abs(v.bottom - bottom) < 1 ? v : { top, bottom }));
  };

  useEffect(() => {
    let frame = 0;
    const onScroll = (): void => {
      follow.noteScrollEvent();
      cancelAnimationFrame(frame);
      frame = requestAnimationFrame(measureView);
    };
    const onUser = (): void => {
      follow.noteUserScroll();
    };
    const onKey = (event: KeyboardEvent): void => {
      const target = event.target;
      const typing = target instanceof HTMLInputElement || target instanceof HTMLTextAreaElement;
      if (!typing && ['PageUp', 'PageDown', 'Home', 'End', ' ', 'ArrowUp', 'ArrowDown'].includes(event.key)) {
        follow.noteUserScroll();
      }
    };
    // Scroll events do not bubble: listen on the transcript's own area as well as on the page.
    const area = listRef.current?.closest<HTMLElement>(`.${TRANSCRIPT_SCROLLER}`) ?? null;
    area?.addEventListener('scroll', onScroll, { passive: true });
    window.addEventListener('scroll', onScroll, { passive: true });
    window.addEventListener('resize', onScroll);
    window.addEventListener('wheel', onUser, { passive: true });
    window.addEventListener('touchmove', onUser, { passive: true });
    window.addEventListener('keydown', onKey);
    measureView();
    return () => {
      cancelAnimationFrame(frame);
      area?.removeEventListener('scroll', onScroll);
      window.removeEventListener('scroll', onScroll);
      window.removeEventListener('resize', onScroll);
      window.removeEventListener('wheel', onUser);
      window.removeEventListener('touchmove', onUser);
      window.removeEventListener('keydown', onKey);
    };
  }, [follow]);

  const keep = [editingIndex, focusIndex ?? -1].filter((i) => i >= 0);
  const range = computeWindow(heights, view.top, view.bottom, OVERSCAN, keep);

  // Measure what was rendered; a changed height lays the list out again.
  useLayoutEffect(() => {
    const list = listRef.current;
    if (list === null) {
      return;
    }
    let changed = false;
    // A row's slot holds the segment and any coverage notice before it; the slot is what takes the space.
    for (const el of list.querySelectorAll<HTMLElement>('[data-row]')) {
      changed = heights.set(Number(el.dataset.row), el.offsetHeight) || changed;
    }
    if (changed) {
      setMeasured((n) => n + 1);
    }
    // A jump to a row that was not rendered used estimated offsets: put the row where it belongs now.
    const pending = pendingReveal.current;
    const target = pending === null ? null : list.querySelector<HTMLElement>(`[data-index="${pending.index}"]`);
    if (pending !== null && target !== null) {
      const box = target.getBoundingClientRect();
      const band = visibleBand(list);
      const delta = followScrollDelta(box.top, box.bottom, band.top, band.bottom);
      pending.tries -= 1;
      if (delta === null || pending.tries <= 0) {
        pendingReveal.current = null;
      }
      if (delta !== null) {
        follow.noteProgrammaticScroll();
        scrollTranscriptBy(list, delta, 'auto');
      }
    }
  });

  /** Scrolls the page so row `index` sits a third of the way down the visible part. */
  const scrollToIndex = (index: number, onlyIfNeeded: boolean): void => {
    const list = listRef.current;
    if (list === null || index < 0 || index >= segments.length) {
      return;
    }
    const offsets = heights.offsets();
    const rowTop = offsets[index] ?? 0;
    const rowBottom = offsets[index + 1] ?? rowTop;
    const rect = list.getBoundingClientRect();
    const laidOut = !(rect.height === 0 && rect.top === 0);
    if (!laidOut) {
      // No layout to scroll: render around the row instead.
      setView({ top: Math.max(0, rowTop - 200), bottom: rowTop + 800 });
      return;
    }
    const band = visibleBand(list);
    // A rendered row has a real position; otherwise its place comes from the (partly estimated) offsets.
    const el = list.querySelector<HTMLElement>(`[data-index="${index}"]`);
    const box = el?.getBoundingClientRect() ?? null;
    const delta = followScrollDelta(box?.top ?? rect.top + rowTop, box?.bottom ?? rect.top + rowBottom, band.top, band.bottom);
    if (delta === null) {
      if (!onlyIfNeeded) {
        measureView();
      }
      return;
    }
    follow.noteProgrammaticScroll();
    if (box === null) {
      // Render the destination at once, jump there, and correct once the rows there are measured.
      const destinationTop = Math.max(0, band.top - (rect.top - delta));
      setView({ top: destinationTop, bottom: destinationTop + (band.bottom - band.top) });
      pendingReveal.current = { index, tries: 4 };
      scrollTranscriptBy(list, delta, 'auto');
      return;
    }
    scrollTranscriptBy(list, delta, prefersReducedMotion() ? 'auto' : 'smooth');
  };

  revealRef.current = (segmentId) => {
    scrollToIndex(
      segments.findIndex((s) => s.id === segmentId),
      false,
    );
  };

  focusRef.current = (index) => {
    if (index < 0 || index >= segments.length) {
      return;
    }
    setFocusIndex(index);
    scrollToIndex(index, true);
  };

  // Keyboard movement focuses the row once it is rendered.
  useEffect(() => {
    if (focusIndex === null) {
      return;
    }
    const el = listRef.current?.querySelector<HTMLElement>(`[data-index="${focusIndex}"]`);
    if (el !== null && el !== undefined) {
      el.focus({ preventScroll: false });
      setFocusIndex(null);
    }
  });

  // The current segment follows the playhead unless the person is scrolling.
  const lastFollowed = useRef(-1);
  useEffect(() => {
    if (currentIndex < 0 || currentIndex === lastFollowed.current) {
      return;
    }
    lastFollowed.current = currentIndex;
    if (follow.shouldFollow() && editing === null) {
      scrollToIndex(currentIndex, true);
    }
  }, [currentIndex]);

  const rows: JSX.Element[] = [];
  for (let i = range.start; i < range.end; i++) {
    const segment = segments[i];
    if (segment === undefined) {
      continue;
    }
    const isEditing = editing?.segmentId === segment.id;
    const before = gaps.get(i) ?? NO_GAPS;
    const after = i === segments.length - 1 ? (gaps.get(segments.length) ?? NO_GAPS) : NO_GAPS;
    rows.push(
      <div key={segment.id} class="segm-slot" role="none" data-row={i}>
        {before.map((gap) => (
          <GapNotice key={`gap-${gap.start}`} gap={gap} action={gapAction} />
        ))}
        <TranscriptSegmentRow
          segment={segment}
          index={i}
          count={segments.length}
          speaker={segment.speaker === null ? null : (speakerById.get(segment.speaker) ?? null)}
          speakers={speakers}
          current={i === currentIndex}
          threshold={threshold}
          query={query}
          currentOccurrence={currentMatch?.segmentId === segment.id ? currentMatch.occurrence : -1}
          editing={isEditing ? editing.draft : null}
          caret={isEditing ? editing.caret : null}
          saving={isEditing && editing.saving}
          highlights={highlightsBySegment.get(segment.id) ?? NO_HIGHLIGHTS}
          handlers={handlers}
        />
        {after.map((gap) => (
          <GapNotice key={`gap-${gap.start}`} gap={gap} action={gapAction} />
        ))}
      </div>,
    );
  }

  return (
    <div
      ref={listRef}
      class="segm-list"
      role="list"
      aria-label={`Transcript, ${segments.length} ${segments.length === 1 ? 'line' : 'lines'}`}
      style={{ paddingTop: `${range.padTop}px`, paddingBottom: `${range.padBottom}px` }}
    >
      {rows}
    </div>
  );
}

export function TranscriptPane({ project, api, search, player, chapterName, inProgress, onBackToRecording, onShowHistory, onError, revealRef, actions, onSaved }: TranscriptPaneProps): JSX.Element {
  const result = api.result;
  const transcript = result?.transcript ?? null;
  const segments = useMemo(() => transcript?.segments ?? [], [transcript]);
  const speakers = useMemo(() => transcript?.speakers ?? [], [transcript]);
  const follow = useMemo(() => createFollowPlayhead(), []);
  const [editing, setEditing] = useState<EditingLine | null>(null);
  const focusRef = useRef<((index: number) => void) | null>(null);
  const currentIndex = segmentIndexAt(segments, player.positionMs / 1000);
  const transcriptStage = api.stages.find((s) => s.stage === 'transcript') ?? null;

  const highlightsBySegment = useMemo(() => {
    const map = new Map<string, Highlight[]>();
    for (const h of project.highlights) {
      const id = h.segmentId ?? segments[segmentIndexAt(segments, h.atMs / 1000)]?.id;
      if (id !== undefined && segments.some((s) => s.id === id)) {
        map.set(id, [...(map.get(id) ?? []), h]);
      }
    }
    return map;
  }, [project.highlights, segments]);

  const currentMatch = useMemo(() => {
    const match = search.matches[search.current];
    if (match === undefined) {
      return null;
    }
    const occurrence = search.matches.slice(0, search.current).filter((m) => m.segmentId === match.segmentId).length;
    return { segmentId: match.segmentId, occurrence };
  }, [search.matches, search.current]);

  // Handlers stay one object for the life of the pane so the memoised rows do not redraw; each call
  // reads the latest state through the ref.
  const latest = useRef({ editing, segments, speakers, actions, player, follow, onError, onSaved });
  latest.current = { editing, segments, speakers, actions, player, follow, onError, onSaved };
  const handlers = useMemo<SegmentHandlers>(() => {
    const focusSegment = (segmentId: string): void => {
      const index = latest.current.segments.findIndex((s) => s.id === segmentId);
      focusRef.current?.(index);
    };
    const save = (): void => {
      const current = latest.current.editing;
      if (current === null || current.saving) {
        return;
      }
      const segment = latest.current.segments.find((s) => s.id === current.segmentId);
      const text = current.draft.replace(/\s+/g, ' ').trim();
      // Unchanged since editing began (an undo may have changed the line meanwhile): nothing to write.
      if (segment === undefined || text === '' || text === segment.text || text === current.startText.replace(/\s+/g, ' ').trim()) {
        setEditing(null);
        focusSegment(current.segmentId);
        return;
      }
      setEditing({ ...current, saving: true });
      latest.current.actions.editLine(segment, text).then(
        () => {
          setEditing(null);
          focusSegment(current.segmentId);
          latest.current.onSaved();
        },
        (e: unknown) => {
          setEditing({ ...current, saving: false });
          latest.current.onError('The line was not saved', e instanceof Error ? e.message : 'Memento did not answer.');
        },
      );
    };
    const report = (title: string) => (e: unknown): void => {
      latest.current.onError(title, e instanceof Error ? e.message : 'Memento did not answer.');
    };
    return {
      seek: (segment) => {
        latest.current.follow.resume();
        latest.current.player.seek(segment.start * 1000);
      },
      startEdit: (segment, caret) => {
        setEditing({ segmentId: segment.id, draft: segment.text, startText: segment.text, caret: caret ?? null, saving: false });
      },
      draft: (text) => {
        setEditing((e) => (e === null ? e : { ...e, draft: text }));
      },
      save,
      cancel: () => {
        const current = latest.current.editing;
        setEditing(null);
        if (current !== null) {
          focusSegment(current.segmentId);
        }
      },
      assign: (segment, speaker) => {
        latest.current.actions.assignSpeaker(segment, speaker).catch(report('The speaker was not changed'));
      },
      addSpeaker: (segment, name) => {
        latest.current.actions.addSpeaker(segment, name).catch(report(`${name} was not added as a speaker`));
      },
      rename: (speaker, name) => {
        latest.current.actions.renameSpeaker(speaker, name).catch(report(`${speaker.name} was not renamed`));
      },
      renameHighlight: (highlight, note) => {
        latest.current.actions.renameHighlight(highlight, note).then(() => {
          latest.current.onSaved();
        }, report('The highlight was not renamed'));
      },
      moveFocus: (index, direction) => {
        focusRef.current?.(index + direction);
      },
    };
  }, []);

  const reviewed = transcript?.reviewed ?? false;
  const status = result?.status ?? null;
  const failure = result?.failure ?? null;

  // Coverage notices, and the installed model they offer to transcribe again with.
  const services = useServices();
  const { bridge, store } = services;
  const coverageGaps = transcript?.coverageGaps;
  const gaps = useMemo(() => gapPlacement(segments, coverageGaps ?? []), [segments, coverageGaps]);
  const [models, setModels] = useState<ModelInfo[] | null>(null);
  const hasGaps = (coverageGaps?.length ?? 0) > 0;
  useEffect(() => {
    if (!hasGaps) {
      return undefined;
    }
    let live = true;
    bridge
      .call('models.list')
      .then(({ models: list }) => {
        if (live) {
          setModels(list);
        }
      })
      .catch((e: unknown) => {
        console.warn('[review] models.list failed', e);
      });
    return () => {
      live = false;
    };
  }, [bridge, hasGaps]);
  const other = transcript === null || models === null ? null : otherModelFor(transcript.engine.model, store.settings.value?.transcription.cpuFallbackModelId ?? null, models);
  const gapAction: GapAction | null =
    other === null
      ? null
      : {
          label: `Transcribe again with ${other.name}`,
          run: () => {
            // A finished transcript is replaced by a new pass (kept as a version); a stopped one continues with that model.
            const queued = status === 'failed' ? api.retry('transcript', `model:${other.id}`) : api.transcribe(other.id);
            void queued.then((message) => {
              if (message !== null) {
                onError('Transcription was not started', message);
              }
            });
          },
        };

  let body: JSX.Element | null = null;
  if (inProgress) {
    body = (
      <div class="tx-empty">
        <span class="tx-empty-tile" aria-hidden="true">
          <TranscriptLinesIcon size={20} />
        </span>
        <h2 class="tx-empty-title">This recording is still in progress</h2>
        <p class="tx-empty-text">It opens here once it has stopped and its tracks are stored on this PC.</p>
        <button class="btn ghost spoke-ghost" type="button" onClick={onBackToRecording}>
          Back to the recording
        </button>
      </div>
    );
  } else if (api.error !== null && result === null) {
    body = (
      <div class="tx-empty">
        <p class="tx-empty-text" role="alert">
          {api.error}
        </p>
        <button class="btn ghost spoke-ghost" type="button" onClick={api.reload}>
          Try again
        </button>
      </div>
    );
  } else if (result === null) {
    body = <p class="tx-loading">Reading the transcript…</p>;
  } else if (transcript === null) {
    const tracks = project.tracks.length;
    const audio = `${tracks} ${tracks === 1 ? 'track' : 'tracks'}, ${formatDuration(project.summary.durationMs)}, stored on this PC`;
    if (status === 'queued') {
      body = (
        <ProgressCard
          title="Waiting to transcribe"
          stage={null}
          text={`Queued behind the recordings ahead of it; it starts on its own. The audio is complete: ${audio}.`}
        />
      );
    } else if (status === 'running') {
      body = (
        <ProgressCard
          title={transcribingText(transcriptStage)}
          stage={transcriptStage}
          text="The lines appear here when the pass is done. You can listen, add chapters and highlights meanwhile; they line up with the transcript."
        />
      );
    } else if (status === 'paused') {
      body = (
        <ProgressCard
          title={isPausedLabel(transcriptStage?.label ?? null) ? (transcriptStage?.label ?? 'Paused') : 'Transcription paused'}
          stage={transcriptStage}
          text="Transcription waits while the PC is busy and carries on by itself. Recording is never paused for it."
        >
          <button
            class="btn ghost spoke-ghost"
            type="button"
            onClick={() => {
              void api.resume().then((message) => {
                if (message !== null) {
                  onError('Transcription did not resume', message);
                }
              });
            }}
          >
            Resume now
          </button>
        </ProgressCard>
      );
    } else if (status !== 'failed') {
      body = (
        <ProgressCard
          title="Not transcribed yet"
          stage={null}
          text={`The audio is complete: ${audio}. Transcription runs on this PC; nothing is uploaded.`}
        >
          <button
            class="btn primary tx-transcribe"
            type="button"
            onClick={() => {
              void api.transcribe().then((message) => {
                if (message !== null) {
                  onError('Transcription was not started', message);
                }
              });
            }}
          >
            Transcribe now
          </button>
        </ProgressCard>
      );
    }
  }

  const showList = !inProgress && transcript !== null && segments.length > 0;

  return (
    <div class="transcript">
      <div class="transcript-head">
        <span class="lbl">{chapterName}</span>
        <span class="transcript-head-end">
          {showList ? <span class="transcript-hint">Click the time to play a line · click the words to correct them</span> : null}
          {transcript === null ? null : (
            <button
              class={reviewed ? 'chip on tx-reviewed' : 'chip tx-reviewed'}
              type="button"
              aria-pressed={reviewed}
              onClick={() => {
                actions.markReviewed(!reviewed).catch((e: unknown) => {
                  onError('The transcript was not marked', e instanceof Error ? e.message : 'Memento did not answer.');
                });
              }}
            >
              {reviewed ? <CheckIcon size={12} /> : null}
              {reviewed ? 'Reviewed' : 'Mark as reviewed'}
            </button>
          )}
        </span>
      </div>
      {/* The transcript stage's failure, or the speakers stage's while the transcript itself is fine. */}
      {failure !== null && (status === 'failed' || failure.stage !== 'transcript') ? (
        <FailedCard
          failure={failure}
          waiting={api.stages.find((s) => s.stage === failure.stage)?.label === WAITING_FOR_MODEL}
          onDetails={onShowHistory}
          onSettings={() => {
            openSettings(services, failure.stage === 'speakers' ? 'speakers' : 'transcription');
          }}
          onRemedy={(remedyId) => {
            void api.retry(failure.stage, remedyId).then((message) => {
              if (message !== null) {
                onError('The stage was not retried', message);
              }
            });
          }}
        />
      ) : null}
      {status === 'failed' && failure === null && transcript === null ? (
        <ProgressCard title="Transcription failed" stage={null} text="The recording is safe. Try again from the History tab." />
      ) : null}
      {body}
      {showList ? (
        <>
          {status === 'failed' ? <p class="tx-partial">The partial transcript, up to {formatDuration((segments.at(-1)?.end ?? 0) * 1000)}:</p> : null}
          <RunningLine stages={api.stages} />
          <TranscriptList
            segments={segments}
            speakers={speakers}
            threshold={transcript.lowConfidenceThreshold}
            currentIndex={currentIndex}
            query={search.query}
            currentMatch={currentMatch}
            highlightsBySegment={highlightsBySegment}
            editing={editing}
            handlers={handlers}
            follow={follow}
            revealRef={revealRef}
            focusRef={focusRef}
            gaps={gaps}
            gapAction={gapAction}
          />
        </>
      ) : null}
    </div>
  );
}
