// 2.0 video in Settings › Recording (DESIGN.md §18.5, renders/SettingsVideo.dc.html): the Video
// defaults group after Tracks and storage. Every change sends the recording block whole.
import type { JSX } from 'preact';
import type {
  PipCorner,
  VideoDefaultSource,
  VideoFrameRate,
  VideoListSourcesResult,
  VideoQuality,
  VideoResolution,
  VideoSettings,
  VideoSource,
} from '../../bridge/types';
import { Segmented, Toggle } from '../../components/Controls';
import { SelectMenu } from '../../components/Menus';
import { cameraLabel, formatResolution, PIP_CORNERS, qualityDescription, recordedHeight } from '../../format/video';
import { OnOff, SettingsGroup, SettingsRow } from './SettingsParts';

const SOURCE_OPTIONS: readonly { value: VideoDefaultSource; label: string }[] = [
  { value: 'screenAndCamera', label: 'Screen + camera' },
  { value: 'screen', label: 'Screen' },
  { value: 'window', label: 'Window' },
  { value: 'camera', label: 'Camera' },
];

const FRAME_RATES: readonly { value: `${VideoFrameRate}`; label: string }[] = [
  { value: '24', label: '24' },
  { value: '30', label: '30' },
  { value: '60', label: '60' },
];

const QUALITIES: readonly { value: VideoQuality; label: string }[] = [
  { value: 'smaller', label: 'Smaller' },
  { value: 'balanced', label: 'Balanced' },
  { value: 'best', label: 'Best' },
];

/** The height a camera is recorded at (cameras are kept at 1080p at most, like the session does). */
const CAMERA_MAX_HEIGHT = 1080;

/** "Match display (2560 × 1440)" with the primary display, else without figures. */
export function resolutionOptions(primary: VideoSource | null): { value: VideoResolution; label: string }[] {
  return [
    { value: 'display', label: primary === null ? 'Match display' : `Match display (${formatResolution(primary.width, primary.height)})` },
    { value: '1080p', label: '1080p' },
    { value: '720p', label: '720p' },
  ];
}

/** The camera the defaults use: the chosen one, else the first one found. */
export function defaultCamera(video: VideoSettings, cameras: readonly VideoSource[]): VideoSource | null {
  return cameras.find((c) => c.id === video.defaultCameraId) ?? (video.defaultCameraId === null ? (cameras[0] ?? null) : null);
}

/**
 * The height the Quality description speaks of: the camera's for Camera, else the primary display's at
 * the chosen resolution; 1080 when no display is known (the figure then errs on neither side much).
 */
export function qualityHeight(video: VideoSettings, primary: VideoSource | null, camera: VideoSource | null): number {
  if (video.defaultSource === 'camera' && camera !== null) {
    return Math.min(recordedHeight(camera, video.resolution), CAMERA_MAX_HEIGHT);
  }
  if (primary !== null) {
    return recordedHeight(primary, video.resolution);
  }
  return video.resolution === '720p' ? 720 : 1080;
}

interface VideoDefaultsProps {
  video: VideoSettings;
  /** null while video.listSources has not answered. */
  sources: VideoListSourcesResult | null;
  save: (video: VideoSettings) => void;
}

export function VideoDefaultsGroup({ video, sources, save }: VideoDefaultsProps): JSX.Element {
  const primary = sources?.displays.find((d) => d.primary) ?? sources?.displays[0] ?? null;
  const cameras = sources?.cameras ?? [];
  const camera = defaultCamera(video, cameras);
  const change = (next: Partial<VideoSettings>): void => {
    save({ ...video, ...next });
  };
  const cameraOptions = [
    ...cameras.map((c) => ({ value: c.id, label: cameraLabel(c) })),
    // A chosen camera that is not connected now stays chosen, and says so.
    ...(video.defaultCameraId !== null && camera === null ? [{ value: video.defaultCameraId, label: 'Not connected now' }] : []),
  ];

  return (
    <SettingsGroup label="Video defaults">
      <SettingsRow label="Default video source" description="What Audio + video captures unless you change it in the session.">
        <SelectMenu<VideoDefaultSource>
          label="Default video source"
          value={video.defaultSource}
          options={SOURCE_OPTIONS}
          onChange={(defaultSource) => {
            change({ defaultSource });
          }}
        />
      </SettingsRow>
      <SettingsRow label="Resolution" description="Recording at the display’s own resolution avoids scaling.">
        <SelectMenu<VideoResolution>
          label="Resolution"
          value={video.resolution}
          options={resolutionOptions(primary)}
          onChange={(resolution) => {
            change({ resolution });
          }}
        />
      </SettingsRow>
      <SettingsRow label="Frame rate" description="Higher is smoother and roughly doubles the file size.">
        <Segmented<`${VideoFrameRate}`>
          label="Frame rate"
          value={`${video.frameRate}`}
          options={FRAME_RATES}
          onChange={(value) => {
            change({ frameRate: Number(value) as VideoFrameRate });
          }}
        />
      </SettingsRow>
      <SettingsRow label="Quality" description={qualityDescription(video.quality, qualityHeight(video, primary, camera), video.frameRate)}>
        <Segmented<VideoQuality>
          label="Quality"
          value={video.quality}
          options={QUALITIES}
          onChange={(quality) => {
            change({ quality });
          }}
        />
      </SettingsRow>
      <SettingsRow
        label="Default camera"
        description={
          sources === null
            ? 'Looking for cameras…'
            : cameras.length === 0
              ? 'No camera was found on this PC. Connect one and open Settings again; screen recordings work without it.'
              : 'Used when Camera is enabled.'
        }
      >
        {sources !== null && cameras.length === 0 ? (
          <span class="settings-value">None found</span>
        ) : (
          <SelectMenu<string>
            label="Default camera"
            value={camera?.id ?? video.defaultCameraId ?? ''}
            options={cameraOptions}
            {...(sources === null ? { buttonText: '…' } : {})}
            disabled={sources === null}
            onChange={(defaultCameraId) => {
              change({ defaultCameraId });
            }}
          />
        )}
      </SettingsRow>
      <SettingsRow label="Picture-in-picture position" description="Where the camera sits on the screen recording.">
        <SelectMenu<PipCorner>
          label="Picture-in-picture position"
          value={video.pipCorner}
          options={PIP_CORNERS.map((c) => ({ value: c.corner, label: c.name }))}
          onChange={(pipCorner) => {
            change({ pipCorner });
          }}
        />
      </SettingsRow>
      <SettingsRow label="Hardware encoder" description="Uses the GPU to encode. Falls back to software automatically if it fails, with a notice.">
        <OnOff on={video.hardwareEncoder} />
        <Toggle
          label="Hardware encoder"
          checked={video.hardwareEncoder}
          onChange={(hardwareEncoder) => {
            change({ hardwareEncoder });
          }}
        />
      </SettingsRow>
      <SettingsRow label="Include the mouse pointer" description="Clicks are not highlighted.">
        <OnOff on={video.includePointer} />
        <Toggle
          label="Include the mouse pointer"
          checked={video.includePointer}
          onChange={(includePointer) => {
            change({ includePointer });
          }}
        />
      </SettingsRow>
    </SettingsGroup>
  );
}
