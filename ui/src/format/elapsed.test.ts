import { describe, expect, it } from 'vitest';
import { createElapsedClock, MAX_LEAD_MS } from './elapsed';

describe('recording timer interpolation', () => {
  it('shows zero before the first recording.state', () => {
    expect(createElapsedClock().read(5_000)).toBe(0);
  });

  it('moves on with the page clock between host events', () => {
    const clock = createElapsedClock();
    clock.observe('s1', 10_000, true, 1_000);
    expect(clock.read(1_000)).toBe(10_000);
    expect(clock.read(1_400)).toBe(10_400);
    expect(clock.read(1_900)).toBe(10_900);
  });

  it('never runs more than one second ahead of the host', () => {
    const clock = createElapsedClock();
    clock.observe('s1', 10_000, true, 1_000);
    // The host went quiet (a stall, a slow event): the display stops one second past it.
    expect(clock.read(4_000)).toBe(10_000 + MAX_LEAD_MS);
    expect(clock.read(60_000)).toBe(10_000 + MAX_LEAD_MS);
    for (let t = 1_000; t < 20_000; t += 137) {
      expect(clock.read(t)).toBeLessThanOrEqual(10_000 + MAX_LEAD_MS);
    }
  });

  it('never steps backwards when the host reports a little less than was shown', () => {
    const clock = createElapsedClock();
    clock.observe('s1', 10_000, true, 1_000);
    expect(clock.read(1_900)).toBe(10_900);
    clock.observe('s1', 10_850, true, 1_950);
    expect(clock.read(1_950)).toBe(10_900);
    expect(clock.read(2_300)).toBe(11_200);
  });

  it('freezes while paused and resumes from the host value', () => {
    const clock = createElapsedClock();
    clock.observe('s1', 30_000, true, 0);
    clock.observe('s1', 30_200, false, 200);
    expect(clock.read(200)).toBe(30_200);
    expect(clock.read(9_000)).toBe(30_200);
    clock.observe('s1', 30_200, true, 9_000);
    expect(clock.read(9_500)).toBe(30_700);
  });

  it('starts again from zero for a new session', () => {
    const clock = createElapsedClock();
    clock.observe('s1', 50_000, true, 0);
    expect(clock.read(100)).toBe(50_100);
    clock.observe('s2', 0, true, 200);
    expect(clock.read(200)).toBe(0);
  });
});
