import { describe, expect, it } from 'vitest';
import type { RecordingSummary, StageStatus } from '../bridge/types';
import { cardStageName, CARD_STAGE_NAMES, isInterruptedImport, metaLine, peopleWording, retryRemedy, stageFill, stageName, stagePills, stageStatusText, summaryLine, typeName } from './recording';

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

/** The pills without the stage they stand for, to compare their wording. */
const pills = (stages: StageStatus[]): { kind: string; label: string }[] => stagePills(stages).map(({ kind, label }) => ({ kind, label }));

describe('status pills', () => {
  it('reads "Audio only" (no pills) when nothing has run', () => {
    expect(pills([])).toEqual([]);
  });

  it('offers Import again on an import that stopped, and a plain retry otherwise', () => {
    const stopped: StageStatus = { stage: 'stored', state: 'failed', percent: null, label: 'Import interrupted' };
    const saveFailed: StageStatus = { stage: 'stored', state: 'failed', percent: null, label: 'Saving failed' };
    expect(stagePills([stopped]).map((p) => p.label)).toEqual(['Import interrupted · Import again']);
    expect(stagePills([saveFailed]).map((p) => p.label)).toEqual(['Stored failed · Retry']);
    expect([isInterruptedImport(stopped), isInterruptedImport(saveFailed)]).toEqual([true, false]);
    expect([retryRemedy(stopped), retryRemedy(saveFailed), retryRemedy(undefined)]).toEqual(['importAgain', undefined, undefined]);
  });

  it('hides a finished Stored stage, the normal state of every recording', () => {
    expect(pills([stage('stored', 'done'), stage('transcript', 'done')])).toEqual([{ kind: 'done', label: 'Transcript' }]);
    expect(pills([stage('stored', 'done')])).toEqual([]);
  });

  it('words active, queued and done stages per DESIGN.md §5.4', () => {
    expect(
      pills([stage('stored', 'done'), stage('transcript', 'active', 64), stage('speakers', 'queued'), stage('minutes', 'queued')]),
    ).toEqual([
      { kind: 'active', label: 'Transcribing 64%' },
      { kind: 'queued', label: 'Speakers' },
      { kind: 'queued', label: 'Minutes' },
    ]);
  });

  it('puts a failed stage first and keeps Stored beside it to say the recording is safe', () => {
    expect(pills([stage('stored', 'done'), stage('transcript', 'failed')])).toEqual([
      { kind: 'failed', label: 'Transcript failed · Retry' },
      { kind: 'done', label: 'Stored' },
    ]);
  });

  it('reads a stage waiting for a model as waiting, not failed', () => {
    const waiting: StageStatus = { stage: 'transcript', state: 'failed', percent: null, label: 'Waiting for a model' };
    expect(pills([stage('stored', 'done'), waiting])).toEqual([{ kind: 'queued', label: 'Transcript · needs a model' }]);
  });

  it('shows storing progress while a new recording finalizes', () => {
    expect(pills([stage('stored', 'active', 40)])).toEqual([{ kind: 'active', label: 'Storing 40%' }]);
  });

  it('hides a finished Smaller files stage like Stored', () => {
    expect(pills([stage('stored', 'done'), stage('optimize', 'done')])).toEqual([]);
    expect(pills([stage('transcript', 'done'), stage('optimize', 'done')])).toEqual([{ kind: 'done', label: 'Transcript' }]);
  });

  it('names the optimize stage while it is queued, running or failed', () => {
    expect(pills([stage('optimize', 'queued')])).toEqual([{ kind: 'queued', label: 'Smaller files' }]);
    expect(pills([stage('optimize', 'active', 30)])).toEqual([{ kind: 'active', label: 'Making smaller 30%' }]);
    expect(pills([stage('optimize', 'active')])).toEqual([{ kind: 'active', label: 'Making smaller' }]);
    expect(pills([stage('stored', 'done'), stage('optimize', 'failed')])).toEqual([
      { kind: 'failed', label: 'Smaller files failed · Retry' },
      { kind: 'done', label: 'Stored' },
    ]);
  });

  it('keeps finished Stored and Smaller files beside another failed stage', () => {
    expect(pills([stage('stored', 'done'), stage('transcript', 'failed'), stage('optimize', 'done')])).toEqual([
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
      topics: 'Topics',
      minutes: 'Minutes',
      optimize: 'Smaller files',
    });
  });

  it('names the M2 stages in pipeline order, and any stage a newer host reports', () => {
    expect(['stored', 'transcript', 'speakers', 'topics', 'optimize'].map(cardStageName)).toEqual([
      'Stored',
      'Transcribing',
      'Speakers',
      'Topics',
      'Smaller files',
    ]);
    expect(cardStageName('summaries')).toBe('Summaries');
    expect(stageName('speakers')).toBe('Speakers');
    // The stages move through the pills as processing.progress reports them.
    const order: [string, StageStatus[]][] = [
      ['transcript', [stage('stored', 'done'), stage('transcript', 'active', 10, '10% · local GPU'), stage('speakers', 'queued'), stage('optimize', 'queued')]],
      ['speakers', [stage('stored', 'done'), stage('transcript', 'done'), stage('speakers', 'active', 50, '50% · CPU'), stage('optimize', 'queued')]],
      ['optimize', [stage('stored', 'done'), stage('transcript', 'done'), stage('speakers', 'done'), stage('optimize', 'active', 20)]],
    ];
    expect(order.map(([, stages]) => pills(stages).map((p) => `${p.kind}:${p.label}`).join(', '))).toEqual([
      'active:Transcribing 10%, queued:Speakers, queued:Smaller files',
      'done:Transcript, active:Speakers 50%, queued:Smaller files',
      'done:Transcript, done:Speakers, active:Making smaller 20%',
    ]);
    expect(stagePills([stage('transcript', 'failed')])[0]).toEqual({ kind: 'failed', label: 'Transcript failed · Retry', stage: 'transcript' });
  });

  it('reads a stage waiting for a busy PC as paused, not running', () => {
    expect(pills([stage('transcript', 'active', 40, 'Paused · PC is busy')])).toEqual([{ kind: 'queued', label: 'Transcript paused' }]);
    expect(stageStatusText(stage('transcript', 'active', 40, 'Paused · PC is busy'))).toBe('Paused · PC is busy');
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
