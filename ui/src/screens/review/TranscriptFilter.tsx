// Review's transcript filters (DESIGN.md §9, after 1.2.0): the view a filter gives (which lines show,
// with counts and labels), the Filter control in the transcript header (a popover of native
// checkboxes and radios, §5.9), and the "Showing 42 of 318 lines · Sarah · Highlights" line with
// Show all and Copy. Filtered-out lines are hidden, never removed; the filter lives in Review's state
// for the open recording only.
import type { JSX } from 'preact';
import { useEffect, useMemo, useRef, useState } from 'preact/hooks';
import type { Chapter, Highlight, Speaker, Transcript, TranscriptSegment } from '../../bridge/types';
import { PopoverLayer } from '../../components/Floating';
import { CloseIcon } from '../../components/icons';
import { formatDuration } from '../../format/duration';
import {
  activeFilter,
  filterCounts,
  filterLabels,
  highlightedSegments,
  isFiltering,
  NO_FILTER,
  segmentPasses,
  showingText,
  type FilterContext,
  type FilterCounts,
  type TranscriptFilter,
} from '../../format/transcriptFilter';
import './filter.css';

/** What the transcript shows under a filter. */
export interface TranscriptView {
  filter: TranscriptFilter;
  /** Some part of the filter narrows the transcript now. */
  filtering: boolean;
  /** The ids of the lines the filter shows; null while nothing is filtered. */
  visibleIds: ReadonlySet<string> | null;
  /** Lines shown and lines in all. */
  visible: number;
  total: number;
  /** The active parts in words ("Sarah", "Highlights"). */
  labels: string[];
  /** How many lines each single choice would show. */
  counts: FilterCounts;
  /** For the predicate (TranscriptPane keeps the line being edited visible). */
  context: FilterContext;
}

const NO_SEGMENTS: readonly TranscriptSegment[] = [];

/** The filter applied to the transcript as it is now: annotations, edits and the search text are read live. */
export function useTranscriptView(transcript: Transcript | null, highlights: readonly Highlight[], chapters: readonly Chapter[], filter: TranscriptFilter, query: string): TranscriptView {
  const segments = transcript?.segments ?? NO_SEGMENTS;
  const threshold = transcript?.lowConfidenceThreshold ?? 0.5;
  const highlighted = useMemo(() => highlightedSegments(highlights, segments), [highlights, segments]);
  const context = useMemo<FilterContext>(() => ({ highlighted, chapters, threshold, query }), [highlighted, chapters, threshold, query]);
  const counts = useMemo(() => filterCounts(segments, context), [segments, context]);
  return useMemo(() => {
    const filtering = isFiltering(activeFilter(filter, context));
    const ids = filtering ? new Set(segments.filter((s) => segmentPasses(s, filter, context)).map((s) => s.id)) : null;
    return {
      filter,
      filtering,
      visibleIds: ids,
      visible: ids?.size ?? segments.length,
      total: segments.length,
      labels: filterLabels(filter, transcript?.speakers ?? [], context),
      counts,
      context,
    };
  }, [filter, context, counts, segments, transcript?.speakers]);
}

function count(n: number | undefined): string {
  return String(n ?? 0);
}

interface FilterMenuProps {
  view: TranscriptView;
  speakers: readonly Speaker[];
  chapters: readonly Chapter[];
  query: string;
  onFilter: (filter: TranscriptFilter) => void;
}

/** The Filter control in the transcript header and its popover. Esc closes it and returns to the button. */
export function TranscriptFilterMenu({ view, speakers, chapters, query, onFilter }: FilterMenuProps): JSX.Element {
  const [open, setOpen] = useState(false);
  const rootRef = useRef<HTMLDivElement | null>(null);
  const buttonRef = useRef<HTMLButtonElement | null>(null);
  const popRef = useRef<HTMLElement | null>(null);
  const filter = view.filter;
  const parts = view.labels.length;

  useEffect(() => {
    if (!open) {
      return undefined;
    }
    popRef.current?.querySelector<HTMLElement>('input, button')?.focus();
    const onPointerDown = (event: PointerEvent): void => {
      const target = event.target;
      if (target instanceof Node && rootRef.current?.contains(target) !== true && popRef.current?.contains(target) !== true) {
        setOpen(false);
      }
    };
    document.addEventListener('pointerdown', onPointerDown);
    return () => {
      document.removeEventListener('pointerdown', onPointerDown);
    };
  }, [open]);

  const close = (focusButton: boolean): void => {
    setOpen(false);
    if (focusButton) {
      buttonRef.current?.focus();
    }
  };
  const set = (changes: Partial<TranscriptFilter>): void => {
    onFilter({ ...filter, ...changes });
  };
  const sortedChapters = [...chapters].sort((a, b) => a.atMs - b.atMs);
  const searching = query.trim() !== '';
  const check = (id: string, label: string, n: number, checked: boolean, onChange: () => void): JSX.Element => (
    <label key={id} class="tx-filter-option">
      <input class="chk" type="checkbox" checked={checked} onChange={onChange} />
      <span class="tx-filter-option-name">{label}</span>
      <span class="mono tx-filter-option-count" aria-label={`${n} ${n === 1 ? 'line' : 'lines'}`}>
        {n}
      </span>
    </label>
  );

  return (
    <div class="menu-root tx-filter" ref={rootRef}>
      <button
        ref={buttonRef}
        class={view.filtering ? 'chip on tx-filter-btn' : 'chip tx-filter-btn'}
        type="button"
        aria-haspopup="dialog"
        aria-expanded={open}
        aria-label={view.filtering ? `Filter the transcript: ${view.labels.join(', ')}` : 'Filter the transcript'}
        onClick={() => {
          setOpen(!open);
        }}
        onKeyDown={(event) => {
          if (event.key === 'ArrowDown') {
            event.preventDefault();
            setOpen(true);
          }
        }}
      >
        Filter{view.filtering ? <span class="tx-filter-badge"> · {parts}</span> : null}
      </button>
      {open ? (
        <PopoverLayer anchorRef={rootRef} popRef={popRef} capHeight={480}>
          <div
            ref={(el) => {
              popRef.current = el;
            }}
            class="popover tx-filter-pop"
            role="dialog"
            aria-label="Filter the transcript"
            onKeyDown={(event) => {
              if (event.key === 'Escape') {
                event.preventDefault();
                event.stopPropagation();
                close(true);
              }
            }}
          >
            {speakers.length > 0 ? (
              <fieldset class="tx-filter-group">
                <legend class="tx-filter-legend">Speakers</legend>
                {speakers.map((s) =>
                  check(`sp-${s.id}`, s.name, view.counts.speakers.get(s.id) ?? 0, filter.speakers.includes(s.id), () => {
                    set({ speakers: filter.speakers.includes(s.id) ? filter.speakers.filter((id) => id !== s.id) : [...filter.speakers, s.id] });
                  }),
                )}
              </fieldset>
            ) : null}
            <fieldset class="tx-filter-group">
              <legend class="tx-filter-legend">Only lines with</legend>
              {check('highlights', 'Highlights', view.counts.highlights, filter.highlights, () => {
                set({ highlights: !filter.highlights });
              })}
              {check('uncertain', 'Uncertain words', view.counts.uncertain, filter.uncertain, () => {
                set({ uncertain: !filter.uncertain });
              })}
              {check('edited', 'Edited lines', view.counts.edited, filter.edited, () => {
                set({ edited: !filter.edited });
              })}
              {searching
                ? check('search', `The search “${query.trim()}”`, view.counts.search, filter.search, () => {
                    set({ search: !filter.search });
                  })
                : null}
            </fieldset>
            {sortedChapters.length > 0 ? (
              <fieldset class="tx-filter-group">
                <legend class="tx-filter-legend">Chapter</legend>
                <label class="tx-filter-option">
                  <input
                    class="chk"
                    type="radio"
                    name="tx-filter-chapter"
                    checked={view.filter.chapterId === null || !sortedChapters.some((c) => c.id === view.filter.chapterId)}
                    onChange={() => {
                      set({ chapterId: null });
                    }}
                  />
                  <span class="tx-filter-option-name">Every chapter</span>
                </label>
                {sortedChapters.map((c) => (
                  <label key={c.id} class="tx-filter-option">
                    <input
                      class="chk"
                      type="radio"
                      name="tx-filter-chapter"
                      checked={filter.chapterId === c.id}
                      onChange={() => {
                        set({ chapterId: c.id });
                      }}
                    />
                    <span class="mono tx-filter-option-at">{formatDuration(c.atMs)}</span>
                    <span class="tx-filter-option-name">{c.title.trim() === '' ? 'Untitled chapter' : c.title}</span>
                    <span class="mono tx-filter-option-count" aria-label={`${count(view.counts.chapters.get(c.id))} lines`}>
                      {count(view.counts.chapters.get(c.id))}
                    </span>
                  </label>
                ))}
              </fieldset>
            ) : null}
            <p class="tx-filter-note">A line shows when it matches every choice, and any of the ticked speakers.</p>
            <div class="tx-filter-foot">
              <button
                class="btn ghost small-btn"
                type="button"
                disabled={!view.filtering}
                onClick={() => {
                  onFilter(NO_FILTER);
                  close(true);
                }}
              >
                Show all
              </button>
              <button
                class="btn ghost small-btn"
                type="button"
                onClick={() => {
                  close(true);
                }}
              >
                Done
              </button>
            </div>
          </div>
        </PopoverLayer>
      ) : null}
    </div>
  );
}

/** "Showing 42 of 318 lines · Sarah · Highlights" with Copy and Show all, while a filter narrows the transcript. */
export function FilterLine({ view, onShowAll, onCopy }: { view: TranscriptView; onShowAll: () => void; onCopy: () => void }): JSX.Element | null {
  if (!view.filtering) {
    return null;
  }
  return (
    <div class="tx-filter-line">
      <p class="tx-filter-line-text" role="status">
        {view.visible === 0 ? ['No lines match', ...view.labels].join(' · ') : showingText(view.visible, view.total, view.labels)}
      </p>
      <span class="tx-filter-line-actions">
        <button class="btn ghost small-btn" type="button" disabled={view.visible === 0} onClick={onCopy} title={`Copy the ${view.visible} lines shown`}>
          Copy
        </button>
        <button class="btn ghost small-btn tx-filter-showall" type="button" onClick={onShowAll} aria-keyshortcuts="Escape" title="Show all lines (Esc)">
          <CloseIcon size={12} />
          Show all
        </button>
      </span>
    </div>
  );
}
