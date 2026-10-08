// Review's transcript filters (DESIGN.md §9, after 1.2.0): which lines a filter shows, how many each
// choice would show, and the words of the "Showing 42 of 318 lines · Sarah · Highlights" line.
// Kinds combine with AND; several speakers combine with OR. Filtered-out lines are hidden, never removed.
import type { Chapter, Highlight, Speaker, TranscriptSegment } from '../bridge/types';
import { formatDuration } from './duration';
import { matchesQuery, segmentIndexAt } from './transcript';

export interface TranscriptFilter {
  /** Speaker ids; empty means every speaker. */
  speakers: readonly string[];
  /** Only lines with a highlight. */
  highlights: boolean;
  /** Only lines inside this chapter's span (from its time to the next chapter's), or null. */
  chapterId: string | null;
  /** Only lines with a word below the low-confidence threshold. */
  uncertain: boolean;
  /** Only lines corrected by hand. */
  edited: boolean;
  /** Only lines matching the transcript search text (ignored while the search is blank). */
  search: boolean;
}

export const NO_FILTER: TranscriptFilter = { speakers: [], highlights: false, chapterId: null, uncertain: false, edited: false, search: false };

/** What a filter is checked against: the recording's annotations and the search text, as they are now. */
export interface FilterContext {
  /** Segment ids with at least one highlight. */
  highlighted: ReadonlySet<string>;
  chapters: readonly Chapter[];
  /** The transcript's low-confidence threshold. */
  threshold: number;
  query: string;
}

/** The segments each highlight belongs to: its own segment id, else the line at its time. */
export function highlightedSegments(highlights: readonly Highlight[], segments: readonly TranscriptSegment[]): Set<string> {
  const ids = new Set(segments.map((s) => s.id));
  const found = new Set<string>();
  for (const h of highlights) {
    const id = h.segmentId ?? segments[segmentIndexAt(segments, h.atMs / 1000)]?.id;
    if (id !== undefined && ids.has(id)) {
      found.add(id);
    }
  }
  return found;
}

/** A chapter's span in seconds: from its time to the next chapter's (or the end), or null for an unknown chapter. */
export function chapterSpan(chapters: readonly Chapter[], chapterId: string | null): { start: number; end: number } | null {
  if (chapterId === null) {
    return null;
  }
  const sorted = [...chapters].sort((a, b) => a.atMs - b.atMs);
  const index = sorted.findIndex((c) => c.id === chapterId);
  const chapter = sorted[index];
  if (chapter === undefined) {
    return null;
  }
  return { start: chapter.atMs / 1000, end: (sorted[index + 1]?.atMs ?? Number.POSITIVE_INFINITY) / 1000 };
}

export function hasUncertainWords(segment: Pick<TranscriptSegment, 'words'>, threshold: number): boolean {
  return segment.words.some((w) => w.c < threshold);
}

/**
 * The parts of the filter that narrow the transcript now: a chapter that no longer exists and the
 * search filter while the search is blank do not count.
 */
export function activeFilter(filter: TranscriptFilter, ctx: Pick<FilterContext, 'chapters' | 'query'>): TranscriptFilter {
  return {
    ...filter,
    chapterId: chapterSpan(ctx.chapters, filter.chapterId) === null ? null : filter.chapterId,
    search: filter.search && ctx.query.trim() !== '',
  };
}

export function isFiltering(filter: TranscriptFilter): boolean {
  return filter.speakers.length > 0 || filter.highlights || filter.chapterId !== null || filter.uncertain || filter.edited || filter.search;
}

/** True when the line passes every active part of the filter. */
export function segmentPasses(segment: TranscriptSegment, filter: TranscriptFilter, ctx: FilterContext): boolean {
  const active = activeFilter(filter, ctx);
  if (active.speakers.length > 0 && (segment.speaker === null || !active.speakers.includes(segment.speaker))) {
    return false;
  }
  if (active.highlights && !ctx.highlighted.has(segment.id)) {
    return false;
  }
  const span = chapterSpan(ctx.chapters, active.chapterId);
  if (span !== null && !(segment.start >= span.start && segment.start < span.end)) {
    return false;
  }
  if (active.uncertain && !hasUncertainWords(segment, ctx.threshold)) {
    return false;
  }
  if (active.edited && segment.edited === null) {
    return false;
  }
  return !active.search || matchesQuery(segment.text, ctx.query);
}

export function filterSegments(segments: readonly TranscriptSegment[], filter: TranscriptFilter, ctx: FilterContext): TranscriptSegment[] {
  return isFiltering(activeFilter(filter, ctx)) ? segments.filter((s) => segmentPasses(s, filter, ctx)) : [...segments];
}

/** How many lines each single choice would show on its own (the counts beside the Filter menu's choices). */
export interface FilterCounts {
  speakers: Map<string, number>;
  highlights: number;
  uncertain: number;
  edited: number;
  search: number;
  chapters: Map<string, number>;
}

export function filterCounts(segments: readonly TranscriptSegment[], ctx: FilterContext): FilterCounts {
  const counts: FilterCounts = { speakers: new Map(), highlights: 0, uncertain: 0, edited: 0, search: 0, chapters: new Map() };
  const spans = ctx.chapters.map((c) => ({ id: c.id, span: chapterSpan(ctx.chapters, c.id) }));
  const searching = ctx.query.trim() !== '';
  for (const s of segments) {
    if (s.speaker !== null) {
      counts.speakers.set(s.speaker, (counts.speakers.get(s.speaker) ?? 0) + 1);
    }
    counts.highlights += ctx.highlighted.has(s.id) ? 1 : 0;
    counts.uncertain += hasUncertainWords(s, ctx.threshold) ? 1 : 0;
    counts.edited += s.edited === null ? 0 : 1;
    counts.search += searching && matchesQuery(s.text, ctx.query) ? 1 : 0;
    const chapter = spans.find(({ span }) => span !== null && s.start >= span.start && s.start < span.end);
    if (chapter !== undefined) {
      counts.chapters.set(chapter.id, (counts.chapters.get(chapter.id) ?? 0) + 1);
    }
  }
  return counts;
}

/** The active parts in words, in a fixed order: speakers, chapter, highlights, uncertain, edited, search. */
export function filterLabels(filter: TranscriptFilter, speakers: readonly Speaker[], ctx: Pick<FilterContext, 'chapters' | 'query'>): string[] {
  const active = activeFilter(filter, ctx);
  const labels: string[] = [];
  const names = active.speakers.map((id) => speakers.find((s) => s.id === id)?.name ?? id);
  if (names.length > 0) {
    labels.push(names.join(' or '));
  }
  const chapter = ctx.chapters.find((c) => c.id === active.chapterId);
  if (chapter !== undefined) {
    labels.push(chapter.title.trim() === '' ? `Chapter at ${formatDuration(chapter.atMs)}` : chapter.title);
  }
  if (active.highlights) {
    labels.push('Highlights');
  }
  if (active.uncertain) {
    labels.push('Uncertain words');
  }
  if (active.edited) {
    labels.push('Edited lines');
  }
  if (active.search) {
    labels.push(`“${ctx.query.trim()}”`);
  }
  return labels;
}

/** "Showing 42 of 318 lines · Sarah · Highlights". */
export function showingText(visible: number, total: number, labels: readonly string[]): string {
  return [`Showing ${visible} of ${total} ${total === 1 ? 'line' : 'lines'}`, ...labels].join(' · ');
}

/** A click on a speaker in the People list: that speaker alone, or (again) every speaker; `add` (Ctrl/Shift) toggles one of several. */
export function toggleSpeaker(filter: TranscriptFilter, speakerId: string, add: boolean): TranscriptFilter {
  const selected = filter.speakers.includes(speakerId);
  if (add) {
    return { ...filter, speakers: selected ? filter.speakers.filter((id) => id !== speakerId) : [...filter.speakers, speakerId] };
  }
  return { ...filter, speakers: selected && filter.speakers.length === 1 ? [] : [speakerId] };
}

/** Drops speakers that are no longer in the transcript (merged away), so the filter never hides everything for nothing. */
export function withKnownSpeakers(filter: TranscriptFilter, speakers: readonly Speaker[]): TranscriptFilter {
  const known = filter.speakers.filter((id) => speakers.some((s) => s.id === id));
  return known.length === filter.speakers.length ? filter : { ...filter, speakers: known };
}
