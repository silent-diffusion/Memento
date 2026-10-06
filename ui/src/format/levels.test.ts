import { describe, expect, it } from 'vitest';
import { LANE_BAR_MAX_PX, LANE_BAR_MIN_PX, LaneHistory, laneBarHeight, loudness, meterPercent } from './levels';

describe('level mapping', () => {
  it('maps linear level to a -60..0 dBFS scale', () => {
    expect(loudness(0)).toBe(0);
    expect(loudness(-0.2)).toBe(0);
    expect(loudness(Number.NaN)).toBe(0);
    expect(loudness(0.001)).toBeCloseTo(0, 5); // -60 dB
    expect(loudness(0.01)).toBeCloseTo(1 / 3, 5); // -40 dB
    expect(loudness(0.1)).toBeCloseTo(2 / 3, 5); // -20 dB
    expect(loudness(1)).toBe(1);
    expect(loudness(3)).toBe(1);
  });

  it('gives the meter a whole percentage', () => {
    expect(meterPercent(0)).toBe(0);
    expect(meterPercent(0.1)).toBe(67);
    expect(meterPercent(1)).toBe(100);
  });

  it('keeps lane bars inside the 36 px track', () => {
    expect(laneBarHeight(0)).toBe(LANE_BAR_MIN_PX);
    expect(laneBarHeight(1)).toBe(LANE_BAR_MAX_PX);
    const heights = [0.001, 0.01, 0.05, 0.1, 0.3, 0.6, 1].map(laneBarHeight);
    expect([...heights].sort((a, b) => a - b)).toEqual(heights);
  });
});

describe('lane history', () => {
  it('spreads the recording so far across the bars, loudest per span', () => {
    const lane = new LaneHistory(4, 100);
    lane.add(0, 0.2);
    lane.add(50, 0.5);
    lane.add(150, 0.1);
    lane.add(250, 0.9);
    lane.add(350, 0.3);
    expect(lane.bars(400)).toEqual([0.5, 0.1, 0.9, 0.3]);
  });

  it('leaves gaps where nothing was recorded (a lost or muted source)', () => {
    const lane = new LaneHistory(4, 100);
    lane.add(0, 0.4);
    lane.add(350, 0.6);
    expect(lane.bars(400)).toEqual([0.4, null, null, 0.6]);
  });

  it('stays bounded for long recordings by merging buckets', () => {
    const lane = new LaneHistory(10, 100);
    for (let t = 0; t < 3_600_000; t += 50) {
      lane.add(t, t === 1_900_000 ? 1 : 0.1);
    }
    expect(lane.resolutionMs).toBeGreaterThan(100);
    const bars = lane.bars(3_600_000);
    expect(bars).toHaveLength(10);
    expect(bars[5]).toBe(1);
    expect(bars.filter((b) => b === 1)).toHaveLength(1);
  });

  it('clamps levels into 0..1 and ignores negative times', () => {
    const lane = new LaneHistory(2, 100);
    lane.add(-5, 1);
    lane.add(0, 4);
    expect(lane.bars(200)).toEqual([1, null]);
  });
});
