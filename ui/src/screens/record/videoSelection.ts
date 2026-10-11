// What the VIDEO section of the Recording session shows and sends (DESIGN.md §18.1): the Screen,
// Window and Camera rows, the inset, the sub-line parts and the storage rate. Before recording the
// page keeps the choice itself and sends it with recording.start; while recording the rows follow
// recording.videoStatus (a display or a window is the one screen track, a camera the camera track).
import type {
  PipState,
  RecordingSettings,
  VideoListSourcesResult,
  VideoRole,
  VideoSource,
  VideoTrackStatus,
} from '../../bridge/types';
import { cameraLabel, formatFrameRate, formatResolution, heightLabel, recordedHeight, rolesWording, targetBitsPerSecond } from '../../format/video';

export interface VideoSelection {
  /** The Screen row (a whole display). */
  screen: boolean;
  /** The Window row (one window instead of a display). Never on together with `screen`. */
  window: boolean;
  camera: boolean;
  displayId: string | null;
  windowId: string | null;
  cameraId: string | null;
  pip: PipState;
}

export const NO_SOURCES: VideoListSourcesResult = { displays: [], windows: [], cameras: [] };

export function selectable(source: VideoSource): boolean {
  return !source.excluded;
}

/** The selection a new session starts with: Settings › Recording › Video defaults (§18.5). */
export function initialSelection(recording: Pick<RecordingSettings, 'captureVideo' | 'video'> | null, sources: VideoListSourcesResult): VideoSelection {
  const video = recording?.video;
  const primary = sources.displays.find((d) => d.primary) ?? sources.displays[0] ?? null;
  const camera = sources.cameras.find((c) => c.id === video?.defaultCameraId) ?? sources.cameras[0] ?? null;
  const firstWindow = sources.windows.find(selectable) ?? null;
  const want = recording?.captureVideo === true ? (video?.defaultSource ?? 'screenAndCamera') : null;
  return {
    screen: (want === 'screen' || want === 'screenAndCamera') && primary !== null,
    window: want === 'window' && firstWindow !== null,
    camera: (want === 'camera' || want === 'screenAndCamera') && camera !== null,
    displayId: primary?.id ?? null,
    windowId: want === 'window' ? (firstWindow?.id ?? null) : null,
    cameraId: camera?.id ?? null,
    pip: { enabled: want === 'screenAndCamera', corner: video?.pipCorner ?? 'bottomRight' },
  };
}

/** The ids recording.start sends beside the audio ones: the screen track's source, then the camera. */
export function selectedVideoIds(selection: VideoSelection | null): string[] {
  if (selection === null) {
    return [];
  }
  const ids: string[] = [];
  const screen = selection.window ? selection.windowId : selection.screen ? selection.displayId : null;
  if (screen !== null) {
    ids.push(screen);
  }
  if (selection.camera && selection.cameraId !== null) {
    ids.push(selection.cameraId);
  }
  return ids;
}

/** A track that is capturing or waiting to (an ended or stopped one is off). */
export function isLive(track: VideoTrackStatus | undefined): track is VideoTrackStatus {
  return track !== undefined && (track.state === 'recording' || track.state === 'paused' || track.state === 'waiting');
}

/**
 * The rows while recording, from the session's tracks: the screen track's source decides whether the
 * Screen or the Window row is on; the chosen ids follow the sources in use so the selectors name them.
 */
export function selectionFromStatus(tracks: readonly VideoTrackStatus[], pip: PipState, before: VideoSelection | null): VideoSelection {
  const screen = tracks.find((t) => t.trackId === 'screen');
  const camera = tracks.find((t) => t.trackId === 'camera');
  const screenIsWindow = screen?.sourceId.startsWith('window:') === true;
  return {
    screen: isLive(screen) && !screenIsWindow,
    window: isLive(screen) && screenIsWindow,
    camera: isLive(camera),
    displayId: screen !== undefined && !screenIsWindow ? screen.sourceId : (before?.displayId ?? null),
    windowId: screen !== undefined && screenIsWindow ? screen.sourceId : (before?.windowId ?? null),
    cameraId: camera?.sourceId ?? before?.cameraId ?? null,
    pip,
  };
}

export function findSource(sources: VideoListSourcesResult, id: string | null): VideoSource | null {
  if (id === null) {
    return null;
  }
  return [...sources.displays, ...sources.windows, ...sources.cameras].find((s) => s.id === id) ?? null;
}

/** The roles a selection records: "screen" covers a display or a window. */
export function selectedRoles(selection: VideoSelection | null): VideoRole[] {
  if (selection === null) {
    return [];
  }
  const roles: VideoRole[] = [];
  if (selection.screen || selection.window) {
    roles.push('screen');
  }
  if (selection.camera) {
    roles.push('camera');
  }
  return roles;
}

/**
 * The stage's sub-line parts: "screen + camera + 3 audio tracks" (§18.1); a window reads "window"
 * as the render does.
 */
export function sublineSources(selection: VideoSelection | null, audioCount: number): string {
  const parts: string[] = [];
  if (selection?.screen === true) {
    parts.push('screen');
  }
  if (selection?.window === true) {
    parts.push('window');
  }
  if (selection?.camera === true) {
    parts.push('camera');
  }
  parts.push(`${audioCount} audio ${audioCount === 1 ? 'track' : 'tracks'}`);
  return parts.join(' + ');
}

/** "Display 1 · 2560 × 1440 · 30 fps": the Screen row's sub-line and the preview caption. */
export function displayLine(display: VideoSource, recording: Pick<RecordingSettings, 'video'> | null, track?: VideoTrackStatus): string {
  if (track !== undefined) {
    return `${display.name} · ${formatResolution(track.width, track.height)} · ${formatFrameRate(track.frameRate)}`;
  }
  const height = recording === null ? display.height : recordedHeight(display, recording.video.resolution);
  const width = Math.round((display.width / display.height) * height);
  return `${display.name} · ${formatResolution(width, height)} · ${formatFrameRate(recording?.video.frameRate ?? 30)}`;
}

/** "Logitech Brio · 1080p": the Camera row's sub-line. */
export function cameraLine(camera: VideoSource): string {
  return cameraLabel(camera);
}

/** The preview's mono caption: the screen track's line, else the camera's, else "No video source". */
export function previewCaption(selection: VideoSelection | null, sources: VideoListSourcesResult, recording: Pick<RecordingSettings, 'video'> | null, tracks: readonly VideoTrackStatus[]): string {
  const screenTrack = tracks.find((t) => t.trackId === 'screen');
  if (selection?.screen === true) {
    const display = findSource(sources, selection.displayId);
    if (display !== null) {
      return displayLine(display, recording, isLive(screenTrack) ? screenTrack : undefined);
    }
  }
  if (selection?.window === true) {
    const window = findSource(sources, selection.windowId);
    const fps = isLive(screenTrack) ? screenTrack.frameRate : (recording?.video.frameRate ?? 30);
    return window === null ? `Window · ${formatFrameRate(fps)}` : `${window.name} · ${formatFrameRate(fps)}`;
  }
  if (selection?.camera === true) {
    const camera = findSource(sources, selection.cameraId);
    const track = tracks.find((t) => t.trackId === 'camera');
    const height = isLive(track) ? track.height : Math.min(camera === null || camera.height === 0 ? 1080 : camera.height, 1080);
    return height > 0 ? `Camera · ${heightLabel(height)}` : 'Camera';
  }
  return 'No video source';
}

/** The Tracks card's video lane label (§18.1). */
export function laneLabel(roles: readonly VideoRole[]): string {
  const words = rolesWording(roles);
  return words === '' ? 'Video (off)' : `${words.charAt(0).toLocaleUpperCase()}${words.slice(1)}`;
}

/**
 * Bytes per second the video adds while recording (the encoders' target, the ceiling a busy picture
 * reaches), from the live tracks, or before recording from the selection and Settings.
 */
export function videoBytesPerSecond(
  selection: VideoSelection | null,
  sources: VideoListSourcesResult,
  recording: Pick<RecordingSettings, 'video'> | null,
  tracks: readonly VideoTrackStatus[] | null,
): number {
  if (tracks !== null) {
    return tracks.filter(isLive).reduce((sum, t) => sum + t.targetBitsPerSecond / 8, 0);
  }
  if (selection === null || recording === null) {
    return 0;
  }
  const { video } = recording;
  let bits = 0;
  const screen = findSource(sources, selection.window ? selection.windowId : selection.screen ? selection.displayId : null);
  if (screen !== null) {
    bits += targetBitsPerSecond(recordedHeight(screen, video.resolution), video.frameRate, video.quality);
  }
  const camera = selection.camera ? findSource(sources, selection.cameraId) : null;
  if (camera !== null) {
    // A camera lists no size until it is opened (0): count it as 1080p.
    bits += targetBitsPerSecond(Math.min(camera.height || 1080, 1080), 30, video.quality);
  }
  return bits / 8;
}

/** "(screen 30 fps + camera)": what the footer's estimate counts. */
export function rateNote(selection: VideoSelection | null, recording: Pick<RecordingSettings, 'video'> | null, tracks: readonly VideoTrackStatus[] | null): string | null {
  const live = tracks?.filter(isLive) ?? null;
  const screenFps = live !== null ? live.find((t) => t.trackId === 'screen')?.frameRate : selection?.screen === true || selection?.window === true ? (recording?.video.frameRate ?? 30) : undefined;
  const camera = live !== null ? live.some((t) => t.trackId === 'camera') : selection?.camera === true;
  const parts = [...(screenFps === undefined ? [] : [`screen ${screenFps} fps`]), ...(camera ? ['camera'] : [])];
  return parts.length === 0 ? null : `(${parts.join(' + ')})`;
}
