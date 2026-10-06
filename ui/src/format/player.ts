// Review player helpers (DESIGN.md §5.11, §5.13, §9): chapter placement at the playhead, the active
// chapter, scrubber keys, the waveform from peaks.json and the History dots.
import type { Chapter, HistoryEntry } from '../bridge/types';

/** Where a chapter added at `atMs` goes: after every chapter at or before that time. */
export function chapterInsertIndex(chapters: readonly Pick<Chapter, 'atMs'>[], atMs: number): number {
  let index = 0;
  while (index < chapters.length && (chapters[index]?.atMs ?? 0) <= atMs) {
    index += 1;
  }
  return index;
}

/** The chapter the playhead is in (the last one starting at or before it), or -1 before the first. */
export function activeChapterIndex(chapters: readonly Pick<Chapter, 'atMs'>[], playheadMs: number): number {
  return chapterInsertIndex(chapters, playheadMs) - 1;
}

export const SCRUB_STEP_MS = 5_000;
export const SCRUB_BIG_STEP_MS = 30_000;

/**
 * The scrubber's keyboard (DESIGN.md §5.11): Left/Right 5 s, with Shift 30 s; PageUp/PageDown 30 s;
 * Home and End jump to the ends. Returns the new position, clamped, or null for any other key.
 */
export function scrubKeyTarget(key: string, shift: boolean, currentMs: number, durationMs: number): number | null {
  const step = shift ? SCRUB_BIG_STEP_MS : SCRUB_STEP_MS;
  let target: number;
  switch (key) {
    case 'ArrowLeft':
    case 'ArrowDown':
      target = currentMs - step;
      break;
    case 'ArrowRight':
    case 'ArrowUp':
      target = currentMs + step;
      break;
    case 'PageDown':
      target = currentMs - SCRUB_BIG_STEP_MS;
      break;
    case 'PageUp':
      target = currentMs + SCRUB_BIG_STEP_MS;
      break;
    case 'Home':
      target = 0;
      break;
    case 'End':
      target = durationMs;
      break;
    default:
      return null;
  }
  return Math.max(0, Math.min(Math.max(0, durationMs), target));
}

/** Peaks from peaks.json: `{ peaks: number[] }` (proposed schema) or a bare array; null if unreadable. */
export function parsePeaks(json: unknown): number[] | null {
  const raw = Array.isArray(json)
    ? (json as unknown[])
    : typeof json === 'object' && json !== null && Array.isArray((json as { peaks?: unknown }).peaks)
      ? ((json as { peaks: unknown[] }).peaks)
      : null;
  if (raw === null || raw.length === 0) {
    return null;
  }
  const peaks = raw.map((v) => (typeof v === 'number' && Number.isFinite(v) ? Math.min(1, Math.abs(v)) : 0));
  return peaks;
}

/** `count` bars, each the loudest peak in its share of the recording. */
export function resamplePeaks(peaks: readonly number[], count: number): number[] {
  const out: number[] = [];
  if (peaks.length === 0) {
    return Array.from({ length: count }, () => 0);
  }
  for (let i = 0; i < count; i++) {
    const from = Math.floor((i * peaks.length) / count);
    const to = Math.max(from + 1, Math.floor(((i + 1) * peaks.length) / count));
    let best = 0;
    for (let j = from; j < to && j < peaks.length; j++) {
      best = Math.max(best, peaks[j] ?? 0);
    }
    out.push(best);
  }
  return out;
}

/** Bar height in the 56 px player waveform: 6 px floor up to 56 px (the render's range). */
export function waveBarHeight(peak: number): number {
  const p = Math.max(0, Math.min(1, peak));
  return Math.round(6 + Math.sqrt(p) * 50);
}

export type HistoryTone = 'ok' | 'info' | 'failed';

/** History dots (DESIGN.md §9): ok for completed stages, accent for failed, text-3 for the rest. */
export function historyTone(entry: Pick<HistoryEntry, 'event'>): HistoryTone {
  switch (entry.event) {
    case 'completed':
      return 'ok';
    case 'failed':
      return 'failed';
    case 'started':
    case 'progress':
    case 'info':
      return 'info';
  }
}

/** Playback speeds offered by the speed menu. */
export const PLAYBACK_RATES = [0.75, 1, 1.25, 1.5, 1.75, 2] as const;

export function rateLabel(rate: number): string {
  return `${Number.isInteger(rate) ? rate.toFixed(1) : String(rate)}×`;
}
