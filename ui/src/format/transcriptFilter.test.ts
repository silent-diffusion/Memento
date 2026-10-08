import { describe, expect, it } from 'vitest';
import type { Chapter, Highlight, Speaker, TranscriptSegment } from '../bridge/types';
import {
  activeFilter,
  chapterSpan,
  filterCounts,
  filterLabels,
  filterSegments,
  highlightedSegments,
  isFiltering,
  NO_FILTER,
  showingText,
  toggleSpeaker,
  withKnownSpeakers,
  type FilterContext,
  type TranscriptFilter,
} from './transcriptFilter';

const seg = (id: string, start: number, speaker: string | null, text: string, extra: Partial<TranscriptSegment> = {}): TranscriptSegment => ({
  id,
  start,
  end: start + 2,
  track: 'mic',
  speaker,
  speakerConfidence: speaker === null ? null : 0.9,
  text,
  confidence: 0.9,
  words: text.split(' ').map((w, i) => ({ w, s: start + i * 0.2, e: start + i * 0.2 + 0.2, c: 0.9 })),
  edited: null,
  ...extra,
});

const SEGMENTS: TranscriptSegment[] = [
  seg('s1', 0, 'spk1', 'Welcome to the budget review'),
  seg('s2', 5, 'spk2', 'Thanks for having me', { words: [{ w: 'Thanks', s: 5, e: 5.3, c: 0.3 }] }),
  seg('s3', 12, 'spk1', 'The budget is approved', { edited: { at: '2026-10-06T10:00:00+01:00', original: 'The budge is approved' } }),
  seg('s4', 70, 'spk3', 'Next item is hiring'),
  seg('s5', 75, null, 'Unattributed note about the budget'),
];
const SPEAKERS: Speaker[] = [
  { id: 'spk1', name: 'Sarah', renamed: true, color: 1, talkTimeMs: 1 },
  { id: 'spk2', name: 'Omar', renamed: true, color: 2, talkTimeMs: 1 },
  { id: 'spk3', name: 'Speaker 3', renamed: false, color: 3, talkTimeMs: 1 },
];
const CHAPTERS: Chapter[] = [
  { id: 'c2', atMs: 60_000, title: 'Hiring', origin: 'user' },
  { id: 'c1', atMs: 0, title: 'Budget', origin: 'user' },
];
const HIGHLIGHTS: Highlight[] = [
  { id: 'h1', atMs: 12_500, note: 'Approved', origin: 'user', segmentId: null },
  { id: 'h2', atMs: 70_000, note: '', origin: 'user', segmentId: 's4' },
];
const ctx = (query = ''): FilterContext => ({ highlighted: highlightedSegments(HIGHLIGHTS, SEGMENTS), chapters: CHAPTERS, threshold: 0.5, query });
const ids = (filter: Partial<TranscriptFilter>, query = ''): string[] => filterSegments(SEGMENTS, { ...NO_FILTER, ...filter }, ctx(query)).map((s) => s.id);

describe('transcript filters (DESIGN.md §9, after 1.2.0)', () => {
  it('shows every line without a filter', () => {
    expect(ids({})).toEqual(['s1', 's2', 's3', 's4', 's5']);
    expect(isFiltering(NO_FILTER)).toBe(false);
  });

  it('filters by one speaker or several (either of them)', () => {
    expect(ids({ speakers: ['spk1'] })).toEqual(['s1', 's3']);
    expect(ids({ speakers: ['spk1', 'spk3'] })).toEqual(['s1', 's3', 's4']);
  });

  it('filters to highlighted lines, by segment id or by the highlight’s time', () => {
    expect([...highlightedSegments(HIGHLIGHTS, SEGMENTS)].sort()).toEqual(['s3', 's4']);
    expect(ids({ highlights: true })).toEqual(['s3', 's4']);
  });

  it('filters to a chapter’s span, from its time to the next chapter’s', () => {
    expect(chapterSpan(CHAPTERS, 'c1')).toEqual({ start: 0, end: 60 });
    expect(chapterSpan(CHAPTERS, 'c2')).toEqual({ start: 60, end: Number.POSITIVE_INFINITY });
    expect(ids({ chapterId: 'c1' })).toEqual(['s1', 's2', 's3']);
    expect(ids({ chapterId: 'c2' })).toEqual(['s4', 's5']);
  });

  it('filters to lines with uncertain words, to edited lines, and to search matches', () => {
    expect(ids({ uncertain: true })).toEqual(['s2']);
    expect(ids({ edited: true })).toEqual(['s3']);
    expect(ids({ search: true }, 'budget')).toEqual(['s1', 's3', 's5']);
  });

  it('combines kinds: every active part must hold', () => {
    expect(ids({ speakers: ['spk1'], highlights: true })).toEqual(['s3']);
    expect(ids({ chapterId: 'c1', search: true }, 'budget')).toEqual(['s1', 's3']);
    expect(ids({ speakers: ['spk2'], edited: true })).toEqual([]);
  });

  it('ignores the search filter while the search is blank, and a chapter that was removed', () => {
    expect(ids({ search: true }, '  ')).toHaveLength(5);
    expect(ids({ chapterId: 'gone' })).toHaveLength(5);
    expect(isFiltering(activeFilter({ ...NO_FILTER, search: true, chapterId: 'gone' }, ctx()))).toBe(false);
  });

  it('counts what each single choice would show', () => {
    const counts = filterCounts(SEGMENTS, ctx('budget'));
    expect(Object.fromEntries(counts.speakers)).toEqual({ spk1: 2, spk2: 1, spk3: 1 });
    expect([counts.highlights, counts.uncertain, counts.edited, counts.search]).toEqual([2, 1, 1, 3]);
    expect(Object.fromEntries(counts.chapters)).toEqual({ c1: 3, c2: 2 });
  });

  it('reads “Showing 42 of 318 lines · Sarah · Highlights”', () => {
    const labels = filterLabels({ ...NO_FILTER, speakers: ['spk1'], highlights: true }, SPEAKERS, ctx());
    expect(showingText(1, 318, labels)).toBe('Showing 1 of 318 lines · Sarah · Highlights');
    expect(filterLabels({ ...NO_FILTER, speakers: ['spk1', 'spk2'], chapterId: 'c2', uncertain: true, edited: true, search: true }, SPEAKERS, ctx(' budget '))).toEqual([
      'Sarah or Omar',
      'Hiring',
      'Uncertain words',
      'Edited lines',
      '“budget”',
    ]);
    expect(showingText(0, 1, [])).toBe('Showing 0 of 1 line');
  });

  it('a speaker click shows that speaker alone, again shows everyone; Ctrl adds or removes one', () => {
    const sarah = toggleSpeaker(NO_FILTER, 'spk1', false);
    expect(sarah.speakers).toEqual(['spk1']);
    expect(toggleSpeaker(sarah, 'spk2', false).speakers).toEqual(['spk2']);
    expect(toggleSpeaker(sarah, 'spk1', false).speakers).toEqual([]);
    const both = toggleSpeaker(sarah, 'spk2', true);
    expect(both.speakers).toEqual(['spk1', 'spk2']);
    expect(toggleSpeaker(both, 'spk1', true).speakers).toEqual(['spk2']);
    expect(toggleSpeaker(both, 'spk1', false).speakers).toEqual(['spk1']);
  });

  it('forgets a speaker that was merged away', () => {
    const filter = { ...NO_FILTER, speakers: ['spk1', 'spk9'] };
    expect(withKnownSpeakers(filter, SPEAKERS).speakers).toEqual(['spk1']);
    const kept = { ...NO_FILTER, speakers: ['spk1'] };
    expect(withKnownSpeakers(kept, SPEAKERS)).toBe(kept);
  });
});
