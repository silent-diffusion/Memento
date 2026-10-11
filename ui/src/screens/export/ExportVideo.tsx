// 2.0 video in the Export dialog (DESIGN.md §18.4, renders/ExportVideo.dc.html): the Video row's
// description and, while it is ticked, the sub-row with One file | Two files and the note.
import type { JSX } from 'preact';
import type { ExportVideoChoice, ProjectVideo, VideoExportLayout } from '../../bridge/types';
import { Segmented } from '../../components/Controls';
import { formatFrameRate, formatResolution, rolesLabel } from '../../format/video';
import './export-video.css';

/** Does the recording have both a screen and a camera track (and so a choice of one or two files)? */
export function hasScreenAndCamera(video: ProjectVideo | null | undefined): boolean {
  const tracks = video?.tracks ?? [];
  return tracks.some((t) => t.id === 'screen') && tracks.some((t) => t.id === 'camera');
}

/**
 * "Screen and camera, 2560 × 1440 · 30 fps, with the mixed audio", "Screen, 1920 × 1080 · 30 fps, with
 * the mixed audio": the tracks, then the main track's picture (the screen's, else the camera's).
 * null when the recording has no video tracks.
 */
export function videoRowDescription(video: ProjectVideo | null | undefined): string | null {
  const tracks = video?.tracks ?? [];
  const main = tracks.find((t) => t.id === 'screen') ?? tracks.find((t) => t.id === 'camera');
  if (main === undefined) {
    return null;
  }
  const what = hasScreenAndCamera(video) ? 'Screen and camera' : rolesLabel(tracks.map((t) => t.id));
  return `${what}, ${formatResolution(main.width, main.height)} · ${formatFrameRate(main.frameRate)}, with the mixed audio`;
}

const LAYOUT_OPTIONS: readonly { value: VideoExportLayout; label: string }[] = [
  { value: 'oneFile', label: 'One file, as recorded' },
  { value: 'twoFiles', label: 'Two files: screen and camera' },
];

export const FORMAT_NOTE = 'MP4 re-encodes; "Original container" copies the recorded frames untouched.';
export const ONE_FILE_NOTE = 'The camera inset is burned in where it was while recording.';
export const TWO_FILES_NOTE = 'Each file keeps its own resolution; audio goes with the screen file.';

/** What a format or layout choice changed in the other one, said once beside the note. */
export type VideoSwitch = 'toTwoFiles' | 'toMp4' | null;

export const SWITCH_NOTES: Record<Exclude<VideoSwitch, null>, string> = {
  toTwoFiles: 'Original container keeps each track as recorded, so it writes two files.',
  toMp4: 'One file is composed, so it is written as MP4.',
};

interface VideoOptionsProps {
  choice: ExportVideoChoice;
  /** Both tracks: the One file | Two files choice is offered. */
  both: boolean;
  switched: VideoSwitch;
  onLayout: (layout: VideoExportLayout) => void;
}

/** The sub-row under a ticked Video row (the Documents sub-row's look). */
export function VideoOptionsRow({ choice, both, switched, onLayout }: VideoOptionsProps): JSX.Element {
  const note = both ? `${choice.layout === 'oneFile' ? ONE_FILE_NOTE : TWO_FILES_NOTE} ${FORMAT_NOTE}` : FORMAT_NOTE;
  return (
    <div class="export-video-options">
      {both ? <Segmented<VideoExportLayout> label="Screen and camera" value={choice.layout} options={LAYOUT_OPTIONS} onChange={onLayout} /> : null}
      <span class="export-video-note" id="exp-video-note">
        {note}
        {/* Always present, so a screen reader hears the sentence when a choice changes the other one. */}
        <span class="export-video-switched" role="status">
          {switched === null ? '' : ` ${SWITCH_NOTES[switched]}`}
        </span>
      </span>
    </div>
  );
}
