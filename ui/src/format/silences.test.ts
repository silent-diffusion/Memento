import { describe, expect, it } from 'vitest';
import { createSilenceSkipper, gapAt, silenceGaps, skippedBars, skippedCaption, skipTarget, type Silence } from './silences';

const seg = (start: number, end: number): { start: number; end: number } => ({ start, end });
const FREE = { scrubbing: false, sinceManualSeekMs: 60_000 };

describe('silenceGaps', () => {
  it('finds the gaps longer than 1.5 s between one segment and the next', () => {
    expect(silenceGaps([seg(0, 4), seg(5, 9), seg(13, 15), seg(15.5, 20), seg(26, 30)])).toEqual([
      { start: 9, end: 13 },
      { start: 20, end: 26 },
    ]);
  });

  it('does not skip a gap of exactly 1.5 s, nor before the first or after the last segment', () => {
    expect(silenceGaps([seg(10, 12), seg(13.5, 14)])).toEqual([]);
    expect(silenceGaps([seg(10, 12), seg(13.51, 14)])).toEqual([{ start: 12, end: 13.51 }]);
    expect(silenceGaps([])).toEqual([]);
    expect(silenceGaps([seg(30, 31)])).toEqual([]);
  });

  it('counts overlapping speech until the later end, and sorts segments by start', () => {
    // 0-10 overlaps 2-4: the gap is from 10, not from 4.
    expect(silenceGaps([seg(12, 14), seg(0, 10), seg(2, 4)])).toEqual([{ start: 10, end: 12 }]);
  });
});

describe('skip decision', () => {
  const gaps: Silence[] = [
    { start: 9, end: 13 },
    { start: 20, end: 26 },
  ];

  it('jumps from inside a gap to the next segment start minus 0.2 s', () => {
    expect(skipTarget(9, gaps, FREE)).toBe(12.8);
    expect(skipTarget(11.2, gaps, FREE)).toBe(12.8);
    expect(skipTarget(23, gaps, FREE)).toBeCloseTo(25.8);
  });

  it('plays on during speech and once it has landed', () => {
    expect(skipTarget(8.99, gaps, FREE)).toBeNull();
    expect(skipTarget(12.8, gaps, FREE)).toBeNull();
    expect(skipTarget(12.78, gaps, FREE)).toBeNull();
    expect(skipTarget(13.5, gaps, FREE)).toBeNull();
    expect(skipTarget(40, gaps, FREE)).toBeNull();
    expect(skipTarget(11, [], FREE)).toBeNull();
  });

  it('never skips while scrubbing or within 1 s after a manual seek', () => {
    expect(skipTarget(11, gaps, { scrubbing: true, sinceManualSeekMs: 60_000 })).toBeNull();
    expect(skipTarget(11, gaps, { scrubbing: false, sinceManualSeekMs: 0 })).toBeNull();
    expect(skipTarget(11, gaps, { scrubbing: false, sinceManualSeekMs: 999 })).toBeNull();
    expect(skipTarget(11, gaps, { scrubbing: false, sinceManualSeekMs: 1000 })).toBe(12.8);
  });

  it('finds the gap by binary search over many gaps', () => {
    const many = Array.from({ length: 1000 }, (_, i) => ({ start: i * 10 + 5, end: i * 10 + 8 }));
    expect(gapAt(many, 4235.5)).toEqual({ start: 4235, end: 4238 });
    expect(gapAt(many, 4234)).toBeNull();
    expect(gapAt(many, 9999)).toBeNull();
  });
});

describe('the player skipper', () => {
  it('guards a manual seek for 1 s and the scrubber while it is held, then skips', () => {
    let now = 0;
    const skipper = createSilenceSkipper(() => now);
    const gaps: Silence[] = [{ start: 9, end: 13 }];
    expect(skipper.check(10, gaps)).toBe(12.8);

    skipper.noteManualSeek();
    now = 500;
    expect(skipper.check(10, gaps)).toBeNull();
    now = 1000;
    expect(skipper.check(10, gaps)).toBe(12.8);

    skipper.setScrubbing(true);
    now = 5000;
    expect(skipper.check(10, gaps)).toBeNull();
    // Letting go of the scrubber is a manual seek too.
    skipper.setScrubbing(false);
    now = 5400;
    expect(skipper.check(10, gaps)).toBeNull();
    now = 6000;
    expect(skipper.check(10, gaps)).toBe(12.8);
  });
});

describe('waveform and caption', () => {
  it('dims the bars whose middle lies in skipped time', () => {
    // 10 bars over 20 s: bar i covers 2i..2i+2 s, middle 2i+1.
    expect(skippedBars(10, 20_000, [{ start: 4, end: 9 }])).toEqual([false, false, true, true, false, false, false, false, false, false]);
    expect(skippedBars(4, 20_000, [])).toEqual([false, false, false, false]);
    expect(skippedBars(0, 20_000, [{ start: 4, end: 9 }])).toEqual([]);
  });

  it('says how much was skipped in whole seconds', () => {
    expect(skippedCaption(4.2)).toBe('Skipped 4 s');
    expect(skippedCaption(1.3)).toBe('Skipped 1 s');
    expect(skippedCaption(0.4)).toBe('Skipped 1 s');
  });
});
