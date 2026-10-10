// The browser-preview host's video (BRIDGE.md "Video (2.0, proposed)" and "Additions the video
// screens need"): invented displays, windows and cameras with schematic SVG thumbnails, the video
// tracks of a simulated session with recording.videoStatus once a second, the §18.6 problems on
// demand (`?video=` flags), the video of the sample recordings with generated, seekable preview
// files, the video stage, Remove video and its estimate. Nothing here is a real measurement.
import { formatSize } from '../format/storage';
import { formatTimecode } from '../format/duration';
import { recordedHeight, targetBitsPerSecond } from '../format/video';
import { isoWithOffset, type MockProject } from './mockData';
import { visibleStages } from './mockLibrary';
import { canGenerateVideo, generateWebm } from './mockVideoFile';
import { MockHostError } from './mockSession';
import type {
  EventName,
  EventPayload,
  MethodParams,
  MethodResult,
  PipState,
  ProjectVideo,
  ProjectVideoTrack,
  RecordingStatePayload,
  RecordingVideoProblemPayload,
  RecordingVideoSummary,
  RecoveredVideoTrack,
  SettingsSnapshot,
  StageFailure,
  StageStatus,
  VideoProblemCode,
  VideoQuality,
  VideoRole,
  VideoSettings,
  VideoSource,
  VideoTrackState,
  VideoTrackStatus,
} from './types';

// ---------------------------------------------------------------------------------------------
// Flags
// ---------------------------------------------------------------------------------------------

/**
 * `?video=` (comma-separated): `on` starts with Audio + video; `off` reports no video capture;
 * `nocamera`; before recording `blocked` (screen capture blocked), `privacy` (camera privacy off),
 * `inuse` (camera in use by Zoom, free 20 s into a recording), `noencoder`; during a recording, 10 s
 * in, `disconnect` (display), `closed` and `protected` (window), `minimised` (window, 8–14 s),
 * `fallback` (hardware encoder, 8 s), `diskfull` (15 s), `lowdisk` (the low-space banner with video
 * figures); in the Library `encoding` and `stagefail` (the video stage of the newest recording) and
 * `recovered` (a recovered recording with video).
 */
export type VideoFlag =
  | 'on'
  | 'off'
  | 'nocamera'
  | 'blocked'
  | 'privacy'
  | 'inuse'
  | 'noencoder'
  | 'disconnect'
  | 'closed'
  | 'protected'
  | 'minimised'
  | 'fallback'
  | 'diskfull'
  | 'lowdisk'
  | 'encoding'
  | 'stagefail'
  | 'recovered';

export const VIDEO_FLAGS: readonly VideoFlag[] = [
  'on',
  'off',
  'nocamera',
  'blocked',
  'privacy',
  'inuse',
  'noencoder',
  'disconnect',
  'closed',
  'protected',
  'minimised',
  'fallback',
  'diskfull',
  'lowdisk',
  'encoding',
  'stagefail',
  'recovered',
];

export function videoFlagsFromQuery(query: URLSearchParams): VideoFlag[] {
  const raw = query.get('video');
  if (raw === null) {
    return [];
  }
  return raw
    .split(',')
    .map((part) => part.trim().toLowerCase())
    .filter((part): part is VideoFlag => (VIDEO_FLAGS as readonly string[]).includes(part));
}

/** Flags that need a recording with video to show: the preview then starts with Audio + video. */
const RECORDING_FLAGS: ReadonlySet<VideoFlag> = new Set<VideoFlag>(['on', 'blocked', 'privacy', 'inuse', 'noencoder', 'disconnect', 'closed', 'protected', 'minimised', 'fallback', 'diskfull', 'lowdisk']);
const WINDOW_FLAGS: ReadonlySet<VideoFlag> = new Set<VideoFlag>(['closed', 'protected', 'minimised']);

// ---------------------------------------------------------------------------------------------
// Sources
// ---------------------------------------------------------------------------------------------

export const SAMPLE_DISPLAYS: readonly VideoSource[] = [
  {
    id: 'display:\\\\.\\DISPLAY1',
    kind: 'display',
    name: 'Display 1',
    detail: '2560 × 1440 · primary · this is where Memento is',
    width: 2560,
    height: 1440,
    refreshRate: 60,
    primary: true,
    excluded: false,
  },
  {
    id: 'display:\\\\.\\DISPLAY2',
    kind: 'display',
    name: 'Display 2',
    detail: '1920 × 1080 · right of Display 1',
    width: 1920,
    height: 1080,
    refreshRate: 60,
    primary: false,
    excluded: false,
  },
];

export const SAMPLE_WINDOWS: readonly VideoSource[] = [
  { id: 'window:1A2B3C', kind: 'window', name: 'Zoom Meeting', detail: 'Zoom · 1600 × 900', width: 1600, height: 900, refreshRate: 60, primary: false, excluded: false },
  { id: 'window:2B3C4D', kind: 'window', name: 'Library — Figma', detail: 'Figma · 2000 × 1250', width: 2000, height: 1250, refreshRate: 60, primary: false, excluded: false },
  { id: 'window:3C4D5E', kind: 'window', name: 'DESIGN.md — Microsoft Edge', detail: 'Edge · 1440 × 1000', width: 1440, height: 1000, refreshRate: 60, primary: false, excluded: false },
  { id: 'window:4D5E6F', kind: 'window', name: 'Memento', detail: 'This app is excluded from capture', width: 1440, height: 900, refreshRate: 60, primary: false, excluded: true },
  { id: 'window:5E6F70', kind: 'window', name: 'Password vault', detail: 'This window keeps itself out of captures', width: 900, height: 640, refreshRate: 60, primary: false, excluded: true },
];

export const SAMPLE_CAMERAS: readonly VideoSource[] = [
  { id: 'camera:usb-brio', kind: 'camera', name: 'Logitech Brio', detail: 'USB · 1920 × 1080 · 30 fps', width: 1920, height: 1080, refreshRate: 30, primary: false, excluded: false },
  { id: 'camera:integrated', kind: 'camera', name: 'Integrated camera', detail: 'Built in · 1280 × 720 · 30 fps', width: 1280, height: 720, refreshRate: 30, primary: false, excluded: false },
];

/** Settings › Recording › Video defaults as a new install has them (BRIDGE.md additions, Settings). */
export function defaultVideoSettings(): VideoSettings {
  return {
    defaultSource: 'screenAndCamera',
    resolution: 'display',
    frameRate: 30,
    quality: 'balanced',
    defaultCameraId: null,
    pipCorner: 'bottomRight',
    hardwareEncoder: true,
    includePointer: true,
  };
}

/** The recording block's video fields for the preview: Audio + video when a flag needs it. */
export function videoRecordingSettings(flags: readonly VideoFlag[]): { captureVideo: boolean; video: VideoSettings } {
  const video = defaultVideoSettings();
  if (flags.some((f) => WINDOW_FLAGS.has(f))) {
    video.defaultSource = 'window';
  }
  return { captureVideo: flags.some((f) => RECORDING_FLAGS.has(f)), video };
}

export function roleOf(sourceId: string): VideoRole | null {
  if (sourceId.startsWith('display:') || sourceId.startsWith('window:')) {
    return 'screen';
  }
  if (sourceId.startsWith('camera:')) {
    return 'camera';
  }
  return null;
}

// ---------------------------------------------------------------------------------------------
// Thumbnails: schematic, like the renders, with something that moves so "live" is visible
// ---------------------------------------------------------------------------------------------

function svgUrl(body: string): string {
  return `data:image/svg+xml,${encodeURIComponent(`<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 160 90" preserveAspectRatio="xMidYMid slice">${body}</svg>`)}`;
}

function clock(ms: number): string {
  const date = new Date(ms);
  return [date.getHours(), date.getMinutes(), date.getSeconds()].map((n) => String(n).padStart(2, '0')).join(':');
}

export function thumbnailSvg(source: VideoSource, nowMs: number): string {
  const t = nowMs / 1000;
  const slide = Math.round(((t * 9) % 96) * 10) / 10;
  if (source.kind === 'camera') {
    const sway = (Math.sin(t / 1.7) * 3).toFixed(1);
    return svgUrl(
      `<defs><radialGradient id="g" cx="${50 + Number(sway) / 2}%" cy="44%" r="60%"><stop offset="0.28" stop-color="#8C7B6A"/><stop offset="0.3" stop-color="#4A443D"/></radialGradient></defs>` +
        `<rect width="160" height="90" fill="url(#g)"/><ellipse cx="${80 + Number(sway)}" cy="92" rx="34" ry="22" fill="#5C5248"/>` +
        `<text x="6" y="85" font-family="monospace" font-size="7" fill="#C9C5BC">${clock(nowMs)}</text>`,
    );
  }
  if (source.excluded) {
    return svgUrl('<rect width="160" height="90" fill="#E4E1DA"/>');
  }
  if (source.kind === 'window') {
    return svgUrl(
      `<rect width="160" height="90" fill="#3A3733"/><rect x="13" y="14" width="134" height="65" rx="3" fill="#4A463F"/>` +
        `<rect x="18" y="20" width="${40 + (source.name.length % 5) * 8}" height="5" rx="1" fill="#57534C"/>` +
        `<rect x="${20 + slide}" y="44" width="22" height="12" rx="2" fill="#6A655C"/>` +
        `<text x="6" y="86" font-family="monospace" font-size="7" fill="#C9C5BC">${clock(nowMs)}</text>`,
    );
  }
  return svgUrl(
    `<rect width="160" height="90" fill="#2A2824"/><rect x="16" y="12" width="128" height="62" rx="3" fill="#3A3733"/>` +
      `<rect x="16" y="12" width="64" height="5" rx="1" fill="#57534C"/>` +
      `<rect x="${22 + slide}" y="${source.primary ? 40 : 52}" width="26" height="14" rx="2" fill="#4A463F"/>` +
      `<text x="6" y="86" font-family="monospace" font-size="7" fill="#C9C5BC">${source.name} · ${clock(nowMs)}</text>`,
  );
}

// ---------------------------------------------------------------------------------------------
// Preview files
// ---------------------------------------------------------------------------------------------

const PREVIEW_FRAMES = 1200;

/** The preview file's frames: the render's schematic screen or camera, with the timeline time drawn on. */
function drawFrame(role: VideoRole, title: string) {
  return (context: OffscreenCanvasRenderingContext2D, atMs: number): void => {
    const { width: w, height: h } = context.canvas;
    if (role === 'camera') {
      const gradient = context.createRadialGradient(w / 2, h * 0.42, 0, w / 2, h * 0.42, w * 0.6);
      gradient.addColorStop(0, '#8C7B6A');
      gradient.addColorStop(0.28, '#8C7B6A');
      gradient.addColorStop(0.3, '#4A443D');
      gradient.addColorStop(1, '#4A443D');
      context.fillStyle = gradient;
      context.fillRect(0, 0, w, h);
      context.fillStyle = '#EDEAE4';
      context.font = `${Math.round(h / 7)}px monospace`;
      context.fillText(formatTimecode(atMs), w * 0.06, h * 0.9);
      return;
    }
    context.fillStyle = '#2A2824';
    context.fillRect(0, 0, w, h);
    context.fillStyle = '#3A3733';
    context.fillRect(w * 0.06, h * 0.08, w * 0.88, h * 0.78);
    context.fillStyle = '#57534C';
    context.fillRect(w * 0.06, h * 0.08, w * 0.28, h * 0.045);
    const slide = ((atMs / 1000) % 60) / 60;
    context.fillStyle = '#4A463F';
    context.fillRect(w * (0.1 + slide * 0.6), h * 0.55, w * 0.18, h * 0.16);
    context.fillStyle = '#EDEAE4';
    context.font = `${Math.round(h / 5)}px monospace`;
    context.fillText(formatTimecode(atMs), w * 0.12, h * 0.42);
    context.fillStyle = '#C9C5BC';
    context.font = `${Math.round(h / 14)}px sans-serif`;
    context.fillText(`${title} · preview`, w * 0.12, h * 0.8);
  };
}

// ---------------------------------------------------------------------------------------------
// The environment and the module
// ---------------------------------------------------------------------------------------------

export interface MockVideoEnvironment {
  emit<E extends EventName>(event: E, payload: EventPayload<E>): void;
  now(): number;
  projects: Map<string, MockProject>;
  find(recordingId: string): MockProject;
  changed(...recordingIds: string[]): void;
  setStages(project: MockProject, stages: StageStatus[]): void;
  settings(): SettingsSnapshot;
  /** The session as recording.current answers it. */
  session(): RecordingStatePayload | null;
  flags: readonly VideoFlag[];
  /** Background timers (the status cadence, the video stage, preview files); off in unit tests. */
  live: boolean;
  stepMs: number;
  log(message: string): void;
}

export const VIDEO_METHODS = [
  'video.listSources',
  'video.thumbnail',
  'recording.setVideoSource',
  'recording.setPip',
  'recording.setVideoQuality',
  'video.openPrivacySettings',
  'video.remove',
  'video.removeEstimate',
] as const;

type VideoMethod = (typeof VIDEO_METHODS)[number];
type VideoHandlers = { [M in VideoMethod]: (params: MethodParams<M>) => MethodResult<M> };

export interface MockVideo {
  handlers: VideoHandlers;
  available(): boolean;
  /** recording.start: the audio ids go to the session, the video ids start tracks here. */
  splitSourceIds(sourceIds: readonly string[]): { audio: string[]; video: string[] };
  started(sessionId: string, recordingId: string, videoIds: readonly string[]): void;
  /** Pause, resume, stop: send the tracks' new state now rather than at the next tick. */
  stateChanged(): void;
  /** The session finished: the recording's video, or null when it had none. */
  finalize(recordingId: string, durationMs: number): { summary: RecordingVideoSummary; project: ProjectVideo } | null;
  /** The sample library's video, by flag. */
  decorateSamples(): void;
  failureOf(recordingId: string): StageFailure | null;
  /** processing.retry for the video stage; false when the remedy is not one of its own. */
  retry(recordingId: string, remedyId: string | undefined): boolean;
  usage(): { videoBytes: number; videoRecordings: number };
  recovered(recordingId: string): RecoveredVideoTrack[];
  /** One status tick now (tests drive the cadence by hand). */
  tick(): void;
}

interface LiveTrack {
  role: VideoRole;
  source: VideoSource;
  state: VideoTrackState;
  reason: string | null;
  /** Turned off by the person: left out of videoTracks, kept for the project. */
  off: boolean;
  hardware: boolean;
  width: number;
  height: number;
  frameRate: number;
  quality: VideoQuality;
  targetBps: number;
  actualBps: number;
  framesWritten: number;
  framesDropped: number;
  bytesWritten: number;
  /** Timeline position where the track began. */
  startOffsetMs: number;
  /** Recorded time of this track so far. */
  durationMs: number;
  endReason: string | null;
  parts: number;
  /** A waiting camera starts here (inuse); a minimised window resumes here. */
  resumeAtMs: number | null;
}

interface LiveSession {
  sessionId: string;
  recordingId: string;
  tracks: LiveTrack[];
  pip: PipState;
  lastElapsed: number;
  fired: Set<string>;
}

const GIB = 1024 ** 3;
const HARDWARE_ENCODER = 'NVIDIA H.264 Encoder MFT';
const SOFTWARE_ENCODER = 'Microsoft H.264 Encoder MFT';

/** Bytes a sample recording's video takes per second (about 3.6 Mb/s, what a real screen and camera use). */
const SAMPLE_VIDEO_BYTES_PER_SECOND = 450_000;

interface SampleVideo {
  screen: [string, number, number, number] | null;
  camera: [string, number, number] | null;
  pip: PipState;
  dropped: number;
  videoStage?: boolean;
}

const SAMPLE_VIDEO: Record<string, SampleVideo> = {
  '20261005-160000-dsrev': { screen: ['Display 1', 2560, 1440, 30], camera: ['Logitech Brio', 1920, 1080], pip: { enabled: true, corner: 'bottomRight' }, dropped: 0 },
  '20261003-110000-cs301': { screen: ['Display 1', 1920, 1080, 30], camera: null, pip: { enabled: false, corner: 'bottomRight' }, dropped: 3 },
  '20260918-160000-thall': { screen: ['Display 1', 2560, 1440, 30], camera: ['Logitech Brio', 1920, 1080], pip: { enabled: true, corner: 'topRight' }, dropped: 2, videoStage: true },
};
const REMOVED_SAMPLE = '20260929-090000-board';
const PROCESSING_SAMPLE = '20261006-100000-q3plan';
const RECOVERED_SAMPLE = '20261002-153000-sam11';

const VIDEO_FAILURE: StageFailure = {
  stage: 'video',
  message: 'Finalising the video file stopped at 71%.',
  kept: 'The raw frames and all audio tracks are safe, and the transcript was not affected.',
  remedies: [
    { id: 'software', label: 'Retry with software encoder' },
    { id: 'keepRaw', label: 'Keep raw frames' },
  ],
};

export function createMockVideo(env: MockVideoEnvironment): MockVideo {
  const has = (flag: VideoFlag): boolean => env.flags.includes(flag);
  let live: LiveSession | null = null;
  let timer: ReturnType<typeof setInterval> | null = null;
  const failures = new Map<string, StageFailure>();
  const recoveredVideo = new Map<string, RecoveredVideoTrack[]>();
  const urls = new Map<string, string>();
  const generating = new Set<string>();

  const cameras = (): VideoSource[] => (has('nocamera') ? [] : [...SAMPLE_CAMERAS]);
  const allSources = (): VideoSource[] => [...SAMPLE_DISPLAYS, ...SAMPLE_WINDOWS, ...cameras()];
  const sourceById = (id: string): VideoSource | undefined => allSources().find((s) => s.id === id);
  const elapsed = (): number => env.session()?.elapsedMs ?? 0;
  const at = (): string => isoWithOffset(new Date(env.now()));

  // --- the session's tracks -------------------------------------------------------------------

  const configure = (track: LiveTrack): void => {
    const settings = env.settings().recording.video;
    track.frameRate = track.role === 'camera' ? Math.min(30, track.source.refreshRate) : settings.frameRate;
    track.height = track.role === 'camera' ? Math.min(track.source.height, 1080) : recordedHeight(track.source, settings.resolution);
    track.width = Math.round((track.source.width / track.source.height) * track.height);
    track.targetBps = targetBitsPerSecond(track.height, track.frameRate, track.quality);
  };

  const newTrack = (role: VideoRole, source: VideoSource): LiveTrack => {
    const settings = env.settings().recording.video;
    const track: LiveTrack = {
      role,
      source,
      state: 'recording',
      reason: null,
      off: false,
      hardware: settings.hardwareEncoder,
      width: 0,
      height: 0,
      frameRate: 30,
      quality: settings.quality,
      targetBps: 0,
      actualBps: 0,
      framesWritten: 0,
      framesDropped: 0,
      bytesWritten: 0,
      startOffsetMs: Math.round(elapsed()),
      durationMs: 0,
      endReason: null,
      parts: 1,
      resumeAtMs: null,
    };
    configure(track);
    return track;
  };

  const statusOf = (track: LiveTrack): VideoTrackStatus => ({
    trackId: track.role,
    state: track.state,
    sourceId: track.source.id,
    sourceName: track.source.name,
    reason: track.reason,
    encoder: track.hardware ? HARDWARE_ENCODER : SOFTWARE_ENCODER,
    hardware: track.hardware,
    width: track.width,
    height: track.height,
    frameRate: track.frameRate,
    targetBitsPerSecond: track.targetBps,
    actualBitsPerSecond: track.actualBps,
    framesWritten: track.framesWritten,
    framesDropped: track.framesDropped,
    bytesWritten: track.bytesWritten,
    durationMs: track.durationMs,
  });

  const shown = (session: LiveSession): VideoTrackStatus[] =>
    session.tracks
      .filter((t) => !t.off)
      .sort((a, b) => (a.role === b.role ? 0 : a.role === 'screen' ? -1 : 1))
      .map(statusOf);

  const emitStatus = (): void => {
    if (live === null) {
      return;
    }
    env.emit('recording.videoStatus', { sessionId: live.sessionId, videoTracks: shown(live), pip: { ...live.pip } });
  };

  const problem = (track: LiveTrack, code: VideoProblemCode, state: VideoTrackState, detail: string | null, message: string): void => {
    if (live === null) {
      return;
    }
    track.state = state;
    track.reason = state === 'recording' ? null : message;
    if (state === 'ended' || state === 'stopped') {
      track.endReason = message;
    }
    const payload: RecordingVideoProblemPayload = {
      sessionId: live.sessionId,
      trackId: track.role,
      state,
      code,
      sourceId: track.source.id,
      sourceName: track.source.name,
      detail,
      message,
      atMs: Math.round(elapsed()),
    };
    env.emit('recording.videoProblem', payload);
    emitStatus();
  };

  const when = (): string => formatTimecode(elapsed());

  /** The problems a flag starts a track with (before any picture is written). */
  const startProblems = (track: LiveTrack): void => {
    if (has('noencoder')) {
      problem(track, 'video.encoderUnavailable', 'stopped', null, `No video encoder could be opened for the ${track.role} track. Audio keeps recording; nothing was lost.`);
    } else if (track.role === 'screen' && has('blocked')) {
      problem(track, 'video.captureBlocked', 'stopped', null, 'Screen capture is blocked for Memento in Windows privacy settings. Audio sources are not affected.');
    } else if (track.role === 'camera' && has('privacy')) {
      problem(track, 'video.cameraPrivacy', 'stopped', 'Let desktop apps access your camera', `${track.source.name} is turned off in Windows privacy settings ("Let desktop apps access your camera"). Recording continues with the screen and audio.`);
    } else if (track.role === 'camera' && has('inuse')) {
      track.resumeAtMs = 20_000;
      problem(track, 'video.cameraInUse', 'waiting', 'Zoom', `${track.source.name} is in use by Zoom. Recording continues with the screen and all audio tracks; the camera track starts the moment it becomes free.`);
    }
  };

  /** Problems that happen a few seconds into a recording, once each. */
  const scheduled = (session: LiveSession, now: number): void => {
    const once = (key: string, after: number, run: () => void): void => {
      if (!session.fired.has(key) && now >= after) {
        session.fired.add(key);
        run();
      }
    };
    const screen = session.tracks.find((t) => t.role === 'screen' && !t.off);
    if (screen?.state === 'recording') {
      if (screen.source.kind === 'display' && has('disconnect')) {
        once('disconnect', 10_000, () => {
          problem(screen, 'video.displayDisconnected', 'waiting', null, `${screen.source.name} was disconnected at ${when()}. The screen track is paused; audio${session.tracks.some((t) => t.role === 'camera' && !t.off) ? ' and the camera keep' : ' keeps'} recording.`);
        });
      }
      if (screen.source.kind === 'window' && has('closed')) {
        once('closed', 10_000, () => {
          problem(screen, 'video.windowClosed', 'ended', null, `"${screen.source.name}" was closed at ${when()}. The video track ends there; everything before it is saved.`);
        });
      }
      if (screen.source.kind === 'window' && has('protected')) {
        once('protected', 10_000, () => {
          problem(screen, 'video.protectedContent', 'ended', null, `"${screen.source.name}" started keeping itself out of captures at ${when()}. The video track ends there rather than recording black; everything before it is saved.`);
        });
      }
      if (screen.source.kind === 'window' && has('minimised')) {
        once('minimised', 8_000, () => {
          screen.resumeAtMs = 14_000;
          problem(screen, 'video.windowMinimised', 'waiting', null, `"${screen.source.name}" is minimised. The video pauses until it comes back; audio keeps recording.`);
        });
      }
    }
    const hardware = session.tracks.find((t) => !t.off && t.state === 'recording' && t.hardware);
    if (hardware !== undefined && has('fallback')) {
      once('fallback', 8_000, () => {
        for (const track of session.tracks) {
          if (track.hardware) {
            track.hardware = false;
            track.parts += 1;
          }
        }
        problem(hardware, 'video.encoderFallback', 'recording', SOFTWARE_ENCODER, `The hardware encoder failed at ${when()}. Switched to software encoding; no frames were lost.`);
      });
    }
    if (has('diskfull')) {
      once('diskfull', 15_000, () => {
        for (const track of session.tracks.filter((t) => !t.off && (t.state === 'recording' || t.state === 'waiting'))) {
          problem(track, 'video.diskFull', 'stopped', null, `Video stopped at ${when()} because the drive is nearly full; audio keeps recording. Everything recorded so far is saved.`);
        }
      });
    }
    for (const track of session.tracks) {
      if (track.resumeAtMs !== null && now >= track.resumeAtMs && track.state === 'waiting') {
        track.resumeAtMs = null;
        track.state = 'recording';
        track.reason = null;
        if (track.framesWritten === 0) {
          track.startOffsetMs = Math.round(now);
        }
      }
    }
  };

  const tick = (): void => {
    const session = live;
    const state = env.session();
    if (session === null || state?.sessionId !== session.sessionId) {
      return;
    }
    const now = state.elapsedMs;
    const step = Math.max(0, now - session.lastElapsed);
    session.lastElapsed = now;
    const recording = state.state === 'recording';
    for (const track of session.tracks) {
      if (track.off || track.state === 'ended' || track.state === 'stopped') {
        continue;
      }
      if (!recording) {
        track.state = track.state === 'waiting' ? 'waiting' : 'paused';
        track.actualBps = 0;
        continue;
      }
      if (track.state === 'paused') {
        track.state = 'recording';
      }
      if (track.state !== 'recording') {
        track.actualBps = 0;
        continue;
      }
      const frames = Math.round((step / 1000) * track.frameRate);
      track.framesWritten += frames;
      // The software encoder drops a frame now and then under load (ENGINE-NOTES.md §M.3).
      if (!track.hardware && frames > 0 && Math.random() < 0.25) {
        track.framesDropped += 1;
      }
      track.actualBps = Math.round(track.targetBps * (0.62 + Math.random() * 0.2));
      track.bytesWritten += Math.round((track.actualBps / 8) * (step / 1000));
      track.durationMs += step;
    }
    if (recording) {
      scheduled(session, now);
    }
    emitStatus();
  };

  const startTimer = (): void => {
    if (!env.live || timer !== null) {
      return;
    }
    timer = setInterval(() => {
      const state = env.session();
      if (live === null || state?.sessionId !== live.sessionId || state.state === 'finalizing' || state.state === 'ready' || state.state === 'stopped') {
        if (timer !== null) {
          clearInterval(timer);
        }
        timer = null;
        return;
      }
      tick();
    }, 1000);
  };

  const requireLive = (sessionId: string): LiveSession => {
    const state = env.session();
    if (live?.sessionId !== sessionId || state?.sessionId !== sessionId || (state.state !== 'recording' && state.state !== 'paused')) {
      throw new MockHostError('recording.noSession', 'There is no recording in progress with that session.', sessionId);
    }
    return live;
  };

  const refuseSource = (source: VideoSource): void => {
    if (source.excluded) {
      throw new MockHostError(
        'video.protectedContent',
        source.name === 'Memento'
          ? 'Memento leaves its own window out of captures, so it cannot be recorded. Choose another window or a whole display.'
          : `"${source.name}" keeps itself out of captures, so Memento would only record black. Choose another window or a whole display.`,
        source.id,
      );
    }
  };

  // --- sample recordings ----------------------------------------------------------------------

  const fileKey = (recordingId: string, role: VideoRole, part: number): string => `${recordingId}/${role}/${part}`;

  /** A preview file for each part: a fake URL in jsdom, a generated WebM in a browser (null until it is built). */
  const partUrl = (recordingId: string, track: ProjectVideoTrack, part: number, durationMs: number): string | null => {
    const key = fileKey(recordingId, track.id, part);
    const known = urls.get(key);
    if (known !== undefined) {
      return known;
    }
    if (!canGenerateVideo() || !env.live) {
      const fake = `blob:memento-preview/${key}`;
      urls.set(key, fake);
      return fake;
    }
    if (!generating.has(key)) {
      generating.add(key);
      const big = track.id === 'screen';
      void generateWebm({
        durationMs: Math.max(1000, durationMs),
        width: big ? 320 : 192,
        height: big ? 180 : 108,
        frameIntervalMs: Math.max(1000, Math.ceil(durationMs / PREVIEW_FRAMES / 500) * 500),
        draw: drawFrame(track.id, track.sourceName),
      })
        .then((blob) => {
          urls.set(key, URL.createObjectURL(blob));
          const project = env.projects.get(recordingId);
          if (project?.video != null) {
            project.video = withUrls(recordingId, project.video);
          }
          env.changed(recordingId);
        })
        .catch((error: unknown) => {
          env.log(`[bridge:mock] the preview video for ${key} could not be built: ${error instanceof Error ? error.message : String(error)}`);
        });
    }
    return null;
  };

  const withUrls = (recordingId: string, video: ProjectVideo): ProjectVideo => ({
    ...video,
    tracks: video.tracks.map((track) => ({
      ...track,
      parts: track.parts.map((part, i) => ({ ...part, url: part.url ?? partUrl(recordingId, track, i, part.durationMs) })),
    })),
  });

  const sampleTrack = (role: VideoRole, name: string, width: number, height: number, frameRate: number, durationMs: number, sizeBytes: number, dropped: number): ProjectVideoTrack => ({
    id: role,
    sourceKind: role === 'camera' ? 'camera' : 'display',
    sourceName: name,
    width,
    height,
    frameRate,
    encoder: HARDWARE_ENCODER,
    hardware: true,
    framesWritten: Math.round((durationMs / 1000) * frameRate),
    framesDropped: dropped,
    sizeBytes,
    startOffsetMs: 0,
    durationMs,
    endReason: null,
    parts: [{ url: null, startOffsetMs: 0, durationMs }],
  });

  const sampleVideo = (project: MockProject, spec: SampleVideo): ProjectVideo => {
    const { durationMs } = project.summary;
    const total = Math.round((durationMs / 1000) * SAMPLE_VIDEO_BYTES_PER_SECOND);
    const cameraShare = spec.camera === null ? 0 : 0.25;
    const tracks: ProjectVideoTrack[] = [];
    if (spec.screen !== null) {
      tracks.push(sampleTrack('screen', spec.screen[0], spec.screen[1], spec.screen[2], spec.screen[3], durationMs, Math.round(total * (1 - cameraShare)), spec.dropped));
    }
    if (spec.camera !== null) {
      tracks.push(sampleTrack('camera', spec.camera[0], spec.camera[1], spec.camera[2], 30, durationMs, Math.round(total * cameraShare), 0));
    }
    return { tracks, pip: { ...spec.pip }, sizeBytes: total, removed: null };
  };

  const summaryOf = (video: ProjectVideo, removedAt: string | null = null): RecordingVideoSummary => {
    const screen = video.tracks.find((t) => t.id === 'screen');
    const first = screen ?? video.tracks[0];
    return { roles: video.tracks.map((t) => t.id), height: first?.height ?? 0, removedAt };
  };

  const addHistory = (project: MockProject, summary: string, detail: string): void => {
    project.history = [...project.history, { at: at(), stage: 'video', event: 'completed', summary, detail }];
  };

  const setVideoStage = (project: MockProject, stage: StageStatus | null): void => {
    const others = project.stages.filter((s) => s.stage !== 'video');
    const storedAt = others.findIndex((s) => s.stage === 'stored');
    const stages = stage === null ? others : [...others.slice(0, storedAt + 1), stage, ...others.slice(storedAt + 1)];
    env.setStages(project, stages);
  };

  const videoLabel = (percent: number, device: 'GPU' | 'CPU'): string =>
    percent < 70 ? `Encoding ${percent}% · ${device}` : percent < 85 ? 'Trimming' : 'Muxing camera inset';

  /** The video stage of `project` moves on until done, then its parts become readable. */
  const runVideoStage = (project: MockProject, from: number, device: 'GPU' | 'CPU'): void => {
    let percent = from;
    setVideoStage(project, { stage: 'video', state: 'active', percent, label: videoLabel(percent, device) });
    if (!env.live) {
      return;
    }
    const step = setInterval(() => {
      percent = Math.min(100, percent + 4);
      if (percent >= 100) {
        clearInterval(step);
        setVideoStage(project, { stage: 'video', state: 'done', percent: null, label: 'Done' });
        if (project.video != null) {
          project.video = withUrls(project.summary.id, { ...project.video, tracks: project.video.tracks.map((t) => ({ ...t, parts: [{ url: null, startOffsetMs: t.startOffsetMs, durationMs: t.durationMs }] })) });
        }
        addHistory(project, 'Video finalised', `${project.video?.tracks.length ?? 0} tracks · ${formatSize(project.video?.sizeBytes ?? 0)} · ${device === 'GPU' ? HARDWARE_ENCODER : SOFTWARE_ENCODER}`);
        env.changed(project.summary.id);
        return;
      }
      setVideoStage(project, { stage: 'video', state: 'active', percent, label: videoLabel(percent, device) });
    }, Math.max(300, env.stepMs * 4));
  };

  const decorate = (project: MockProject, video: ProjectVideo): void => {
    project.video = video;
    const audioBytes = project.summary.sizeBytes - (project.summary.hasVideo ? Math.round((project.summary.durationMs / 1000) * SAMPLE_VIDEO_BYTES_PER_SECOND) : 0);
    project.summary = { ...project.summary, hasVideo: true, video: summaryOf(video), sizeBytes: audioBytes + video.sizeBytes };
  };

  // --- the handlers ---------------------------------------------------------------------------

  const handlers: VideoHandlers = {
    'video.listSources': () => {
      if (has('off')) {
        return { displays: [], windows: [], cameras: [] };
      }
      return { displays: [...SAMPLE_DISPLAYS], windows: [...SAMPLE_WINDOWS], cameras: cameras() };
    },
    'video.thumbnail': (params) => {
      const source = sourceById(params.sourceId);
      if (source === undefined) {
        throw new MockHostError('video.sourceNotFound', 'That display, window or camera is not there any more. Choose another one.', params.sourceId);
      }
      if (source.kind !== 'camera' && !source.excluded && has('blocked')) {
        throw new MockHostError('video.captureBlocked', 'Screen capture is blocked for Memento in Windows privacy settings. Allow it under Privacy & security › Screen capture, then come back. Audio sources are not affected.');
      }
      if (source.kind === 'camera' && has('privacy')) {
        throw new MockHostError('video.cameraPrivacy', `${source.name} is turned off in Windows privacy settings ("Let desktop apps access your camera"). Turn it on there, then come back.`, 'Let desktop apps access your camera');
      }
      if (source.kind === 'camera' && has('inuse') && elapsed() < 20_000) {
        throw new MockHostError('video.cameraInUse', `${source.name} is in use by Zoom. Close it there, or pick another camera.`, 'Zoom');
      }
      return { dataUrl: thumbnailSvg(source, env.now()) };
    },
    'recording.setVideoSource': (params) => {
      const session = requireLive(params.sessionId);
      const current = session.tracks.find((t) => t.role === params.role && !t.off);
      if (params.sourceId === null) {
        if (current !== undefined) {
          current.off = true;
          current.endReason ??= `Turned off at ${when()}.`;
          if (current.state === 'recording' || current.state === 'waiting' || current.state === 'paused') {
            current.state = 'ended';
          }
        }
        emitStatus();
        return { videoTracks: shown(session) };
      }
      const source = sourceById(params.sourceId);
      if (source === undefined || roleOf(source.id) !== params.role) {
        throw new MockHostError('video.sourceNotFound', 'That display, window or camera is not there any more. Choose another one; the other tracks keep recording.', params.sourceId);
      }
      refuseSource(source);
      if (current !== undefined && (current.state === 'recording' || current.state === 'waiting' || current.state === 'paused')) {
        // Switching: the track continues with the new source (§18.2).
        current.source = source;
        if (!(current.role === 'camera' && has('inuse') && elapsed() < 20_000)) {
          current.state = env.session()?.state === 'paused' ? 'paused' : 'recording';
          current.reason = null;
          current.resumeAtMs = null;
        }
        configure(current);
      } else {
        // A new track (the old one, ended or stopped, stays in the project).
        if (current !== undefined) {
          current.off = true;
        }
        const track = newTrack(params.role, source);
        session.tracks.push(track);
        startProblems(track);
      }
      emitStatus();
      return { videoTracks: shown(session) };
    },
    'recording.setPip': (params) => {
      const session = requireLive(params.sessionId);
      session.pip = { enabled: params.enabled, corner: params.corner };
      emitStatus();
      return {};
    },
    'recording.setVideoQuality': (params) => {
      const session = requireLive(params.sessionId);
      for (const track of session.tracks) {
        track.quality = params.quality;
        track.targetBps = targetBitsPerSecond(track.height, track.frameRate, params.quality);
      }
      emitStatus();
      return { videoTracks: shown(session) };
    },
    'video.openPrivacySettings': (params) => {
      env.log(`[bridge:mock] would open ms-settings:${params.which === 'camera' ? 'privacy-webcam' : 'privacy-graphicsCaptureProgrammatic'}`);
      return {};
    },
    'video.remove': (params) => {
      const project = env.find(params.recordingId);
      const video = project.video ?? null;
      if (video === null || video.tracks.length === 0) {
        throw new MockHostError('bridge.invalidParams', `"${project.summary.title}" has no video to remove. Nothing was changed.`, params.recordingId);
      }
      const freed = video.sizeBytes;
      const removedAt = at();
      project.video = { ...video, tracks: [], sizeBytes: 0, removed: { at: removedAt, bytesFreed: freed } };
      project.summary = { ...project.summary, hasVideo: false, video: { ...summaryOf(video), removedAt }, sizeBytes: Math.max(0, project.summary.sizeBytes - freed) };
      addHistory(project, `Removed the video · ${formatSize(freed)} freed`, 'Audio tracks, transcript and documents were kept.');
      env.changed(project.summary.id);
      return { freedBytes: freed };
    },
    'video.removeEstimate': (params) => {
      const cutoff = params.olderThanDays === null ? Number.POSITIVE_INFINITY : env.now() - params.olderThanDays * 86_400_000;
      const chosen = [...env.projects.values()].filter((p) => {
        if (p.video == null || p.video.tracks.length === 0) {
          return false;
        }
        if (params.recordingIds !== null) {
          return params.recordingIds.includes(p.summary.id);
        }
        return Date.parse(p.summary.createdAt) < cutoff;
      });
      return { recordings: chosen.length, bytes: chosen.reduce((sum, p) => sum + (p.video?.sizeBytes ?? 0), 0) };
    },
  };

  return {
    handlers,
    available: () => !has('off'),
    splitSourceIds(sourceIds) {
      return { audio: sourceIds.filter((id) => roleOf(id) === null), video: sourceIds.filter((id) => roleOf(id) !== null) };
    },
    started(sessionId, recordingId, videoIds) {
      live = { sessionId, recordingId, tracks: [], pip: { enabled: env.settings().recording.video.defaultSource === 'screenAndCamera', corner: env.settings().recording.video.pipCorner }, lastElapsed: 0, fired: new Set() };
      for (const id of videoIds) {
        const source = sourceById(id);
        const role = roleOf(id);
        if (source === undefined || role === null || source.excluded || live.tracks.some((t) => t.role === role)) {
          // Never keeps the audio from starting (BRIDGE.md): an unknown source is reported, not thrown.
          continue;
        }
        const track = newTrack(role, source);
        live.tracks.push(track);
      }
      emitStatus();
      for (const track of live.tracks) {
        startProblems(track);
      }
      startTimer();
    },
    stateChanged() {
      tick();
    },
    finalize(recordingId, durationMs) {
      if (live?.recordingId !== recordingId) {
        return null;
      }
      const session = live;
      live = null;
      if (timer !== null) {
        clearInterval(timer);
        timer = null;
      }
      const recorded = session.tracks.filter((t) => t.framesWritten > 0 || t.durationMs > 0);
      if (recorded.length === 0) {
        return null;
      }
      const byRole = (role: VideoRole): LiveTrack | undefined => recorded.filter((t) => t.role === role).at(-1);
      const tracks: ProjectVideoTrack[] = [];
      for (const role of ['screen', 'camera'] as const) {
        const track = byRole(role);
        if (track === undefined) {
          continue;
        }
        const length = Math.min(track.durationMs, Math.max(0, durationMs - track.startOffsetMs));
        tracks.push({
          id: role,
          sourceKind: track.source.kind,
          sourceName: track.source.name,
          width: track.width,
          height: track.height,
          frameRate: track.frameRate,
          encoder: track.hardware ? HARDWARE_ENCODER : SOFTWARE_ENCODER,
          hardware: track.hardware,
          framesWritten: track.framesWritten,
          framesDropped: track.framesDropped,
          sizeBytes: track.bytesWritten,
          startOffsetMs: track.startOffsetMs,
          durationMs: length,
          endReason: track.endReason,
          parts: [{ url: null, startOffsetMs: track.startOffsetMs, durationMs: length }],
        });
      }
      const sizeBytes = tracks.reduce((sum, t) => sum + t.sizeBytes, 0);
      const video = withUrls(recordingId, { tracks, pip: { ...session.pip }, sizeBytes, removed: null });
      const project = env.projects.get(recordingId);
      if (project !== undefined && recorded.some((t) => t.parts > 1)) {
        // Parts from two encoders are joined by the video stage (ENGINE-NOTES.md §M.3).
        project.video = video;
        setTimeout(() => {
          runVideoStage(project, 0, 'CPU');
        }, 0);
      }
      return { summary: summaryOf(video), project: video };
    },
    decorateSamples() {
      for (const [id, spec] of Object.entries(SAMPLE_VIDEO)) {
        const project = env.projects.get(id);
        if (project === undefined) {
          continue;
        }
        decorate(project, withUrls(id, sampleVideo(project, spec)));
        if (spec.videoStage === true && !project.stages.some((s) => s.stage === 'video')) {
          const storedAt = project.stages.findIndex((s) => s.stage === 'stored');
          project.stages.splice(storedAt + 1, 0, { stage: 'video', state: 'done', percent: null, label: 'Done' });
          project.summary = { ...project.summary, stages: visibleStages(project.stages) };
        }
      }
      const board = env.projects.get(REMOVED_SAMPLE);
      if (board !== undefined) {
        const removedAt = isoWithOffset(new Date(env.now() - 5 * 86_400_000));
        const bytesFreed = Math.round(1.9 * GIB);
        board.video = { tracks: [], pip: { enabled: true, corner: 'bottomRight' }, sizeBytes: 0, removed: { at: removedAt, bytesFreed } };
        board.summary = { ...board.summary, hasVideo: false, video: { roles: ['screen', 'camera'], height: 1440, removedAt } };
        board.history = [...board.history, { at: removedAt, stage: 'video', event: 'completed', summary: `Removed the video · ${formatSize(bytesFreed)} freed`, detail: 'Audio tracks, transcript and documents were kept.' }];
      }
      const q3 = env.projects.get(PROCESSING_SAMPLE);
      if (q3 !== undefined && (has('encoding') || has('stagefail'))) {
        const video = sampleVideo(q3, { screen: ['Display 1', 2560, 1440, 30], camera: ['Logitech Brio', 1920, 1080], pip: { enabled: true, corner: 'bottomRight' }, dropped: 0 });
        // Two parts per track (a hardware encoder failed): unreadable until the stage joins them.
        const split = (t: ProjectVideoTrack): ProjectVideoTrack => ({
          ...t,
          hardware: false,
          encoder: SOFTWARE_ENCODER,
          parts: [
            { url: null, startOffsetMs: 0, durationMs: Math.round(t.durationMs * 0.3) },
            { url: null, startOffsetMs: Math.round(t.durationMs * 0.3), durationMs: t.durationMs - Math.round(t.durationMs * 0.3) },
          ],
        });
        q3.video = { ...video, tracks: video.tracks.map(split) };
        const audioBytes = q3.summary.sizeBytes;
        q3.summary = { ...q3.summary, hasVideo: true, video: summaryOf(video), sizeBytes: audioBytes + video.sizeBytes };
        const storedAt = q3.stages.findIndex((s) => s.stage === 'stored');
        const stage: StageStatus = has('stagefail')
          ? { stage: 'video', state: 'failed', percent: null, label: 'Video failed at 71%' }
          : { stage: 'video', state: 'active', percent: 42, label: 'Encoding 42% · GPU' };
        q3.stages.splice(storedAt + 1, 0, stage);
        q3.summary = { ...q3.summary, stages: visibleStages(q3.stages) };
        if (has('stagefail')) {
          failures.set(q3.summary.id, VIDEO_FAILURE);
        } else if (env.live) {
          setTimeout(() => {
            runVideoStage(q3, 42, 'GPU');
          }, 1500);
        }
      }
      const sam = env.projects.get(RECOVERED_SAMPLE);
      if (sam !== undefined && has('recovered')) {
        const video = sampleVideo(sam, { screen: null, camera: ['Integrated camera', 1280, 720], pip: { enabled: false, corner: 'bottomRight' }, dropped: 0 });
        const recoveredMs = Math.max(0, sam.summary.durationMs - 20_000);
        const tracks = video.tracks.map((t) => ({ ...t, durationMs: recoveredMs, parts: [{ url: null, startOffsetMs: 0, durationMs: recoveredMs }] }));
        decorate(sam, withUrls(sam.summary.id, { ...video, tracks }));
        recoveredVideo.set(sam.summary.id, [{ trackId: 'camera', sourceName: 'Integrated camera', recoveredDurationMs: recoveredMs }]);
      }
    },
    failureOf(recordingId) {
      return failures.get(recordingId) ?? null;
    },
    retry(recordingId, remedyId) {
      const project = env.projects.get(recordingId);
      if (!project?.stages.some((s) => s.stage === 'video')) {
        return false;
      }
      failures.delete(recordingId);
      if (remedyId === 'keepRaw') {
        setVideoStage(project, { stage: 'video', state: 'done', percent: null, label: 'Done' });
        if (project.video != null) {
          project.video = withUrls(recordingId, project.video);
        }
        addHistory(project, 'Kept the raw frames', 'The video stays in the parts it was written in; nothing was re-encoded.');
        env.changed(recordingId);
        return true;
      }
      runVideoStage(project, 0, remedyId === 'software' ? 'CPU' : 'GPU');
      return true;
    },
    usage() {
      const withVideo = [...env.projects.values()].filter((p) => p.video != null && p.video.tracks.length > 0);
      return { videoBytes: withVideo.reduce((sum, p) => sum + (p.video?.sizeBytes ?? 0), 0), videoRecordings: withVideo.length };
    },
    recovered(recordingId) {
      return recoveredVideo.get(recordingId) ?? [];
    },
    tick,
  };
}
