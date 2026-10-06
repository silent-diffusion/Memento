// Level meters and track lanes on the Recording session (DESIGN.md §5.10, §5.11). recording.levels
// reports linear RMS and peak in 0..1 up to 30 times a second. The meter shows loudness on a
// -60..0 dBFS scale, as people read levels; the lanes draw amplitude, as waveforms do.

const FLOOR_DB = -60;

/** 0..1 on a decibel scale: -60 dBFS (or silence) is 0, full scale is 1. */
export function loudness(linear: number): number {
  if (!Number.isFinite(linear) || linear <= 0) {
    return 0;
  }
  const db = 20 * Math.log10(Math.min(1, linear));
  return Math.min(1, Math.max(0, (db - FLOOR_DB) / -FLOOR_DB));
}

/** Width of a source's level meter fill, as a whole percentage. */
export function meterPercent(rms: number): number {
  return Math.round(loudness(rms) * 100);
}

/** Lane bars sit in a 36 px track: 4 px for near-silence up to 26 px at full scale (the render's range). */
export const LANE_BAR_MIN_PX = 4;
export const LANE_BAR_MAX_PX = 26;

/**
 * Lane bars draw amplitude like a waveform rather than loudness like the meter: the square root of
 * the linear level, so speech shows its rise and fall instead of sitting near the top.
 */
export function laneBarHeight(level: number): number {
  const l = Number.isFinite(level) ? Math.sqrt(Math.min(1, Math.max(0, level))) : 0;
  return Math.round(LANE_BAR_MIN_PX + l * (LANE_BAR_MAX_PX - LANE_BAR_MIN_PX));
}

/**
 * Loudest level per time bucket for one lane, covering the whole recording so far. Buckets start at
 * `baseBucketMs`; whenever there are more than four per bar they merge pairwise and double, so memory
 * stays bounded however long the recording runs.
 */
export class LaneHistory {
  readonly barCount: number;
  private bucketMs: number;
  private buckets: (number | null)[] = [];

  constructor(barCount = 72, baseBucketMs = 100) {
    this.barCount = barCount;
    this.bucketMs = baseBucketMs;
  }

  get resolutionMs(): number {
    return this.bucketMs;
  }

  add(elapsedMs: number, peak: number): void {
    if (!Number.isFinite(elapsedMs) || elapsedMs < 0) {
      return;
    }
    let index = Math.floor(elapsedMs / this.bucketMs);
    while (index >= this.barCount * 4) {
      this.merge();
      index = Math.floor(elapsedMs / this.bucketMs);
    }
    while (this.buckets.length <= index) {
      this.buckets.push(null);
    }
    const value = Math.max(0, Math.min(1, peak));
    this.buckets[index] = Math.max(this.buckets[index] ?? 0, value);
  }

  private merge(): void {
    const merged: (number | null)[] = [];
    for (let i = 0; i < this.buckets.length; i += 2) {
      const a = this.buckets[i] ?? null;
      const b = this.buckets[i + 1] ?? null;
      merged.push(a === null ? b : b === null ? a : Math.max(a, b));
    }
    this.buckets = merged;
    this.bucketMs *= 2;
  }

  /**
   * One value per bar across 0..`nowMs`: the loudest level in that span, or null where nothing was
   * recorded (before the source started, while it was off or lost).
   */
  bars(nowMs: number): (number | null)[] {
    const out: (number | null)[] = [];
    const span = Math.max(nowMs, 1);
    for (let i = 0; i < this.barCount; i++) {
      // Each bucket belongs to the bar its start falls in; a bar narrower than a bucket borrows the
      // bucket it sits inside.
      const barStart = (i * span) / this.barCount;
      const barEnd = ((i + 1) * span) / this.barCount;
      let from = Math.ceil(barStart / this.bucketMs);
      let to = Math.ceil(barEnd / this.bucketMs) - 1;
      if (to < from) {
        from = Math.floor(barStart / this.bucketMs);
        to = from;
      }
      let best: number | null = null;
      for (let b = from; b <= to && b < this.buckets.length; b++) {
        const v = this.buckets[b] ?? null;
        if (v !== null) {
          best = best === null ? v : Math.max(best, v);
        }
      }
      out.push(best);
    }
    return out;
  }
}
