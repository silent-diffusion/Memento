// 2.1 "Resume recording": the words and positions of the markers where a recording was resumed
// (DESIGN.md §9: Review's waveform and timeline; §4: the Library row).
import type { RecordingSegment } from '../bridge/types';
import { formatClock } from './when';

/** The real time between Stop and Resume: "paused 45 s", "paused 12 min", "paused 1 h 5 min". */
export function formatGap(ms: number): string {
  const seconds = Math.max(0, Math.round(ms / 1000));
  if (seconds < 60) {
    return `paused ${seconds} s`;
  }
  const minutes = Math.round(seconds / 60);
  if (minutes < 60) {
    return `paused ${minutes} min`;
  }
  const hours = Math.floor(minutes / 60);
  const rest = minutes % 60;
  return rest === 0 ? `paused ${hours} h` : `paused ${hours} h ${rest} min`;
}

/** "Resumed at 10:42 AM". */
export function resumedAtLabel(startedAt: string): string {
  return `Resumed at ${formatClock(new Date(startedAt))}`;
}

/** The Library row's marker: "Resumed at 10:42 AM", or "Resumed 3 times · last at 4:05 PM". */
export function resumedSummary(resumedAt: readonly string[] | undefined): string | null {
  if (resumedAt === undefined || resumedAt.length === 0) {
    return null;
  }
  const last = resumedAt[resumedAt.length - 1] ?? '';
  return resumedAt.length === 1 ? resumedAtLabel(last) : `Resumed ${resumedAt.length} times · last at ${formatClock(new Date(last))}`;
}

export interface SegmentMarker {
  index: number;
  /** Where the segment starts, 0–1 of the recording. */
  fraction: number;
  atMs: number;
  /** "Resumed at 10:42 AM" */
  label: string;
  /** "paused 12 min" */
  gap: string;
}

/** One marker per resumed segment (every segment after the first), placed on a recording of `durationMs`. */
export function segmentMarkers(segments: readonly RecordingSegment[] | undefined, durationMs: number): SegmentMarker[] {
  if (segments === undefined || durationMs <= 0) {
    return [];
  }
  return segments
    .filter((s) => s.index > 1 && s.offsetMs >= 0 && s.offsetMs <= durationMs)
    .map((s) => ({
      index: s.index,
      fraction: Math.min(1, s.offsetMs / durationMs),
      atMs: s.offsetMs,
      label: resumedAtLabel(s.startedAt),
      gap: formatGap(s.gapMs),
    }));
}
