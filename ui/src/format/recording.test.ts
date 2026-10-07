import { describe, expect, it } from 'vitest';
import type { RecordingSummary, StageStatus } from '../bridge/types';
import { CARD_STAGE_NAMES, metaLine, peopleWording, stageFill, stagePills, stageStatusText, summaryLine, typeName } from './recording';

const stage = (s: StageStatus['stage'], state: StageStatus['state'], percent: number | null = null, label: string | null = null): StageStatus => ({
  stage: s,
  state,
  percent,
  label,
});

describe('people wording', () => {
  it.each([
    [0, 'Just me'],
    [-1, 'Just me'],
    [1, '1 speaker'],
    [2, '2 people'],
    [5, '5 people'],
    [12, '12 people'],
  ])('%i reads %s', (count, expected) => {
    expect(peopleWording(count)).toBe(expected);
  });
});

describe('summary line', () => {
  it('counts and totals what is shown', () => {
    expect(summaryLine(10, (8 * 3600 + 38 * 60) * 1000)).toBe('10 recordings · 8 h 38 min · all on this PC');
  });

  it('uses the singular for one and still says where it is', () => {
    expect(summaryLine(1, 401_000)).toBe('1 recording · 7 min · all on this PC');
  });

  it('reads zero when a filter matches nothing', () => {
    expect(summaryLine(0, 0)).toBe('0 recordings · 0 min · all on this PC');
  });
});

describe('type names', () => {
  it('names built-in types and keeps custom names as they are', () => {
    expect(typeName('meeting')).toBe('Meeting');
    expect(typeName('general')).toBe('General');
    expect(typeName('Book notes')).toBe('Book notes');
  });
});

describe('meta line', () => {
  it('reads type, people and when', () => {
    const recording: RecordingSummary = {
      id: 'a',
      title: 'Q3 planning sync',
      type: 'meeting',
      createdAt: new Date(2026, 9, 6, 10, 0).toISOString(),
      durationMs: 1000,
      participantCount: 5,
      hasVideo: false,
      stages: [],
      people: [],
      isProcessing: false,
      state: 'ready',
      sizeBytes: 0,
      matchSnippet: null,
    };
    expect(metaLine(recording, new Date(2026, 9, 6, 15, 0))).toBe('Meeting · 5 people · 10:00 AM');
  });
});

describe('status pills', () => {
  it('reads "Audio only" (no pills) when nothing has run', () => {
    expect(stagePills([])).toEqual([]);
  });

  it('hides a finished Stored stage, the normal state of every recording', () => {
    expect(stagePills([stage('stored', 'done'), stage('transcript', 'done')])).toEqual([{ kind: 'done', label: 'Transcript' }]);
    expect(stagePills([stage('stored', 'done')])).toEqual([]);
  });

  it('words active, queued and done stages per DESIGN.md §5.4', () => {
    expect(
      stagePills([stage('stored', 'done'), stage('transcript', 'active', 64), stage('speakers', 'queued'), stage('minutes', 'queued')]),
    ).toEqual([
      { kind: 'active', label: 'Transcribing 64%' },
      { kind: 'queued', label: 'Speakers' },
      { kind: 'queued', label: 'Minutes' },
    ]);
  });

  it('puts a failed stage first and keeps Stored beside it to say the recording is safe', () => {
    expect(stagePills([stage('stored', 'done'), stage('transcript', 'failed')])).toEqual([
      { kind: 'failed', label: 'Transcript failed · Retry' },
      { kind: 'done', label: 'Stored' },
    ]);
  });

  it('shows storing progress while a new recording finalizes', () => {
    expect(stagePills([stage('stored', 'active', 40)])).toEqual([{ kind: 'active', label: 'Storing 40%' }]);
  });

  it('hides a finished Smaller files stage like Stored', () => {
    expect(stagePills([stage('stored', 'done'), stage('optimize', 'done')])).toEqual([]);
    expect(stagePills([stage('transcript', 'done'), stage('optimize', 'done')])).toEqual([{ kind: 'done', label: 'Transcript' }]);
  });

  it('names the optimize stage while it is queued, running or failed', () => {
    expect(stagePills([stage('optimize', 'queued')])).toEqual([{ kind: 'queued', label: 'Smaller files' }]);
    expect(stagePills([stage('optimize', 'active', 30)])).toEqual([{ kind: 'active', label: 'Making smaller 30%' }]);
    expect(stagePills([stage('optimize', 'active')])).toEqual([{ kind: 'active', label: 'Making smaller' }]);
    expect(stagePills([stage('stored', 'done'), stage('optimize', 'failed')])).toEqual([
      { kind: 'failed', label: 'Smaller files failed · Retry' },
      { kind: 'done', label: 'Stored' },
    ]);
  });

  it('keeps finished Stored and Smaller files beside another failed stage', () => {
    expect(stagePills([stage('stored', 'done'), stage('transcript', 'failed'), stage('optimize', 'done')])).toEqual([
      { kind: 'failed', label: 'Transcript failed · Retry' },
      { kind: 'done', label: 'Stored' },
      { kind: 'done', label: 'Smaller files' },
    ]);
  });
});

describe('processing card columns', () => {
  it('names every stage column', () => {
    expect(CARD_STAGE_NAMES).toEqual({
      stored: 'Stored',
      transcript: 'Transcribing',
      speakers: 'Speakers',
      minutes: 'Minutes',
      optimize: 'Smaller files',
    });
  });

  it('prefers the host label and falls back to plain words', () => {
    expect(stageStatusText(stage('transcript', 'active', 64, '64% · local GPU'))).toBe('64% · local GPU');
    expect(stageStatusText(stage('transcript', 'active', 64))).toBe('64%');
    expect(stageStatusText(stage('speakers', 'queued'))).toBe('Queued');
    expect(stageStatusText(stage('stored', 'done'))).toBe('Done');
  });

  it('fills done to 100%, active to its percentage and queued not at all', () => {
    expect(stageFill(stage('stored', 'done'))).toBe('100%');
    expect(stageFill(stage('transcript', 'active', 64))).toBe('64%');
    expect(stageFill(stage('transcript', 'active', 140))).toBe('100%');
    expect(stageFill(stage('speakers', 'queued'))).toBe('0%');
  });
});
