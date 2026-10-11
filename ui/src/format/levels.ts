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

// ---------------------------------------------------------------------------------------------
// The quiet-source hint (after 2.0.0-beta.1, ENGINE-NOTES.md §Q, DESIGN.md §17): a source whose
// speech over the last 10 s stays under -30 dBFS, or 20 dB or more under another source's, gets a
// line under its meter. Judged from recording.levels' peaks, per source, against its own floor.
// ---------------------------------------------------------------------------------------------

/** How much of each source's level is looked at. */
export const QUIET_WINDOW_MS = 10_000;
/** Speech that peaks under this is quiet, whatever the other sources do. */
export const QUIET_BELOW_DBFS = -30;
/** Or this far under another source that is speaking. */
export const QUIET_UNDER_OTHERS_DB = 20;
/** Speech is present when the loud moments are this far over the source's own floor. */
const SPEAKING_OVER_FLOOR_DB = 10;
/** Once shown, the hint stays until the speech peaks reach this (so it does not flicker at the edge). */
const NOT_QUIET_FROM_DBFS = -26;
/** Fewer readings than this in the window say nothing yet (levels arrive up to 30 times a second). */
const MIN_READINGS = 50;

function dbfs(linear: number): number {
  return linear > 0 && Number.isFinite(linear) ? 20 * Math.log10(Math.min(1, linear)) : -120;
}

function percentile(sorted: readonly number[], p: number): number {
  return sorted[Math.min(sorted.length - 1, Math.floor(sorted.length * p))] ?? -120;
}

export interface SourceSpeech {
  /** The loud moments' peak in dBFS over the window (its 95th-percentile reading). */
  levelDb: number;
  /** Whether the source rose well above its own floor in the window: someone spoke. */
  speaking: boolean;
}

/** The last 10 s of one source's peak readings. */
export class SpeechWindow {
  private readings: { at: number; db: number }[] = [];

  add(atMs: number, peak: number): void {
    this.readings.push({ at: atMs, db: dbfs(peak) });
    const from = atMs - QUIET_WINDOW_MS;
    let drop = 0;
    while (drop < this.readings.length && (this.readings[drop]?.at ?? 0) < from) {
      drop++;
    }
    if (drop > 0) {
      this.readings = this.readings.slice(drop);
    }
  }

  speech(): SourceSpeech | null {
    if (this.readings.length < MIN_READINGS) {
      return null;
    }
    const sorted = this.readings.map((r) => r.db).sort((a, b) => a - b);
    const floor = percentile(sorted, 0.1);
    const levelDb = percentile(sorted, 0.95);
    return { levelDb, speaking: levelDb > -100 && levelDb - floor >= SPEAKING_OVER_FLOOR_DB };
  }
}

/**
 * Which sources are quiet now, with their level, from each source's last 10 s and which were quiet
 * before: a speaking source under -30 dBFS or 20 dB under another speaking source becomes quiet; it
 * stops being quiet once its speech reaches -26 dBFS and is within 16 dB of the others. A source that
 * is silent keeps what it was, so the hint neither appears nor goes while nobody talks into it.
 */
export function quietSources(windows: ReadonlyMap<string, SpeechWindow>, before: ReadonlyMap<string, number>): Map<string, number> {
  const speech = new Map<string, SourceSpeech>();
  for (const [id, window] of windows) {
    const s = window.speech();
    if (s !== null) {
      speech.set(id, s);
    }
  }
  const quiet = new Map<string, number>();
  for (const [id, s] of speech) {
    const others = [...speech].filter(([other, o]) => other !== id && o.speaking).map(([, o]) => o.levelDb);
    const loudestOther = Math.max(-120, ...others);
    const wasQuiet = before.has(id);
    if (!s.speaking) {
      if (wasQuiet) {
        quiet.set(id, before.get(id) ?? s.levelDb);
      }
      continue;
    }
    const under = loudestOther - s.levelDb;
    const isQuiet = wasQuiet
      ? s.levelDb < NOT_QUIET_FROM_DBFS || under >= QUIET_UNDER_OTHERS_DB - 4
      : s.levelDb < QUIET_BELOW_DBFS || under >= QUIET_UNDER_OTHERS_DB;
    if (isQuiet) {
      quiet.set(id, s.levelDb);
    }
  }
  return quiet;
}

/** The §17 line under a quiet source's meter: "Microphone is quiet (−38 dB). Move closer or raise its level in Windows; the transcript may miss you." */
export function quietHint(kind: string, name: string, levelDb: number): string {
  const rounded = Math.round(levelDb);
  const level = `${rounded < 0 ? '−' : ''}${Math.abs(rounded)} dB`;
  return kind === 'microphone'
    ? `${name} is quiet (${level}). Move closer or raise its level in Windows; the transcript may miss you.`
    : `${name} is quiet (${level}). Raise its volume in Windows or in the app; the transcript may miss it.`;
}
