// "about 190 hours at this quality" in the Recording session footer (DESIGN.md §8).
//
// Formula: hours = freeBytes / (Σ over tracks of sampleRate × 3 bytes × channels) / 3600, rounded
// down. While recording, every track streams to disk as int24 PCM WAV (ARCHITECTURE.md §3, step 3),
// so that is the rate the drive actually fills at; FLAC encoding at finalize only makes the files
// smaller afterwards. Before a session starts, each selected source counts as 48 kHz, mono for a
// microphone and stereo for system and application audio, which is what the capture layer opens.
import type { AudioSource } from '../bridge/types';

export interface TrackFormat {
  sampleRate: number;
  channels: number;
}

/** int24 PCM while recording. */
export const RECORDING_BYTES_PER_SAMPLE = 3;

export const DEFAULT_SAMPLE_RATE = 48_000;

export function sourceFormat(source: Pick<AudioSource, 'kind'>): TrackFormat {
  return { sampleRate: DEFAULT_SAMPLE_RATE, channels: source.kind === 'microphone' ? 1 : 2 };
}

export function recordingBytesPerSecond(tracks: readonly TrackFormat[]): number {
  return tracks.reduce((sum, t) => sum + t.sampleRate * RECORDING_BYTES_PER_SAMPLE * Math.max(1, t.channels), 0);
}

/** Whole hours of recording that fit in `freeBytes`, or null when nothing would be recorded. */
export function hoursAtThisQuality(freeBytes: number, tracks: readonly TrackFormat[]): number | null {
  const rate = recordingBytesPerSecond(tracks);
  if (rate <= 0 || !Number.isFinite(freeBytes) || freeBytes < 0) {
    return null;
  }
  return Math.floor(freeBytes / rate / 3600);
}

/** "about 190 hours at this quality", "about 1 hour…", "under an hour…". */
export function estimateWording(freeBytes: number, tracks: readonly TrackFormat[]): string | null {
  const hours = hoursAtThisQuality(freeBytes, tracks);
  if (hours === null) {
    return null;
  }
  if (hours < 1) {
    return 'under an hour at this quality';
  }
  return `about ${hours} ${hours === 1 ? 'hour' : 'hours'} at this quality`;
}
