// Skip silences in the Review player: the gaps between transcript segments, whether the playhead
// should jump over the one it is in, and which waveform bars stand for skipped time. All times are
// in seconds, as in the transcript, except where a name says Ms.
import type { TranscriptSegment } from '../bridge/types';

/** A gap longer than this between one segment's end and the next one's start is skipped. */
export const SILENCE_MIN_S = 1.5;
/** Playback lands this long before the next segment starts, so its first word is not clipped. */
export const SKIP_LEAD_S = 0.2;
/** No skip this soon after the person seeks: they chose to be where they are. */
export const MANUAL_SEEK_GUARD_MS = 1000;
/** How long the "Skipped 4 s" caption stays in the player strip. */
export const SKIPPED_CAPTION_MS = 1600;

export interface Silence {
  /** Where the speech before it ends. */
  start: number;
  /** Where the next segment starts. */
  end: number;
}

/**
 * The silences in a transcript: every gap from the end of the speech so far to the start of the next
 * segment that is longer than `minSeconds`. Overlapping segments (two people at once) count as speech
 * until the later of their ends. Before the first segment and after the last nothing is skipped.
 */
export function silenceGaps(segments: readonly Pick<TranscriptSegment, 'start' | 'end'>[], minSeconds = SILENCE_MIN_S): Silence[] {
  const sorted = [...segments].sort((a, b) => a.start - b.start);
  const first = sorted[0];
  if (first === undefined) {
    return [];
  }
  const gaps: Silence[] = [];
  let speechEnd = first.end;
  for (const segment of sorted.slice(1)) {
    if (segment.start - speechEnd > minSeconds) {
      gaps.push({ start: speechEnd, end: segment.start });
    }
    speechEnd = Math.max(speechEnd, segment.end);
  }
  return gaps;
}

/** Where playback goes from inside `gap`. */
export function landingOf(gap: Silence): number {
  return gap.end - SKIP_LEAD_S;
}

/** Positions this close to the landing already count as landed (seeking is not sample-exact). */
const LANDED_S = 0.05;

/** The gap the playhead is in and still before its landing, by binary search, or null. */
export function gapAt(gaps: readonly Silence[], positionS: number): Silence | null {
  let lo = 0;
  let hi = gaps.length - 1;
  while (lo <= hi) {
    const mid = (lo + hi) >> 1;
    const gap = gaps[mid];
    if (gap === undefined) {
      return null;
    }
    if (positionS < gap.start) {
      hi = mid - 1;
    } else if (positionS >= landingOf(gap) - LANDED_S) {
      lo = mid + 1;
    } else {
      return gap;
    }
  }
  return null;
}

export interface SkipGuard {
  /** The scrubber is being dragged. */
  scrubbing: boolean;
  /** Milliseconds since the person last sought (button, scrubber, chapter, line, search). */
  sinceManualSeekMs: number;
}

/**
 * The skip decision: the position to jump to (the next segment's start minus 0.2 s) when the
 * playhead is inside a skipped gap, or null. Never while scrubbing or within 1 s of a manual seek.
 */
export function skipTarget(positionS: number, gaps: readonly Silence[], guard: SkipGuard): number | null {
  if (guard.scrubbing || guard.sinceManualSeekMs < MANUAL_SEEK_GUARD_MS) {
    return null;
  }
  const gap = gapAt(gaps, positionS);
  return gap === null ? null : landingOf(gap);
}

/**
 * The player's side of skipping: remembers manual seeks and scrubbing, and answers, for each
 * time update while playing, where to jump if anywhere.
 */
export interface SilenceSkipper {
  noteManualSeek: () => void;
  setScrubbing: (scrubbing: boolean) => void;
  /** Null: play on. Otherwise the position to seek to. */
  check: (positionS: number, gaps: readonly Silence[]) => number | null;
}

export function createSilenceSkipper(now: () => number = () => performance.now()): SilenceSkipper {
  let lastManualSeek = Number.NEGATIVE_INFINITY;
  let scrubbing = false;
  return {
    noteManualSeek: () => {
      lastManualSeek = now();
    },
    setScrubbing: (on) => {
      scrubbing = on;
      if (!on) {
        lastManualSeek = now();
      }
    },
    check: (positionS, gaps) => skipTarget(positionS, gaps, { scrubbing, sinceManualSeekMs: now() - lastManualSeek }),
  };
}

/** For each of `count` waveform bars over `durationMs`: whether its middle lies in skipped time. */
export function skippedBars(count: number, durationMs: number, gaps: readonly Silence[]): boolean[] {
  if (count <= 0) {
    return [];
  }
  if (durationMs <= 0 || gaps.length === 0) {
    return Array.from({ length: count }, () => false);
  }
  const barSeconds = durationMs / 1000 / count;
  return Array.from({ length: count }, (_, i) => gapAt(gaps, (i + 0.5) * barSeconds) !== null);
}

/** "Skipped 4 s": the time jumped over, in whole seconds (at least 1). */
export function skippedCaption(seconds: number): string {
  return `Skipped ${Math.max(1, Math.round(seconds))} s`;
}
