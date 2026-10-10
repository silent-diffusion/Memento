// The Recording session footer (DESIGN.md §8, §17): what is being saved on the left, free space and
// the time left at this quality on the right.
import type { FooterStatusPayload } from '../bridge/types';
import { estimateWording, recordingBytesPerSecond, type TrackFormat } from './estimate';
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

/** 2.0 video (DESIGN.md §18.1, §18.6): what the session's video adds to both sides of the footer. */
export interface VideoFooter {
  /** videoFooterLine: "video and audio in sync", "Video paused · Display 2 disconnected · audio recording", … */
  line: { tone: 'ok' | 'accent'; text: string } | null;
  /** What the video tracks write per second (their targets), on top of the audio. */
  bytesPerSecond: number;
  /** "(screen 30 fps + camera)". */
  note: string | null;
}

/** "about 9 hours at this quality (screen 30 fps + camera)", at the audio and video rates together. */
export function videoEstimateWording(freeBytes: number, audioBytesPerSecond: number, video: VideoFooter): string | null {
  const rate = audioBytesPerSecond + video.bytesPerSecond;
  if (!(rate > 0) || !Number.isFinite(freeBytes) || freeBytes < 0) {
    return null;
  }
  const hours = Math.floor(freeBytes / rate / 3600);
  const base = hours < 1 ? 'under an hour at this quality' : `about ${hours} ${hours === 1 ? 'hour' : 'hours'} at this quality`;
  return video.note === null ? base : `${base} ${video.note}`;
}

/**
 * The footer with video: a problem (accent) replaces the saving line, a quiet "video and audio in
 * sync" is appended to it; the right side estimates at video rates and, when space is low, says how
 * much video still fits ("Low disk space · about 25 min of video").
 */
export function withVideo(
  phase: RecordPhase,
  left: EngineLine,
  status: FooterStatusPayload | null,
  formats: readonly TrackFormat[],
  video: VideoFooter | null,
  right: StorageLine,
): { left: EngineLine; right: StorageLine } {
  if (video === null || video.bytesPerSecond <= 0) {
    return { left, right };
  }
  let nextLeft = left;
  if (phase === 'recording' && video.line !== null && left.strong === undefined && left.tone === 'ok') {
    nextLeft = video.line.tone === 'ok' ? { ...left, text: `${left.text} · ${video.line.text}` } : { tone: 'accent', text: video.line.text };
  }
  const free = status?.storage.freeBytes ?? null;
  if (free === null) {
    return { left: nextLeft, right };
  }
  const audio = recordingBytesPerSecond(formats);
  if (status?.storage.lowSpace === true) {
    const minutes = Math.floor(free / (audio + video.bytesPerSecond) / 60);
    return { left: nextLeft, right: { text: `Low disk space · ${minutes < 1 ? 'under a min' : `about ${minutes} min`} of video`, low: true } };
  }
  const estimate = videoEstimateWording(free, audio, video);
  return { left: nextLeft, right: { text: estimate === null ? right.text : `${formatFreeSpace(free)} free · ${estimate}`, low: false } };
}
