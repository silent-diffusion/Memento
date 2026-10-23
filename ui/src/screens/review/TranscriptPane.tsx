// The transcript area of Review (DESIGN.md §9, §5.12, §17): the label row (current chapter, hint,
// Mark as reviewed), the state of the transcript stage when there is no transcript yet (or a
// failed pass), and the segments as a windowed list that follows the playhead.
import type { JSX } from 'preact';
import { useEffect, useLayoutEffect, useMemo, useRef, useState } from 'preact/hooks';
import type { CoverageGap, Highlight, ModelInfo, Project, Speaker, StageFailure, StageStatus, TranscriptSegment } from '../../bridge/types';
import { CheckIcon, TranscriptLinesIcon } from '../../components/icons';
import { formatDuration } from '../../format/duration';
import { gapNoticeText, gapPlacement, isPausedLabel, isSpeakerUncertain, otherModelFor, segmentIndexAt, transcribingText } from '../../format/transcript';
import { openSettings } from '../../state/actions';
import { useServices } from '../../state/context';
import { computeWindow, RowHeights } from '../../format/virtualList';
import { orderSpeakers } from '../../format/speakerOrder';
import { sectionChoices } from '../../format/speakerRuns';
import { createFollowPlayhead, followScrollDelta, type FollowPlayhead } from '../../state/followPlayhead';
import type { PlayerApi } from './Player';
import { TranscriptSegmentRow, type SegmentHandlers } from './TranscriptSegment';
import { TurnBoundary } from './TurnBoundary';
import type { ReviewActions } from './reviewActions';
import type { SearchApi, TranscriptApi } from './useTranscript';
import { NO_FILTER, type TranscriptFilter } from '../../format/transcriptFilter';
import { FilterLine, TranscriptFilterMenu, type TranscriptView } from './TranscriptFilter';
import { useLineSelection, type LineActions } from './LineSelection';

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
  /** After 1.2.0: the filter (Review's state for this recording) and what it shows; the list shows only those lines. */
  view?: TranscriptView;
  onFilter?: (filter: TranscriptFilter) => void;
  /** Copy the lines the filter shows. */
  onCopyVisible?: () => void;
  /** 2.0: selection mode and the line context menu act through these; without them neither is offered. */
  lineActions?: LineActions;
  /** 2.6.0: the pane's follow-the-playhead state, so the screen can resume following (Hear {name}). */
  followRef?: { current: FollowPlayhead | null };
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
        <button class="btn ghost tx-gap-action" type="button" onClick={action.run}>
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
  /** The transcript's line count when a filter shows fewer (the list's accessible name says "42 of 318 lines"). */
  total?: number;
  /** 2.0 selection mode: the selected lines, or null outside it. */
  selectedIds?: ReadonlySet<string> | null;
  /** 2.6.0: the lines a hovered or active choice in a speaker menu would move, or null. */
  previewIds?: ReadonlySet<string> | null;
  /** 2.6.0: the line whose speaker menu asked which lines sound the same, with the host's answer; only its row redraws. */
  sameVoice?: { segmentId: string; ids: readonly string[] } | null;
  /** 2.6.0: the line whose speaker menu opens by itself (the second part of a line just cut); `key` grows each time. */
  openMenu?: { segmentId: string; key: number } | null;
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
  total,
  selectedIds = null,
  previewIds = null,
  sameVoice = null,
  openMenu = null,
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

  // 2.6.0: the turn boundary under row `i` when the next line shown has another speaker (never while selecting
  // or editing); it lives beside the memoised row, so the rows' props do not change with it.
  const boundaryAfter = (i: number): JSX.Element | null => {
    const above = segments[i];
    const below = segments[i + 1];
    if (above === undefined || below === undefined || selectedIds !== null || editing !== null || above.speaker === null || below.speaker === null || above.speaker === below.speaker) {
      return null;
    }
    const aboveSpeaker = speakerById.get(above.speaker);
    const belowSpeaker = speakerById.get(below.speaker);
    if (aboveSpeaker === undefined || belowSpeaker === undefined) {
      return null;
    }
    return (
      <TurnBoundary
        above={above}
        below={below}
        aboveSpeaker={aboveSpeaker}
        belowSpeaker={belowSpeaker}
        onGiveAbove={() => {
          handlers.assign(above, belowSpeaker);
        }}
        onGiveBelow={() => {
          handlers.assign(below, aboveSpeaker);
        }}
      />
    );
  };

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
          selected={selectedIds === null ? null : selectedIds.has(segment.id)}
          previewed={previewIds?.has(segment.id) ?? false}
          sameVoice={sameVoice?.segmentId === segment.id ? sameVoice.ids : null}
          openMenu={openMenu?.segmentId === segment.id ? openMenu.key : 0}
        />
        {boundaryAfter(i)}
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
      aria-label={
        total !== undefined && total !== segments.length
          ? `Transcript, ${segments.length} of ${total} lines shown`
          : `Transcript, ${segments.length} ${segments.length === 1 ? 'line' : 'lines'}`
      }
      style={{ paddingTop: `${range.padTop}px`, paddingBottom: `${range.padBottom}px` }}
    >
      {rows}
    </div>
  );
}

export function TranscriptPane({
  project,
  api,
  search,
  player,
  chapterName,
  inProgress,
  onBackToRecording,
  onShowHistory,
  onError,
  revealRef,
  actions,
  onSaved,
  view,
  onFilter,
  onCopyVisible,
  lineActions,
  followRef,
}: TranscriptPaneProps): JSX.Element {
  const result = api.result;
  const transcript = result?.transcript ?? null;
  const allSegments = useMemo(() => transcript?.segments ?? [], [transcript]);
  // Named people first, then "Speaker n", each by first appearance (format/speakerOrder.ts).
  const speakers = useMemo(() => orderSpeakers(transcript?.speakers ?? [], transcript?.segments ?? []), [transcript]);
  const follow = useMemo(() => createFollowPlayhead(), []);
  useEffect(() => {
    if (followRef !== undefined) {
      followRef.current = follow;
    }
  }, [follow, followRef]);
  const [editing, setEditing] = useState<EditingLine | null>(null);
  // 2.6.0: the second part of a line just cut asks who said it: its speaker menu opens by itself (the key grows so a
  // second cut of the same line opens it again).
  const [autoMenu, setAutoMenu] = useState<{ segmentId: string; key: number } | null>(null);
  const focusRef = useRef<((index: number) => void) | null>(null);
  // After 1.2.0: a filter hides lines; everything below (playhead, keys, editing, Undo) works on the lines shown.
  // The line being edited stays shown until it is saved, even when the edit takes it out of the filter.
  const visibleIds = view?.visibleIds ?? null;
  const editingId = editing?.segmentId ?? null;
  const segments = useMemo(
    () => (visibleIds === null ? allSegments : allSegments.filter((s) => visibleIds.has(s.id) || s.id === editingId)),
    [allSegments, visibleIds, editingId],
  );
  const filtered = visibleIds !== null;
  const currentIndex = segmentIndexAt(segments, player.positionMs / 1000);
  const transcriptStage = api.stages.find((s) => s.stage === 'transcript') ?? null;

  const highlightsBySegment = useMemo(() => {
    const map = new Map<string, Highlight[]>();
    for (const h of project.highlights) {
      const id = h.segmentId ?? allSegments[segmentIndexAt(allSegments, h.atMs / 1000)]?.id;
      if (id !== undefined && allSegments.some((s) => s.id === id)) {
        map.set(id, [...(map.get(id) ?? []), h]);
      }
    }
    return map;
  }, [project.highlights, allSegments]);

  const currentMatch = useMemo(() => {
    const match = search.matches[search.current];
    if (match === undefined) {
      return null;
    }
    const occurrence = search.matches.slice(0, search.current).filter((m) => m.segmentId === match.segmentId).length;
    return { segmentId: match.segmentId, occurrence };
  }, [search.matches, search.current]);

  // 2.6.0: while a choice in a speaker menu (This turn, From here on, Merge) is hovered or active, the lines
  // it would move are marked. The set changes only as the active choice does, so the rows barely redraw.
  const [previewIds, setPreviewIds] = useState<ReadonlySet<string> | null>(null);
  const preview = (segmentIds: readonly string[] | null): void => {
    setPreviewIds(segmentIds === null ? null : new Set(segmentIds));
  };

  // 2.6.0: which other lines sound like a line (transcript.sameVoice), asked once when its speaker menu opens and kept
  // per line until the transcript changes (a speaker change moves lines; a new pass renumbers them). A failed ask reads
  // as no such lines; the menu never waits for the answer.
  const sameVoice = useRef<{ lines: Map<string, readonly string[]> }>({ lines: new Map() });
  // The last answer, handed to that one row so its open menu can show the choice; the other rows see no change.
  const [answered, setAnswered] = useState<{ segmentId: string; ids: readonly string[] } | null>(null);
  useEffect(() => {
    sameVoice.current = { lines: new Map() };
    setAnswered(null);
  }, [allSegments]);
  const prepareLine = (segment: TranscriptSegment): void => {
    const cache = sameVoice.current;
    if (cache.lines.has(segment.id)) {
      return;
    }
    cache.lines.set(segment.id, []);
    latest.current.api.sameVoice(segment.id).then(
      (ids) => {
        if (sameVoice.current === cache) {
          cache.lines.set(segment.id, ids);
          setAnswered({ segmentId: segment.id, ids });
        }
      },
      (e: unknown) => {
        console.warn('[review] transcript.sameVoice failed', e);
      },
    );
  };
  const sameVoiceOf = (segment: TranscriptSegment): readonly string[] | undefined => sameVoice.current.lines.get(segment.id);

  // 2.0: selection mode and the line context menu (LineSelection.tsx), over the lines shown.
  const allIds = useMemo(() => new Set(allSegments.map((s) => s.id)), [allSegments]);
  const selection = useLineSelection({
    segments,
    allSegments,
    allIds,
    highlightsBySegment,
    speakers,
    actions: lineActions,
    focusLine: (segmentId) => {
      focusRef.current?.(latest.current.segments.findIndex((s) => s.id === segmentId));
    },
    preview,
    prepare: prepareLine,
    sameVoiceOf,
  });
  const withSelection = useRef(lineActions !== undefined).current;

  // The services: the coverage notices below read the bridge, and the key handlers check that no dialog is open.
  const services = useServices();
  const { bridge, store } = services;

  // 2.6.0: what U / Shift+U say when there is no uncertain line that way, shown in the hint's place for 4 s.
  const [keyNote, setKeyNote] = useState<string | null>(null);
  const noteTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  useEffect(
    () => () => {
      if (noteTimer.current !== null) {
        clearTimeout(noteTimer.current);
      }
    },
    [],
  );
  const note = (text: string | null): void => {
    if (noteTimer.current !== null) {
      clearTimeout(noteTimer.current);
      noteTimer.current = null;
    }
    setKeyNote(text);
    if (text !== null) {
      noteTimer.current = setTimeout(() => {
        setKeyNote(null);
      }, 4000);
    }
  };

  // Handlers stay one object for the life of the pane so the memoised rows do not redraw; each call
  // reads the latest state through the ref.
  const latest = useRef({ editing, segments, allSegments, speakers, actions, lineActions, api, player, follow, onError, onSaved, selection, store });
  latest.current = { editing, segments, allSegments, speakers, actions, lineActions, api, player, follow, onError, onSaved, selection, store };
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
      // 2.6.0: Ctrl+Enter while editing: the draft is saved when it changed (its own Undo step), then the line is cut
      // at the caret (another), and the second part's speaker menu opens so the next keystroke says who said it.
      splitAt: (caret) => {
        const current = latest.current.editing;
        if (current === null || current.saving) {
          return;
        }
        const segment = latest.current.segments.find((s) => s.id === current.segmentId);
        const text = current.draft.replace(/\s+/g, ' ').trim();
        if (segment === undefined || text === '') {
          return;
        }
        // The caret's place in the draft, carried into the saved text (whitespace collapses on save).
        const at = current.draft.slice(0, caret).replace(/\s+/g, ' ').trimStart().length;
        setEditing({ ...current, saving: true });
        const run = async (): Promise<void> => {
          if (text !== segment.text) {
            await latest.current.actions.editLine(segment, text);
          }
          const second = await latest.current.actions.splitLine({ ...segment, text }, at);
          setEditing(null);
          latest.current.onSaved();
          setAutoMenu((m) => ({ segmentId: second.id, key: (m?.key ?? 0) + 1 }));
        };
        run().catch((e: unknown) => {
          setEditing((e2) => (e2 === null ? e2 : { ...e2, saving: false }));
          report('The line was not split')(e);
        });
      },
      assign: (segment, speaker) => {
        latest.current.actions.assignSpeaker(segment, speaker).catch(report('The speaker was not changed'));
      },
      merge: (from, into) => {
        latest.current.actions.mergeSpeakers(from, into).catch(report(`${from.name} was not merged into ${into.name}`));
      },
      sections: (segment, chosen, alike) => {
        // Over the whole transcript, not the lines a filter shows: a turn is the run in the conversation.
        const { allSegments: all, speakers: people } = latest.current;
        const from = people.find((s) => s.id === segment.speaker);
        if (from === undefined) {
          return [];
        }
        const choices = sectionChoices(all, all.findIndex((s) => s.id === segment.id), from, chosen, alike ?? sameVoiceOf(segment));
        // Without the 2.0 line actions there is no way to move several lines in one step: the line, the swaps or the merge.
        return withSelection ? choices : choices.filter((c) => c.id !== 'voice' && c.id !== 'turn' && c.id !== 'onward');
      },
      prepare: prepareLine,
      swap: (speaker, other, from) => {
        latest.current.actions.swapSpeakers(speaker, other, from).catch(report(`${speaker.name} and ${other.name} were not swapped`));
      },
      preview,
      ...(withSelection
        ? {
            assignLines: (lines: readonly TranscriptSegment[], speaker: Speaker) => {
              latest.current.lineActions?.assign(lines, speaker).catch(report('The speaker was not changed'));
            },
          }
        : {}),
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
      // 2.6.0: a digit moves the focused line to that person in the People order; the line keeps focus, so
      // ↓ then a digit is the rhythm. Nothing happens for a digit past the list, the line's own speaker, or a dialog.
      assignKey: (segment, number) => {
        const { speakers: people, store: s, actions: acts } = latest.current;
        const speaker = people[number - 1];
        if (s.dialog.value !== null || speaker === undefined || speaker.id === segment.speaker) {
          return;
        }
        acts.assignSpeaker(segment, speaker).catch(report('The speaker was not changed'));
      },
      // 2.6.0: U / Shift+U: the next or previous line shown whose speaker is uncertain (or missing, once speakers
      // were identified) takes focus and plays from its start; with none that way, the hint says so for 4 s.
      jumpUncertain: (index, direction) => {
        const { segments: shown, speakers: people, player: p, follow: f, store: s } = latest.current;
        if (s.dialog.value !== null) {
          return;
        }
        const doubtful = (line: TranscriptSegment): boolean => isSpeakerUncertain(line) || (people.length > 0 && line.speaker === null);
        let at = index + direction;
        let target: TranscriptSegment | undefined;
        for (; at >= 0 && at < shown.length; at += direction) {
          const line = shown[at];
          if (line !== undefined && doubtful(line)) {
            target = line;
            break;
          }
        }
        if (target === undefined) {
          note(direction === 1 ? 'No uncertain lines after this one' : 'No uncertain lines before this one');
          return;
        }
        note(null);
        focusRef.current?.(at);
        f.resume();
        p.seek(target.start * 1000);
        if (!p.playing) {
          p.toggle();
        }
      },
      ...(withSelection
        ? {
            select: (segment: TranscriptSegment, how: 'toggle' | 'range') => {
              latest.current.selection.select(segment, how);
            },
            selectAll: () => {
              latest.current.selection.selectAll();
            },
            extend: (segment: TranscriptSegment, direction: 1 | -1) => {
              latest.current.selection.extend(segment, direction);
            },
            contextMenu: (segment: TranscriptSegment, at: { x: number; y: number }) => {
              latest.current.selection.openMenu(segment, at);
            },
          }
        : {}),
    };
  }, []);

  const reviewed = transcript?.reviewed ?? false;
  const status = result?.status ?? null;
  const failure = result?.failure ?? null;

  // Coverage notices, and the installed model they offer to transcribe again with.
  const coverageGaps = transcript?.coverageGaps;
  // A filtered view leaves the coverage notices out: they mark missing words, not lines.
  const gaps = useMemo(() => (filtered ? new Map<number, CoverageGap[]>() : gapPlacement(segments, coverageGaps ?? [])), [filtered, segments, coverageGaps]);
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
        <button class="btn ghost" type="button" onClick={onBackToRecording}>
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
        <button class="btn ghost" type="button" onClick={api.reload}>
          Try again
        </button>
      </div>
    );
  } else if (result === null) {
    body = <p class="tx-loading">Reading the transcript…</p>;
  } else if (transcript === null) {
    const tracks = project.tracks.length;
    const kept = project.mixOnly !== null ? 'the mix only' : `${tracks} ${tracks === 1 ? 'track' : 'tracks'}`;
    const audio = `${kept}, ${formatDuration(project.summary.durationMs)}, stored on this PC`;
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
            class="btn ghost"
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

  const showList = !inProgress && transcript !== null && allSegments.length > 0;

  // Esc anywhere in Review shows every line again, unless a field, a menu or a dialog takes it first.
  const filtering = view?.filtering ?? false;
  useEffect(() => {
    if (!filtering || onFilter === undefined) {
      return undefined;
    }
    const onKey = (event: KeyboardEvent): void => {
      const target = event.target;
      const typing = target instanceof HTMLInputElement || target instanceof HTMLTextAreaElement || target instanceof HTMLSelectElement || (target instanceof HTMLElement && target.isContentEditable);
      if (event.key === 'Escape' && !event.defaultPrevented && !typing && store.dialog.value === null) {
        event.preventDefault();
        onFilter(NO_FILTER);
      }
    };
    document.addEventListener('keydown', onKey);
    return () => {
      document.removeEventListener('keydown', onKey);
    };
  }, [filtering, onFilter, store]);

  // A new filter starts the list from its first line.
  const listRef = useRef<HTMLDivElement | null>(null);
  const filterKey = visibleIds === null ? 'all' : JSON.stringify(view?.filter ?? NO_FILTER);
  const lastKey = useRef(filterKey);
  useEffect(() => {
    if (lastKey.current === filterKey) {
      return;
    }
    lastKey.current = filterKey;
    const scroller = listRef.current?.closest<HTMLElement>(`.${TRANSCRIPT_SCROLLER}`) ?? null;
    if (scroller !== null) {
      scroller.scrollTop = 0;
    }
  }, [filterKey]);

  return (
    <div class="transcript">
      <div class="transcript-head">
        <span class="lbl">{chapterName}</span>
        <span class="transcript-head-end">
          {showList ? (
            // 2.6.0: with speakers, the hint also names the keys that fix them; U's "none that way" note takes its place.
            <span class="transcript-hint" role={keyNote === null ? undefined : 'status'}>
              {keyNote ??
                (speakers.length > 0
                  ? 'Click the time to play a line · click the words to correct them · U for the next uncertain line · 1–9 move it to a speaker'
                  : 'Click the time to play a line · click the words to correct them')}
            </span>
          ) : null}
          {showList && view !== undefined && onFilter !== undefined ? (
            <TranscriptFilterMenu view={view} speakers={speakers} chapters={project.chapters} query={search.query} onFilter={onFilter} />
          ) : null}
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
          {view === undefined || onFilter === undefined ? null : (
            <FilterLine
              view={view}
              onShowAll={() => {
                onFilter(NO_FILTER);
              }}
              onCopy={() => onCopyVisible?.()}
            />
          )}
          {selection.bar}
          <div ref={listRef} class="tx-list-wrap" role="none">
          <TranscriptList
            key={filterKey}
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
            {...(filtered ? { total: allSegments.length } : {})}
            selectedIds={selection.selectedIds}
            previewIds={previewIds}
            sameVoice={answered}
            openMenu={autoMenu}
          />
          </div>
          {selection.menu}
        </>
      ) : null}
    </div>
  );
}
