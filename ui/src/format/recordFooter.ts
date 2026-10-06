// The Recording session footer (DESIGN.md §8, §17): what is being saved on the left, free space and
// the time left at this quality on the right.
import type { FooterStatusPayload } from '../bridge/types';
import { estimateWording, type TrackFormat } from './estimate';
import { statusLine, type EngineLine, type StorageLine } from './footer';
import { formatFreeSpace } from './storage';

export type RecordPhase = 'ready' | 'recording' | 'paused' | 'finalizing' | 'stopped';

/** "8 s", "2 min", "1 h 5 min": how long ago the last checkpoint was written. */
export function agoWording(ms: number): string {
  const seconds = Math.max(0, Math.floor(ms / 1000));
  if (seconds < 60) {
    return `${seconds} s`;
  }
  const minutes = Math.floor(seconds / 60);
  if (minutes < 60) {
    return `${minutes} min`;
  }
  const hours = Math.floor(minutes / 60);
  return minutes % 60 === 0 ? `${hours} h` : `${hours} h ${minutes % 60} min`;
}

export function recordStatusLine(
  phase: RecordPhase,
  status: FooterStatusPayload | null,
  lastCheckpointAt: string | null,
  lostAtMs: number | null,
  nowMs: number,
): EngineLine {
  if ((phase === 'recording' || phase === 'paused') && status?.recording.active === true && status.recording.lostSource !== null) {
    // "Zoom lost at 00:41:12 · other tracks recording" in danger (DESIGN.md §17).
    return statusLine(status, lostAtMs);
  }
  switch (phase) {
    case 'ready':
      return { tone: 'ok', text: 'Ready · recordings save to this PC as they happen' };
    case 'recording': {
      const at = lastCheckpointAt === null ? Number.NaN : Date.parse(lastCheckpointAt);
      return {
        tone: 'ok',
        text: Number.isNaN(at) ? 'Saving continuously' : `Saving continuously · last checkpoint ${agoWording(nowMs - at)} ago`,
      };
    }
    case 'paused':
      return { tone: 'accent', text: 'Paused · everything so far is saved on this PC' };
    case 'finalizing':
      return { tone: 'ok', text: 'Finalizing · writing the tracks to this PC' };
    case 'stopped':
      return { tone: 'danger', text: 'Recording stopped · everything up to that point is saved' };
  }
}

/** "212 GB free · about 87 hours at this quality"; the low-space variant leads with the warning. */
export function recordStorageLine(status: FooterStatusPayload | null, tracks: readonly TrackFormat[]): StorageLine {
  const free = status?.storage.freeBytes ?? null;
  if (free === null) {
    return { text: 'Everything is stored on this PC', low: false };
  }
  const estimate = estimateWording(free, tracks);
  const freeText = `${formatFreeSpace(free)} free`;
  const low = status?.storage.lowSpace === true;
  const lead = low ? `Low disk space · ${freeText}` : freeText;
  return { text: estimate === null ? lead : `${lead} · ${estimate}`, low };
}
