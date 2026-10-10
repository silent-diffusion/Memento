// What the Recording session says when video fails (DESIGN.md §18.6): video fails first and alone,
// audio keeps recording. Same rules as §17: the thing, the time, what is safe, then the fix. The
// host's own sentence (`message`) is the fallback for a code this page does not know.
import type { RecordingVideoProblemPayload, VideoProblemCode, VideoRole, VideoSource, VideoTrackStatus } from '../bridge/types';
import { formatTimecode } from './duration';
import { formatFreeSpace } from './storage';

/** A §5.19 toast for a problem in the middle of a recording, with its two actions. */
export interface VideoProblemToast {
  title: string;
  body: string;
  /** The ghost action: switch the screen track to another display. */
  capture: VideoSource | null;
  captureLabel: string | null;
  /** The quiet action: "Keep waiting" (nothing) or "Audio only from here" (end the video tracks). */
  quiet: { label: string; endVideo: boolean };
}

/** The codes that are shown as a toast (the track is waiting for a display or ended mid-recording). */
const TOAST_CODES: ReadonlySet<VideoProblemCode> = new Set<VideoProblemCode>(['video.displayDisconnected', 'video.windowClosed', 'video.protectedContent', 'video.sourceNotFound']);

export function isToastProblem(problem: Pick<RecordingVideoProblemPayload, 'code'>): boolean {
  return TOAST_CODES.has(problem.code);
}

/** The display a lost or closed screen track can switch to: the primary one first, never the one that went away. */
export function fallbackDisplay(displays: readonly VideoSource[], lostSourceId: string): VideoSource | null {
  const usable = displays.filter((d) => d.id !== lostSourceId && !d.excluded);
  return usable.find((d) => d.primary) ?? usable[0] ?? null;
}

export function videoProblemToast(problem: RecordingVideoProblemPayload, displays: readonly VideoSource[], cameraRecording: boolean): VideoProblemToast {
  const at = formatTimecode(problem.atMs);
  const other = problem.trackId === 'screen' ? fallbackDisplay(displays, problem.sourceId) : null;
  switch (problem.code) {
    case 'video.displayDisconnected':
      return {
        title: `${problem.sourceName} disconnected at ${at}`,
        body: `Video is paused. ${cameraRecording ? 'Audio and the camera keep' : 'Audio keeps'} recording. ${
          other === null ? 'The screen track continues when the display comes back.' : `Switch the screen track to ${other.name}?`
        }`,
        capture: other,
        captureLabel: other === null ? null : `Switch to ${other.name}`,
        quiet: { label: 'Keep waiting', endVideo: false },
      };
    case 'video.windowClosed':
      return {
        title: `"${problem.sourceName}" was closed at ${at}`,
        body: 'The video track ends there; everything before it is saved. Audio continues.',
        capture: other,
        captureLabel: other === null ? null : `Capture ${other.name} instead`,
        quiet: { label: 'Audio only from here', endVideo: true },
      };
    case 'video.protectedContent':
      return {
        title: `"${problem.sourceName}" hid itself from capture at ${at}`,
        body: 'The video track ends there rather than record a black picture; everything before it is saved. Audio continues.',
        capture: other,
        captureLabel: other === null ? null : `Capture ${other.name} instead`,
        quiet: { label: 'Audio only from here', endVideo: true },
      };
    default:
      return {
        title: `${problem.sourceName} went away at ${at}`,
        body: `The ${problem.trackId} track ends there; everything before it is saved. Audio continues.`,
        capture: other,
        captureLabel: other === null ? null : `Capture ${other.name} instead`,
        quiet: { label: 'Dismiss', endVideo: false },
      };
  }
}

/** An inline card in the VIDEO section (camera in use, privacy, capture blocked, no encoder). */
export interface VideoProblemCard {
  kind: 'cameraInUse' | 'cameraPrivacy' | 'captureBlocked' | 'encoderUnavailable';
  role: VideoRole;
  lead: string;
  rest: string;
}

/**
 * The card for a code, before recording (from a thumbnail that failed) or during it (from
 * recording.videoProblem). `name` is the camera's name; `detail` the other app or the privacy switch.
 */
export function videoProblemCard(code: string, role: VideoRole, name: string, detail: string | null, recording: boolean): VideoProblemCard | null {
  switch (code) {
    case 'video.cameraInUse':
      return {
        kind: 'cameraInUse',
        role,
        lead: detail === null ? `${name} is in use by another app.` : `${name} is in use by ${detail}.`,
        rest: recording
          ? 'Recording continues with the screen and all audio tracks. The camera track will start the moment it becomes free.'
          : 'You can start recording now: the screen and audio record, and the camera track starts the moment it becomes free.',
      };
    case 'video.cameraPrivacy':
      return {
        kind: 'cameraPrivacy',
        role,
        lead: `${name} is turned off in Windows privacy settings.`,
        rest: `Allow it under Privacy & security › Camera${detail === null ? '' : ` ("${detail}")`}, then come back. ${
          recording ? 'Recording continues with the screen and audio.' : 'The screen and audio sources are not affected.'
        }`,
      };
    case 'video.captureBlocked':
      return {
        kind: 'captureBlocked',
        role,
        lead: 'Screen capture is blocked for Memento in Windows privacy settings.',
        rest: 'Allow it under Privacy & security › Screen capture, then come back. Audio sources are not affected.',
      };
    case 'video.encoderUnavailable':
      return {
        kind: 'encoderUnavailable',
        role,
        lead: 'No video encoder could be opened on this PC.',
        rest: recording
          ? 'Audio keeps recording; nothing was lost. On a Windows N edition, add the Media Feature Pack under Settings › Apps › Optional features, then record video again.'
          : 'Audio records as usual. On a Windows N edition, add the Media Feature Pack under Settings › Apps › Optional features, then try again.',
      };
    default:
      return null;
  }
}

/** A row's sub-line while its track waits or after it stopped: "Disconnected at 00:23:40 · waiting". */
export function rowNoteText(problem: Pick<RecordingVideoProblemPayload, 'code' | 'atMs' | 'detail'>): { text: string; tone: 'accent' | 'danger' } {
  const at = formatTimecode(problem.atMs);
  switch (problem.code) {
    case 'video.displayDisconnected':
      return { text: `Disconnected at ${at} · waiting`, tone: 'accent' };
    case 'video.windowMinimised':
      return { text: 'Minimised · resumes when it comes back', tone: 'accent' };
    case 'video.cameraInUse':
      return { text: problem.detail === null ? 'In use by another app · waiting' : `In use by ${problem.detail} · waiting`, tone: 'accent' };
    case 'video.windowClosed':
      return { text: `Closed at ${at}`, tone: 'danger' };
    case 'video.protectedContent':
      return { text: `Hidden from capture at ${at}`, tone: 'danger' };
    case 'video.cameraPrivacy':
      return { text: 'Turned off in Windows privacy settings', tone: 'danger' };
    case 'video.captureBlocked':
      return { text: 'Blocked in Windows privacy settings', tone: 'danger' };
    case 'video.encoderUnavailable':
      return { text: 'No video encoder could be opened', tone: 'danger' };
    case 'video.diskFull':
      return { text: `Stopped at ${at} · drive nearly full`, tone: 'danger' };
    case 'video.encoderFallback':
      return { text: 'Software encoder', tone: 'accent' };
    default:
      return { text: `Stopped at ${at}`, tone: 'danger' };
  }
}

/** Over the live preview while its main track has no picture: "Video paused · Display 2 disconnected". */
export function previewNoticeText(track: Pick<VideoTrackStatus, 'state' | 'sourceName'>, problem: Pick<RecordingVideoProblemPayload, 'code' | 'atMs' | 'detail'> | undefined): string {
  if (track.state === 'waiting') {
    switch (problem?.code) {
      case 'video.displayDisconnected':
        return `Video paused · ${track.sourceName} disconnected`;
      case 'video.windowMinimised':
        return `Video paused · "${track.sourceName}" minimised`;
      case 'video.cameraInUse':
        return problem.detail === null ? `Camera waiting · ${track.sourceName} is in use` : `Camera waiting · in use by ${problem.detail}`;
      default:
        return `Video paused · ${track.sourceName}`;
    }
  }
  return problem === undefined ? `${track.sourceName} · stopped` : `${track.sourceName} · ${rowNoteText(problem).text}`;
}

/** The encoder banner (§18.6): "Hardware encoder failed at 00:12:03." */
export function encoderFallbackBanner(atMs: number): { lead: string; rest: string } {
  return {
    lead: `Hardware encoder failed at ${formatTimecode(atMs)}.`,
    rest: ' Switched to software encoding; no frames were lost. CPU use is higher until this recording ends.',
  };
}

/** Video stopped because the drive filled (video stops first, audio last). */
export function videoDiskFullBanner(atMs: number): { lead: string; rest: string } {
  return {
    lead: `Video stopped at ${formatTimecode(atMs)} because the drive is nearly full.`,
    rest: ' Audio keeps recording; everything recorded so far is saved. Free up space to keep recording.',
  };
}

/** "about 25 minutes", "under a minute", "about 1 hour 10 minutes" of recording at `bytesPerSecond`. */
export function minutesLeftWording(freeBytes: number, bytesPerSecond: number, unit: 'long' | 'short' = 'long'): string | null {
  if (!(bytesPerSecond > 0) || !Number.isFinite(freeBytes) || freeBytes < 0) {
    return null;
  }
  const minutes = Math.floor(freeBytes / bytesPerSecond / 60);
  const min = unit === 'long' ? (minutes === 1 ? 'minute' : 'minutes') : 'min';
  if (minutes < 1) {
    return `under a ${unit === 'long' ? 'minute' : 'min'}`;
  }
  if (minutes < 120) {
    return `about ${minutes} ${min}`;
  }
  const hours = Math.floor(minutes / 60);
  return `about ${hours} hours`;
}

/** The low-disk banner with video figures (§18.6): "Low disk space · 4 GB free · about 25 minutes of video at this quality." */
export function lowSpaceVideoBanner(freeBytes: number, bytesPerSecond: number): { lead: string; rest: string } {
  const left = minutesLeftWording(freeBytes, bytesPerSecond) ?? 'little time';
  return {
    lead: `Low disk space · ${formatFreeSpace(freeBytes)} free · ${left} of video at this quality.`,
    rest: ' Recording continues. If space runs out, video stops first and audio last.',
  };
}

/** The footer's left-hand video variants (§18.6), or null when video adds nothing to say. */
export function videoFooterLine(tracks: readonly VideoTrackStatus[], problems: readonly RecordingVideoProblemPayload[]): { tone: 'ok' | 'accent'; text: string } | null {
  if (tracks.length === 0) {
    return null;
  }
  const waiting = tracks.find((t) => t.state === 'waiting');
  if (waiting !== undefined) {
    const problem = [...problems].reverse().find((p) => p.trackId === waiting.trackId);
    if (problem?.code === 'video.displayDisconnected') {
      return { tone: 'accent', text: `Video paused · ${waiting.sourceName} disconnected · audio recording` };
    }
    if (problem?.code === 'video.windowMinimised') {
      return { tone: 'accent', text: `Video paused · "${waiting.sourceName}" minimised · audio recording` };
    }
    if (problem?.code === 'video.cameraInUse') {
      return { tone: 'accent', text: `Camera waiting · ${waiting.sourceName} is in use · ${tracks.some((t) => t.trackId === 'screen' && t.state === 'recording') ? 'screen and audio recording' : 'audio recording'}` };
    }
    return { tone: 'accent', text: `Video paused · ${waiting.sourceName} · audio recording` };
  }
  const live = tracks.filter((t) => t.state === 'recording' || t.state === 'paused');
  if (live.length === 0) {
    return { tone: 'accent', text: 'Video stopped · audio recording' };
  }
  if (live.some((t) => !t.hardware)) {
    const dropped = live.reduce((sum, t) => sum + t.framesDropped, 0);
    return { tone: 'accent', text: `Software encoder · ${dropped} dropped ${dropped === 1 ? 'frame' : 'frames'}` };
  }
  return { tone: 'ok', text: 'video and audio in sync' };
}
