// Review's video wording (DESIGN.md §18.3, §18.6, §17): the header meta's end, the Audio only line for a
// removed video, the outline's Video facts, the details pane's Video row and the Remove video dialog.
// Every figure in plain numbers (§18).
import type { ProjectVideo, ProjectVideoTrack, VideoRole } from '../../bridge/types';
import { formatSize } from '../../format/storage';
import { formatFrameRate, formatResolution, rolesLabel, rolesWording } from '../../format/video';
import { formatShortDate, parseIso } from '../../format/when';
import { videoLayers } from './videoSync';

function rolesOf(video: Pick<ProjectVideo, 'tracks'>): VideoRole[] {
  return [...new Set(video.tracks.map((t) => t.id))];
}

/** The header meta's last part: "screen + camera · 1.9 GB video", "video removed"; null when there is nothing to say. */
export function metaVideoTail(video: ProjectVideo): string | null {
  if (video.removed !== null) {
    return 'video removed';
  }
  if (video.tracks.length === 0) {
    return null;
  }
  return `${rolesWording(rolesOf(video))} · ${formatSize(video.sizeBytes)} video`;
}

/** The pressed-in line in place of a removed video. */
export function removedLine(removed: NonNullable<ProjectVideo['removed']>, now: Date): string {
  return `Video was removed on ${formatShortDate(parseIso(removed.at), now)} to free ${formatSize(removed.bytesFreed)}. Audio tracks, transcript and documents are intact.`;
}

export const HIDDEN_LINE = 'Video hidden. Playback and the transcript are unchanged.';

/** The outline's Video block: Sources, Resolution, Frame rate, Size (of the screen track, else the camera). */
export function outlineFacts(video: ProjectVideo): { label: string; value: string; mono: boolean }[] {
  const { main } = videoLayers(video);
  const facts = [{ label: 'Sources', value: rolesLabel(rolesOf(video)), mono: false }];
  if (main !== null) {
    facts.push({ label: 'Resolution', value: formatResolution(main.width, main.height), mono: true });
    facts.push({ label: 'Frame rate', value: formatFrameRate(main.frameRate), mono: true });
  }
  facts.push({ label: 'Size', value: formatSize(video.sizeBytes), mono: true });
  return facts;
}

function dropped(count: number): string {
  return `${count} dropped ${count === 1 ? 'frame' : 'frames'}`;
}

/** The details pane's Video row: "2560 × 1440 · 30 fps · hardware encoder · 0 dropped frames". */
export function detailsVideoRow(video: ProjectVideo): string | null {
  const { main } = videoLayers(video);
  if (main === null) {
    return null;
  }
  const hardware = video.tracks.every((t) => t.hardware);
  const lost = video.tracks.reduce((sum, t) => sum + t.framesDropped, 0);
  return [formatResolution(main.width, main.height), formatFrameRate(main.frameRate), `${hardware ? 'hardware' : 'software'} encoder`, dropped(lost)].join(' · ');
}

/** The Video row once the video was removed. */
export function detailsRemovedRow(removed: NonNullable<ProjectVideo['removed']>, now: Date): string {
  return `Removed on ${formatShortDate(parseIso(removed.at), now)} · ${formatSize(removed.bytesFreed)} freed`;
}

/** "Screen", "Camera": the names the Tracks row lists before the audio tracks. */
export function videoTrackNames(tracks: readonly Pick<ProjectVideoTrack, 'id'>[]): string[] {
  return (['screen', 'camera'] as const).filter((role) => tracks.some((t) => t.id === role)).map((role) => (role === 'screen' ? 'Screen' : 'Camera'));
}

export const NEVER_SENT_NOTE = 'Video is never transcribed or sent anywhere. Documents are generated from the audio transcript and details only.';

/** The §18.6 Remove video dialog: names the recording and the size freed, says what is kept and that it cannot be undone. */
export function removeVideoCopy(title: string, video: ProjectVideo): { title: string; body: string } {
  const which = (['screen', 'camera'] as const).filter((role) => video.tracks.some((t) => t.id === role)).join(' and ');
  return {
    title: `Remove the video of "${title}"?`,
    body: `Deletes the ${which === '' ? '' : `${which} `}video from this PC and frees ${formatSize(video.sizeBytes)}. The audio tracks, transcript and documents are kept. This cannot be undone.`,
  };
}

/** The §17 toast when Remove video fails: what happened, then what is safe. */
export function removeFailedBody(message: string): string {
  return `${message} The video, audio tracks and transcript are as they were.`;
}
