// Review's video follows the mix (DESIGN.md §18.3, ENGINE-NOTES.md §M.4): the mix <audio> is the one
// playhead, and each video track is positioned from it. These pure functions say which part of a track
// covers a moment on the recording timeline, where inside that file to be, and whether the <video>
// has drifted far enough to seek; useVideoSync.ts applies them to the elements.
import type { ProjectVideo, ProjectVideoTrack, StageStatus, VideoPart, VideoRole } from '../../bridge/types';
import { formatTimecode } from '../../format/duration';

/** While both play: a <video> this far from the mix is put back by seeking; closer, it plays on by itself. */
export const PLAYING_DRIFT_MS = 120;
/** While the <video> is still seeking (fragmented MP4 seeks take 30–700 ms, §M.3) only a jump this large seeks again. */
export const SEEKING_DRIFT_MS = 1000;
/** Paused: the picture shows the exact frame, so anything beyond rounding seeks. */
export const PAUSED_DRIFT_MS = 5;
/** A seek never lands on the very end of a file, where some engines show nothing. */
const END_GUARD_MS = 1;
/** The most a seek made while playing aims ahead of the mix to cover its own latency. */
export const MAX_SEEK_LEAD_MS = 750;

/** Where a track is at one moment of the recording timeline. */
export type TrackPosition =
  /** A part with a readable file covers the moment: play it from `localMs` (the file starts at 0, §M.3). */
  | { kind: 'play'; partIndex: number; url: string; localMs: number; /** The last moment inside the file a seek may aim at. */ lastMs: number }
  /** A part covers the moment but its file cannot be read yet (being repaired or joined by the video stage). */
  | { kind: 'preparing'; partIndex: number }
  /** Before the track began (a source turned on mid-recording). */
  | { kind: 'before'; startsAtMs: number }
  /** After the track ended (a window closed, a source turned off). */
  | { kind: 'after'; endedAtMs: number }
  /** Between two parts. */
  | { kind: 'gap'; resumesAtMs: number };

type Timed = Pick<ProjectVideoTrack, 'startOffsetMs' | 'durationMs'> & { parts: readonly VideoPart[]; endReason?: string | null };

/** A track that ends this close to the recording's end ended with it (frame and audio lengths differ slightly). */
export const ENDS_WITH_RECORDING_MS = 1000;

/**
 * Which part covers `timeMs` (recording timeline, ms) and where inside its file. With `recordingMs`, a
 * track that ended with the recording (no end reason) holds its last frame to the end instead of
 * saying it ended.
 */
export function trackPosition(track: Timed, timeMs: number, recordingMs?: number): TrackPosition {
  const raw = Number.isFinite(timeMs) ? timeMs : 0;
  const end = track.startOffsetMs + track.durationMs;
  const endsWithRecording = recordingMs !== undefined && (track.endReason ?? null) === null && end >= recordingMs - ENDS_WITH_RECORDING_MS;
  const t = endsWithRecording && raw >= end && track.durationMs > 0 ? end - END_GUARD_MS : raw;
  if (t < track.startOffsetMs) {
    return { kind: 'before', startsAtMs: track.startOffsetMs };
  }
  if (t >= end) {
    return { kind: 'after', endedAtMs: end };
  }
  let next: number | null = null;
  for (let i = 0; i < track.parts.length; i++) {
    const part = track.parts[i];
    if (part === undefined) {
      continue;
    }
    if (t >= part.startOffsetMs && t < part.startOffsetMs + part.durationMs) {
      if (part.url === null) {
        return { kind: 'preparing', partIndex: i };
      }
      const lastMs = Math.max(0, part.durationMs - END_GUARD_MS);
      const localMs = Math.min(Math.max(0, t - part.startOffsetMs), lastMs);
      return { kind: 'play', partIndex: i, url: part.url, localMs, lastMs };
    }
    if (part.startOffsetMs > t && (next === null || part.startOffsetMs < next)) {
      next = part.startOffsetMs;
    }
  }
  return next === null ? { kind: 'after', endedAtMs: end } : { kind: 'gap', resumesAtMs: next };
}

/** What the <video> reports about itself. */
export interface VideoElementState {
  currentTimeMs: number;
  paused: boolean;
  seeking: boolean;
  playbackRate: number;
}

/** What the mix is doing. */
export interface MixState {
  playing: boolean;
  rate: number;
}

/** What to do to the <video>: null leaves that aspect alone. */
export interface SyncAction {
  /** Set currentTime to this (ms inside the file). */
  seekToMs: number | null;
  /** true: play(), false: pause(). */
  play: boolean | null;
  /** Set playbackRate to this. */
  rate: number | null;
}

/** How far the <video> may be from `localMs` before it is sought, given what it and the mix are doing. */
export function driftTolerance(mix: MixState, video: Pick<VideoElementState, 'seeking'>): number {
  if (video.seeking) {
    return SEEKING_DRIFT_MS;
  }
  return mix.playing ? PLAYING_DRIFT_MS : PAUSED_DRIFT_MS;
}

/**
 * One step of slaving a <video> to the mix: it plays at the mix's rate while the mix plays, pauses
 * when the mix pauses, and seeks only when it is further from `localMs` than the tolerance (no second
 * clock: the video never moves the playhead). `seekLatencyMs` is how long the last seek took: a seek
 * made while playing aims that far ahead (at the mix's rate, at most MAX_SEEK_LEAD_MS) so that it
 * lands where the mix will be instead of chasing it with one seek after another.
 */
export function syncAction(localMs: number, mix: MixState, video: VideoElementState, seekLatencyMs = 0, lastMs = Number.POSITIVE_INFINITY): SyncAction {
  const drift = Math.abs(video.currentTimeMs - localMs);
  const lead = mix.playing ? Math.min(MAX_SEEK_LEAD_MS, Math.max(0, seekLatencyMs) * (mix.rate > 0 ? mix.rate : 1)) : 0;
  const seekToMs = drift > driftTolerance(mix, video) ? Math.min(localMs + lead, Math.max(localMs, lastMs)) : null;
  const rate = Number.isFinite(mix.rate) && mix.rate > 0 && video.playbackRate !== mix.rate ? mix.rate : null;
  const play = mix.playing && video.paused ? true : !mix.playing && !video.paused ? false : null;
  return { seekToMs, play, rate };
}

/** The tracks the well draws: the screen fills it with the camera as the inset, or the camera alone fills it. */
export function videoLayers(video: Pick<ProjectVideo, 'tracks'>): { main: ProjectVideoTrack | null; inset: ProjectVideoTrack | null } {
  const screen = video.tracks.find((t) => t.id === 'screen') ?? null;
  const camera = video.tracks.find((t) => t.id === 'camera') ?? null;
  return screen === null ? { main: camera, inset: null } : { main: screen, inset: camera };
}

function roleName(role: VideoRole): string {
  return role === 'screen' ? 'Screen recording' : 'Camera recording';
}

/**
 * The calm still state the well shows instead of a frozen frame, or null while the track plays: before
 * it starts, after it ended (its end reason when it has one), between parts, and while the file is
 * being prepared (with the video stage's own label while there is one).
 */
export function stillMessage(track: Pick<ProjectVideoTrack, 'id' | 'endReason'>, position: TrackPosition, videoStage: StageStatus | null): { lead: string; detail: string | null } | null {
  switch (position.kind) {
    case 'play':
      return null;
    case 'before':
      return { lead: `${roleName(track.id)} starts at ${formatTimecode(position.startsAtMs)}.`, detail: 'Audio and the transcript play as usual until then.' };
    case 'after':
      return { lead: track.endReason ?? `${roleName(track.id)} ended at ${formatTimecode(position.endedAtMs)}.`, detail: 'Audio and the transcript continue.' };
    case 'gap':
      return { lead: `${roleName(track.id)} resumes at ${formatTimecode(position.resumesAtMs)}.`, detail: 'Audio and the transcript continue.' };
    case 'preparing':
      return { lead: 'The video is being prepared.', detail: videoStage === null || videoStage.state === 'done' ? null : videoStage.label };
  }
}

/** The recording's video stage while it is listed and not done, for the "being prepared" line. */
export function activeVideoStage(stages: readonly StageStatus[]): StageStatus | null {
  return stages.find((s) => s.stage === 'video' && s.state !== 'done') ?? null;
}
