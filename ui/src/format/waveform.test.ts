import { describe, expect, it } from 'vitest';
import { seedFromId, waveformBars } from './waveform';

describe('grid waveform strip', () => {
  it('draws 48 bars between 4 and 60 px', () => {
    const bars = waveformBars('20261006-100000-q3plan');
    expect(bars).toHaveLength(48);
    expect(Math.min(...bars)).toBeGreaterThanOrEqual(4);
    expect(Math.max(...bars)).toBeLessThanOrEqual(60);
  });

  it('is the same for the same id and differs between ids', () => {
    expect(waveformBars('a')).toEqual(waveformBars('a'));
    expect(waveformBars('a')).not.toEqual(waveformBars('b'));
    expect(seedFromId('a')).toBeLessThan(233_280);
  });
});
