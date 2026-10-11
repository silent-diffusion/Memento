// Video figures in plain numbers (DESIGN.md §18: "every video figure is shown in plain numbers"):
// resolutions, frame rates, bitrates, the sources a recording has, the inset corners, and the
// bitrate a quality setting asks the encoder for (ENGINE-NOTES.md §M.3, the busy-content ceilings).
import type { PipCorner, VideoFrameRate, VideoQuality, VideoRole, VideoSettings, VideoSource } from '../bridge/types';

/** Settings › Recording › Video defaults of a new install (BRIDGE.md additions, Settings). */
export const DEFAULT_VIDEO_SETTINGS: VideoSettings = {
  defaultSource: 'screenAndCamera',
  resolution: 'display',
  frameRate: 30,
  quality: 'balanced',
  defaultCameraId: null,
  pipCorner: 'bottomRight',
  hardwareEncoder: true,
  includePointer: true,
};

/** The recording block's video fields, with the defaults where a host before 2.0 sends none. */
export function videoSettingsOf(recording: { captureVideo?: boolean; video?: VideoSettings } | null | undefined): { captureVideo: boolean; video: VideoSettings } {
  return { captureVideo: recording?.captureVideo ?? false, video: recording?.video ?? DEFAULT_VIDEO_SETTINGS };
}

/** "2560 × 1440". */
export function formatResolution(width: number, height: number): string {
  return `${Math.round(width)} × ${Math.round(height)}`;
}

/** "1440p", "1080p". */
export function heightLabel(height: number): string {
  return `${Math.round(height)}p`;
}

/** "30 fps". */
export function formatFrameRate(fps: number): string {
  return `${Number.isInteger(fps) ? fps : fps.toFixed(1)} fps`;
}

/** "12 Mb/s", "7.8 Mb/s", "850 kb/s". */
export function formatBitrate(bitsPerSecond: number): string {
  const safe = Number.isFinite(bitsPerSecond) && bitsPerSecond > 0 ? bitsPerSecond : 0;
  if (safe >= 10_000_000) {
    return `${Math.round(safe / 1_000_000)} Mb/s`;
  }
  if (safe >= 1_000_000) {
    const tenths = Math.round(safe / 100_000) / 10;
    return `${Number.isInteger(tenths) ? tenths.toFixed(0) : tenths.toFixed(1)} Mb/s`;
  }
  return `${Math.round(safe / 1000)} kb/s`;
}

/** "Hardware · GPU" or "Software · CPU": the Encoder card's pill. */
export function encoderKind(hardware: boolean): string {
  return hardware ? 'Hardware · GPU' : 'Software · CPU';
}

/** The order of the inset corner buttons in their 2 × 2 grid, with their accessible names. */
export const PIP_CORNERS: readonly { corner: PipCorner; name: string }[] = [
  { corner: 'topLeft', name: 'Top left' },
  { corner: 'topRight', name: 'Top right' },
  { corner: 'bottomLeft', name: 'Bottom left' },
  { corner: 'bottomRight', name: 'Bottom right' },
];

export function cornerName(corner: PipCorner): string {
  return PIP_CORNERS.find((c) => c.corner === corner)?.name ?? 'Bottom right';
}

/** "screen + camera", "screen", "camera": the parts of a sub-line. */
export function rolesWording(roles: readonly VideoRole[]): string {
  const ordered = (['screen', 'camera'] as const).filter((r) => roles.includes(r));
  return ordered.join(' + ');
}

/** "Screen + camera", "Screen", "Camera": a label at the start of a line. */
export function rolesLabel(roles: readonly VideoRole[]): string {
  const words = rolesWording(roles);
  return words === '' ? 'Video' : `${words.charAt(0).toLocaleUpperCase()}${words.slice(1)}`;
}

/** What a camera offers: "1080p" for a 1920 × 1080 camera. */
export function cameraLabel(source: Pick<VideoSource, 'name' | 'height'>): string {
  // A camera's size is not known without opening it: the host lists 0 (BRIDGE.md, VideoSource).
  return source.height > 0 ? `${source.name} · ${heightLabel(source.height)}` : source.name;
}

/** Mean bitrate of busy 30 fps content at each height, at Balanced (ENGINE-NOTES.md §M.3). */
function balancedAt30(height: number): number {
  if (height <= 720) {
    return 4_200_000;
  }
  if (height <= 1080) {
    return 7_800_000;
  }
  if (height <= 1440) {
    return 12_000_000;
  }
  return 24_000_000;
}

const QUALITY_FACTOR: Record<VideoQuality, number> = { smaller: 0.5, balanced: 1, best: 20 / 12 };
const FPS_FACTOR: Record<VideoFrameRate, number> = { 24: 0.85, 30: 1, 60: 1.68 };

/**
 * The mean bitrate the encoder is asked for: the ceiling a busy picture reaches. Screens are mostly
 * still and use far less (§M.3), so estimates from this figure err on the safe side.
 */
export function targetBitsPerSecond(height: number, frameRate: number, quality: VideoQuality): number {
  const fps: VideoFrameRate = frameRate === 24 || frameRate === 60 ? frameRate : 30;
  return Math.round(balancedAt30(height) * QUALITY_FACTOR[quality] * FPS_FACTOR[fps]);
}

/** The height a setting records a source at: its own, or 1080p / 720p scaled down (never up). */
export function recordedHeight(source: Pick<VideoSource, 'height'>, resolution: VideoSettings['resolution']): number {
  switch (resolution) {
    case 'display':
      return source.height;
    case '1080p':
      return Math.min(source.height, 1080);
    case '720p':
      return Math.min(source.height, 720);
  }
}

/** "Balanced is about 12 Mb/s at 1440p": the Quality row's description (DESIGN.md §18.5). */
export function qualityDescription(quality: VideoQuality, height: number, frameRate: VideoFrameRate): string {
  const name = quality === 'smaller' ? 'Smaller' : quality === 'best' ? 'Best' : 'Balanced';
  return `${name} is about ${formatBitrate(targetBitsPerSecond(height, frameRate, quality))} at ${heightLabel(height)}.`;
}
