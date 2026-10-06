import { describe, expect, it } from 'vitest';
import {
  activeChapterIndex,
  chapterInsertIndex,
  historyTone,
  parsePeaks,
  rateLabel,
  resamplePeaks,
  scrubKeyTarget,
  waveBarHeight,
} from './player';

const chapters = [{ atMs: 0 }, { atMs: 390_000 }, { atMs: 1_090_000 }, { atMs: 2_040_000 }];

describe('chapters at the playhead', () => {
  it('inserts after every chapter at or before the playhead', () => {
    expect(chapterInsertIndex(chapters, 0)).toBe(1);
    expect(chapterInsertIndex(chapters, 200_000)).toBe(1);
    expect(chapterInsertIndex(chapters, 390_000)).toBe(2);
    expect(chapterInsertIndex(chapters, 1_500_000)).toBe(3);
    expect(chapterInsertIndex(chapters, 9_000_000)).toBe(4);
    expect(chapterInsertIndex([], 5_000)).toBe(0);
    expect(chapterInsertIndex([{ atMs: 10_000 }], 5_000)).toBe(0);
  });

  it('finds the chapter the playhead is in', () => {
    expect(activeChapterIndex(chapters, 0)).toBe(0);
    expect(activeChapterIndex(chapters, 1_200_000)).toBe(2);
    expect(activeChapterIndex([{ atMs: 10_000 }], 5_000)).toBe(-1);
  });
});

describe('scrubber keys', () => {
  const total = 4_202_000;
  it('steps 5 s, or 30 s with Shift', () => {
    expect(scrubKeyTarget('ArrowRight', false, 60_000, total)).toBe(65_000);
    expect(scrubKeyTarget('ArrowLeft', false, 60_000, total)).toBe(55_000);
    expect(scrubKeyTarget('ArrowRight', true, 60_000, total)).toBe(90_000);
    expect(scrubKeyTarget('ArrowLeft', true, 60_000, total)).toBe(30_000);
    expect(scrubKeyTarget('ArrowUp', false, 60_000, total)).toBe(65_000);
    expect(scrubKeyTarget('PageDown', false, 60_000, total)).toBe(30_000);
  });

  it('clamps to the recording and jumps with Home and End', () => {
    expect(scrubKeyTarget('ArrowLeft', true, 10_000, total)).toBe(0);
    expect(scrubKeyTarget('ArrowRight', true, total - 1_000, total)).toBe(total);
    expect(scrubKeyTarget('Home', false, 60_000, total)).toBe(0);
    expect(scrubKeyTarget('End', false, 60_000, total)).toBe(total);
    expect(scrubKeyTarget('Enter', false, 60_000, total)).toBeNull();
  });
});

describe('waveform from peaks.json', () => {
  it('reads the proposed schema and a bare array', () => {
    expect(parsePeaks({ schemaVersion: 1, bucketMs: 100, durationMs: 300, peaks: [0.1, 0.5, 2] })).toEqual([0.1, 0.5, 1]);
    expect(parsePeaks([0.2, -0.4])).toEqual([0.2, 0.4]);
    expect(parsePeaks({ peaks: [] })).toBeNull();
    expect(parsePeaks('nope')).toBeNull();
    expect(parsePeaks(null)).toBeNull();
  });

  it('resamples to the bar count, loudest per bar', () => {
    expect(resamplePeaks([0.1, 0.9, 0.2, 0.3], 2)).toEqual([0.9, 0.3]);
    expect(resamplePeaks([0.5], 3)).toEqual([0.5, 0.5, 0.5]);
    expect(resamplePeaks([], 2)).toEqual([0, 0]);
    expect(waveBarHeight(0)).toBe(6);
    expect(waveBarHeight(1)).toBe(56);
  });
});

describe('history dots', () => {
  it('uses ok for completed, accent for failed and text-3 for the rest', () => {
    expect(historyTone({ event: 'completed' })).toBe('ok');
    expect(historyTone({ event: 'failed' })).toBe('failed');
    expect(historyTone({ event: 'info' })).toBe('info');
    expect(historyTone({ event: 'started' })).toBe('info');
    expect(historyTone({ event: 'progress' })).toBe('info');
  });
});

describe('speed labels', () => {
  it('reads like the render', () => {
    expect(rateLabel(1)).toBe('1.0×');
    expect(rateLabel(0.75)).toBe('0.75×');
    expect(rateLabel(2)).toBe('2.0×');
    expect(rateLabel(1.5)).toBe('1.5×');
  });
});
