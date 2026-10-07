import { describe, expect, it } from 'vitest';
import type { RecordingSummary } from '../bridge/types';
import {
  filterChips,
  groupRecordings,
  INITIAL_LIBRARY_VIEW,
  isNarrowed,
  libraryViewReducer,
  listParams,
  type LibraryViewState,
} from './libraryView';

const rec = (id: string, createdAt: Date, extra: Partial<RecordingSummary> = {}): RecordingSummary => ({
  id,
  title: id,
  type: 'meeting',
  createdAt: createdAt.toISOString(),
  durationMs: 60_000,
  participantCount: 2,
  hasVideo: false,
  stages: [],
  people: [],
  isProcessing: false,
  state: 'ready',
  sizeBytes: 0,
  matchSnippet: null,
  ...extra,
});

describe('libraryViewReducer', () => {
  it('sets search, filter, sort and layout', () => {
    let state = INITIAL_LIBRARY_VIEW;
    state = libraryViewReducer(state, { type: 'search', query: 'priya' });
    state = libraryViewReducer(state, { type: 'filter', recordingType: 'interview' });
    state = libraryViewReducer(state, { type: 'sort', sort: 'longest' });
    state = libraryViewReducer(state, { type: 'layout', layout: 'grid' });
    expect(state).toEqual({ ...INITIAL_LIBRARY_VIEW, query: 'priya', type: 'interview', sort: 'longest', layout: 'grid' });
  });

  it('returns the same object when nothing changes, so no refetch happens', () => {
    expect(libraryViewReducer(INITIAL_LIBRARY_VIEW, { type: 'filter', recordingType: 'all' })).toBe(INITIAL_LIBRARY_VIEW);
    expect(libraryViewReducer(INITIAL_LIBRARY_VIEW, { type: 'sort', sort: 'newest' })).toBe(INITIAL_LIBRARY_VIEW);
    expect(libraryViewReducer(INITIAL_LIBRARY_VIEW, { type: 'search', query: '' })).toBe(INITIAL_LIBRARY_VIEW);
  });

  it('clear resets search and filter but keeps sort, layout and position', () => {
    const busy: LibraryViewState = { query: 'x', type: 'lecture', sort: 'title', layout: 'grid', scrollY: 400, selectedId: 'a' };
    expect(libraryViewReducer(busy, { type: 'clear' })).toEqual({ ...busy, query: '', type: 'all' });
  });

  it('never stores a negative scroll offset', () => {
    expect(libraryViewReducer(INITIAL_LIBRARY_VIEW, { type: 'scroll', y: -20 }).scrollY).toBe(0);
  });

  it('knows when the list is narrowed (the processing card hides then)', () => {
    expect(isNarrowed(INITIAL_LIBRARY_VIEW)).toBe(false);
    expect(isNarrowed({ query: '  ', type: 'all' })).toBe(false);
    expect(isNarrowed({ query: 'a', type: 'all' })).toBe(true);
    expect(isNarrowed({ query: '', type: 'meeting' })).toBe(true);
  });

  it('turns the view into library.list parameters, leaving out what is not narrowed', () => {
    expect(listParams(INITIAL_LIBRARY_VIEW)).toEqual({ sort: 'newest' });
    expect(listParams({ ...INITIAL_LIBRARY_VIEW, query: '  priya ', type: 'interview', sort: 'oldest' })).toEqual({
      sort: 'oldest',
      query: 'priya',
      type: 'interview',
    });
  });
});

describe('filterChips', () => {
  it('always lists the built-in types in design order', () => {
    expect(filterChips([]).map((c) => c.label)).toEqual([
      'All',
      'Meetings',
      'Interviews',
      'Lectures',
      'Presentations',
      'Dictation',
      'Research',
    ]);
  });

  it('appends General and custom types found in the library after Research, once each', () => {
    const now = new Date();
    const chips = filterChips([
      rec('a', now, { type: 'Book notes' }),
      rec('b', now, { type: 'general' }),
      rec('c', now, { type: 'Book notes' }),
      rec('d', now, { type: 'Band practice' }),
    ]);
    expect(chips.slice(7)).toEqual([
      { value: 'general', label: 'General' },
      { value: 'Band practice', label: 'Band practice' },
      { value: 'Book notes', label: 'Book notes' },
    ]);
  });
});

describe('groupRecordings', () => {
  const now = new Date(2026, 9, 6, 15, 0);
  const sample = [
    rec('today', new Date(2026, 9, 6, 10, 0)),
    rec('yesterday', new Date(2026, 9, 5, 9, 0)),
    rec('week', new Date(2026, 9, 2, 9, 0)),
    rec('sept-a', new Date(2026, 8, 29, 9, 0)),
    rec('sept-b', new Date(2026, 8, 3, 9, 0)),
    rec('aug', new Date(2026, 7, 14, 9, 0)),
  ];

  it('buckets newest-first results in order and omits empty buckets', () => {
    const groups = groupRecordings(sample, 'newest', now);
    expect(groups.map((g) => [g.label, g.items.map((i) => i.id)])).toEqual([
      ['Today', ['today']],
      ['Yesterday', ['yesterday']],
      ['Earlier this week', ['week']],
      ['September', ['sept-a', 'sept-b']],
      ['August', ['aug']],
    ]);
    expect(groupRecordings(sample.slice(3), 'newest', now).map((g) => g.label)).toEqual(['September', 'August']);
  });

  it('reverses the buckets for oldest first', () => {
    const groups = groupRecordings([...sample].reverse(), 'oldest', now);
    expect(groups.map((g) => g.label)).toEqual(['August', 'September', 'Earlier this week', 'Yesterday', 'Today']);
  });

  it('reads longest and title as one ranked list in the host order', () => {
    expect(groupRecordings(sample, 'longest', now)).toEqual([{ key: 'longest', label: 'Longest first', items: sample }]);
    expect(groupRecordings(sample, 'title', now)[0]?.label).toBe('A to Z');
  });

  it('has no groups for no recordings', () => {
    expect(groupRecordings([], 'newest', now)).toEqual([]);
  });
});
