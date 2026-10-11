import { describe, expect, it } from 'vitest';
import { formatGap, resumedSummary, segmentMarkers } from './segments';

describe('resume markers', () => {
  it('words the gap between Stop and Resume', () => {
    expect(formatGap(0)).toBe('paused 0 s');
    expect(formatGap(45_000)).toBe('paused 45 s');
    expect(formatGap(12 * 60_000 + 20_000)).toBe('paused 12 min');
    expect(formatGap(60 * 60_000)).toBe('paused 1 h');
    expect(formatGap(65 * 60_000)).toBe('paused 1 h 5 min');
  });

  it('places one marker per resumed segment on the timeline', () => {
    const markers = segmentMarkers(
      [
        { index: 1, startedAt: '2026-10-11T09:00:00+02:00', offsetMs: 0, durationMs: 60_000, gapMs: 0 },
        { index: 2, startedAt: '2026-10-11T10:42:00+02:00', offsetMs: 60_000, durationMs: 60_000, gapMs: 12 * 60_000 },
      ],
      120_000,
    );
    expect(markers).toHaveLength(1);
    expect(markers[0]?.fraction).toBe(0.5);
    expect(markers[0]?.atMs).toBe(60_000);
    expect(markers[0]?.label).toMatch(/^Resumed at \d{1,2}:42 (AM|PM)$/);
    expect(markers[0]?.gap).toBe('paused 12 min');
    expect(segmentMarkers(undefined, 1000)).toEqual([]);
    expect(segmentMarkers([{ index: 2, startedAt: '2026-10-11T10:42:00+02:00', offsetMs: 10, durationMs: 1, gapMs: 1 }], 0)).toEqual([]);
  });

  it('sums the resumes for a Library row', () => {
    expect(resumedSummary(undefined)).toBeNull();
    expect(resumedSummary([])).toBeNull();
    expect(resumedSummary(['2026-10-11T10:42:00+02:00'])).toMatch(/^Resumed at /);
    expect(resumedSummary(['2026-10-11T10:42:00+02:00', '2026-10-11T16:05:00+02:00'])).toMatch(/^Resumed 2 times · last at /);
  });
});
