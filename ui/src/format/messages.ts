// Copy for the error and recovery surfaces (DESIGN.md §17). Name the thing, give the time or amount,
// say what is safe, then offer the most specific fix. No exclamation marks.
import type {
  ProjectDeleteEstimate,
  RecordingSourceLostPayload,
  RecordingStoppedByHostPayload,
  RecoveredRecording,
  StorageLowSpacePayload,
} from '../bridge/types';
import { formatSpan, formatTimecode, formatDuration } from './duration';
import { formatFreeSpace, formatSize } from './storage';

/** "a", "a and b", "a, b and c". */
export function joinList(items: readonly string[]): string {
  if (items.length <= 1) {
    return items[0] ?? '';
  }
  return `${items.slice(0, -1).join(', ')} and ${items.at(-1) ?? ''}`;
}

export interface DeleteCopy {
  title: string;
  body: string;
}

/** `Delete "Weekly 1:1 with Sam"?` + what is removed, its size, exports untouched, cannot be undone. */
export function deleteCopy(estimate: ProjectDeleteEstimate): DeleteCopy {
  const items = estimate.items.length > 0 ? estimate.items : ['the recording'];
  return {
    title: `Delete "${estimate.title}"?`,
    body: `Removes ${joinList(items)} from this PC (${formatSize(estimate.sizeBytes)}). Exported copies are not affected. This cannot be undone.`,
  };
}

export interface RecoveryCopy {
  title: string;
  /** Rendered bold in `text` before `rest`. */
  lead: string;
  rest: string;
}

/** "Recovered an interrupted recording": what was saved, how many tracks are intact, what may be missing. */
export function recoveryCopy(item: RecoveredRecording): RecoveryCopy {
  const tracks =
    item.tracksIntact >= item.tracksTotal
      ? item.tracksTotal === 1
        ? 'The track is intact.'
        : `All ${item.tracksTotal} tracks are intact.`
      : `${item.tracksIntact} of ${item.tracksTotal} tracks are intact.`;
  const missing = item.mayBeMissingMs >= 1000 ? ` The last ${formatSpan(item.mayBeMissingMs)} may be missing.` : '';
  return {
    title: 'Recovered an interrupted recording',
    lead: item.title,
    rest: ` was saved up to ${formatDuration(item.recoveredDurationMs)} before the recording was interrupted. ${tracks}${missing}`,
  };
}

export interface ToastCopy {
  title: string;
  body: string;
}

/** "Zoom stopped at 00:41:12" + "Shure MV7 and Everything this PC plays are still recording." */
export function sourceLostCopy(payload: RecordingSourceLostPayload): ToastCopy {
  const remaining = payload.remaining;
  const body =
    remaining.length === 0
      ? 'Nothing else is recording. Everything up to that point is saved.'
      : `${joinList(remaining)} ${remaining.length === 1 ? 'is' : 'are'} still recording.`;
  return { title: `${payload.name} stopped at ${formatTimecode(payload.atMs)}`, body };
}

/** The recording ended without the user asking: say when, why and that what came before is saved. */
export function stoppedByHostCopy(payload: RecordingStoppedByHostPayload): ToastCopy {
  const at = formatTimecode(payload.atMs);
  switch (payload.reason) {
    case 'diskFull':
      return {
        title: `Recording stopped at ${at} · drive full`,
        body: 'Everything up to that point is saved and will transcribe once there is room.',
      };
    case 'deviceLost':
      return { title: `Recording stopped at ${at}`, body: `${payload.message} Everything up to that point is saved.` };
    case 'error':
      return { title: `Recording stopped at ${at}`, body: `${payload.message} Everything up to that point is saved.` };
  }
}

export interface BannerCopy {
  lead: string;
  rest: string;
}

/** "Low disk space · 4 GB free." + what continues and what pauses. */
export function lowSpaceCopy(payload: StorageLowSpacePayload): BannerCopy {
  const parts: string[] = [];
  if (payload.recordingContinues) {
    parts.push('Recording continues.');
  }
  if (payload.transcriptionPaused) {
    parts.push('Transcription is paused until there is room.');
  }
  if (parts.length === 0) {
    parts.push('Everything already recorded is safe.');
  }
  return { lead: `Low disk space · ${formatFreeSpace(payload.freeBytes)} free.`, rest: ` ${parts.join(' ')}` };
}
