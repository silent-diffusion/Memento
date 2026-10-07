import { describe, expect, it } from 'vitest';
import { computeWindow, KEEP_REACH, RowHeights, rowAt } from './virtualList';

describe('transcript list windowing', () => {
  it('lays rows out from the estimate, then from the measured average', () => {
    const heights = new RowHeights(4, 80);
    expect([...heights.offsets()]).toEqual([0, 80, 160, 240, 320]);
    expect(heights.set(1, 120)).toBe(true);
    expect(heights.set(1, 120)).toBe(false);
    // An element that is not laid out (0 px) does not count as measured.
    expect(heights.set(2, 0)).toBe(false);
    // Unmeasured rows now count as the average measured height.
    expect([...heights.offsets()]).toEqual([0, 120, 240, 360, 480]);
    expect(heights.total()).toBe(480);
    expect(heights.typical()).toBe(120);
  });

  it('finds the row under a position', () => {
    const offsets = new RowHeights(3, 100).offsets();
    expect(rowAt(offsets, 3, 0)).toBe(0);
    expect(rowAt(offsets, 3, 99.9)).toBe(0);
    expect(rowAt(offsets, 3, 100)).toBe(1);
    expect(rowAt(offsets, 3, 1000)).toBe(3);
  });

  it('renders only the rows near the viewport of 10,000', () => {
    const heights = new RowHeights(10_000, 80);
    const window = computeWindow(heights, 400_000, 400_800, 6);
    expect(window.start).toBe(5000 - 6);
    expect(window.end).toBe(5010 + 6 + 1);
    expect(window.end - window.start).toBeLessThan(40);
    expect(window.padTop).toBe(window.start * 80);
    expect(window.padTop + (window.end - window.start) * 80 + window.padBottom).toBe(heights.total());
  });

  it('clamps at both ends', () => {
    const heights = new RowHeights(20, 50);
    expect(computeWindow(heights, -500, 100, 2)).toEqual({ start: 0, end: 5, padTop: 0, padBottom: 750 });
    const end = computeWindow(heights, 5000, 6000, 2);
    expect(end.end).toBe(20);
    expect(end.padBottom).toBe(0);
    expect(computeWindow(new RowHeights(0, 50), 0, 100)).toEqual({ start: 0, end: 0, padTop: 0, padBottom: 0 });
  });

  it('keeps a nearby row (the one being edited) rendered, but lets a far one go', () => {
    const heights = new RowHeights(1000, 50);
    const near = computeWindow(heights, 10_000, 10_500, 2, [190]);
    expect(near.start).toBe(190);
    const far = computeWindow(heights, 10_000, 10_500, 2, [200 - KEEP_REACH - 50]);
    expect(far.start).toBe(198);
  });

  it('keeps measurements of remaining rows when the count changes', () => {
    const heights = new RowHeights(3, 10);
    heights.set(0, 30);
    heights.set(2, 30);
    heights.resize(2);
    expect(heights.total()).toBe(60);
    heights.resize(3);
    expect(heights.total()).toBe(90);
  });
});
