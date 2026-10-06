import { describe, expect, it } from 'vitest';
import { advanceStages, processingOf, queryLibrary } from './mockLibrary';
import { sampleProjects } from './mockData';
import type { RecordingSummary } from './types';

const now = new Date(2026, 9, 6, 15, 0);
const library = (): RecordingSummary[] => sampleProjects(now).map((p) => p.summary);

describe('mock library.list', () => {
  it('lists every sample newest first with totals', () => {
    const result = queryLibrary(library(), {});
    expect(result.totalCount).toBe(14);
    expect(result.recordings).toHaveLength(14);
    const times = result.recordings.map((r) => Date.parse(r.createdAt));
    expect([...times].sort((a, b) => b - a)).toEqual(times);
    expect(result.totalDurationMs).toBe(result.recordings.reduce((sum, r) => sum + r.durationMs, 0));
  });

  it('filters by type and reports the totals of what matches', () => {
    const result = queryLibrary(library(), { type: 'interview' });
    expect(result.recordings.every((r) => r.type === 'interview')).toBe(true);
    expect(result.totalCount).toBe(result.recordings.length);
    expect(result.totalCount).toBe(3);
  });

  it('finds custom types by name, ignoring case', () => {
    expect(queryLibrary(library(), { type: 'book notes' }).totalCount).toBe(1);
  });

  it('searches titles and people, ignoring case', () => {
    expect(queryLibrary(library(), { query: 'TOWN' }).recordings.map((r) => r.title)).toEqual(['Town hall Q&A']);
    const marcus = queryLibrary(library(), { query: 'marcus' }).recordings.map((r) => r.title);
    expect(marcus).toContain('Interview — Marcus Lee, candidate');
    expect(marcus).toContain('Q3 planning sync');
    expect(queryLibrary(library(), { query: 'nobody at all' })).toEqual({ recordings: [], totalDurationMs: 0, totalCount: 0 });
  });

  it('combines search and filter', () => {
    expect(queryLibrary(library(), { query: 'marcus', type: 'interview' }).recordings.map((r) => r.title)).toEqual([
      'Interview — Marcus Lee, candidate',
    ]);
  });

  it('sorts oldest, longest and by title', () => {
    expect(queryLibrary(library(), { sort: 'oldest' }).recordings[0]?.title).toBe('Voice memo: garden plan');
    expect(queryLibrary(library(), { sort: 'longest' }).recordings[0]?.title).toBe('Town hall Q&A');
    const titles = queryLibrary(library(), { sort: 'title' }).recordings.map((r) => r.title);
    expect(titles[0]).toBe('Board prep walkthrough');
    expect(titles.at(-1)).toBe('Weekly 1:1 with Sam');
  });
});

describe('mock processing', () => {
  it('reports the most recent processing recording and how many others are', () => {
    const recordings = library();
    expect(processingOf(recordings)).toMatchObject({ current: { title: 'Q3 planning sync' }, othersCount: 0 });

    const more = recordings.map((r) => (r.title === 'Sprint retrospective' ? { ...r, isProcessing: true } : r));
    expect(processingOf(more).othersCount).toBe(1);
    expect(processingOf(recordings.map((r) => ({ ...r, isProcessing: false })))).toEqual({ current: null, othersCount: 0 });
  });

  it('advances the active stage, then starts the next queued one', () => {
    let stages = advanceStages(
      [
        { stage: 'stored', state: 'done', percent: null, label: 'Done' },
        { stage: 'transcript', state: 'active', percent: 98, label: null },
        { stage: 'speakers', state: 'queued', percent: null, label: 'Queued' },
      ],
      1,
    );
    expect(stages[1]).toEqual({ stage: 'transcript', state: 'active', percent: 99, label: '99% · local GPU' });
    stages = advanceStages(stages, 5);
    expect(stages[1]?.state).toBe('done');
    stages = advanceStages(stages, 5);
    expect(stages[2]).toMatchObject({ state: 'active', percent: 0 });
  });
});
