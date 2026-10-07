// Export wording and the folder an export is written to (DESIGN.md §15, BRIDGE.md M3). The host
// names the subfolder the same way; the dialog only previews it.
import type { AudioExportFormat, ExportComponent, ExportEstimate, ExportSelection, TranscriptExportFormat } from '../bridge/types';
import { formatSize } from './storage';

// Characters Windows does not allow in a file name, and control characters.
// eslint-disable-next-line no-control-regex
const INVALID = /[<>:"/\\|?*\u0000-\u001f]/g;

/**
 * "Design review: library screen" recorded on 5 October 2026 -> "Design review - library screen
 * 2026-10-05". Invalid characters become " - ", runs of spaces and dashes collapse, trailing dots
 * and spaces go (Windows drops them), and the title part is at most 80 characters.
 */
export function exportFolderName(title: string, createdAt: string): string {
  const clean = title
    .replace(INVALID, ' - ')
    .replace(/\s+/g, ' ')
    .replace(/(?:\s-\s*){2,}/g, ' - ')
    .replace(/^[\s.-]+|[\s.-]+$/g, '')
    .slice(0, 80)
    .trim();
  const date = /^\d{4}-\d{2}-\d{2}/.exec(createdAt)?.[0] ?? '';
  const name = [clean === '' ? 'Recording' : clean, date].filter((part) => part !== '').join(' ');
  return name;
}

/** Joins a Windows folder and a name with exactly one backslash. */
export function joinWindowsPath(folder: string, name: string): string {
  return `${folder.replace(/[\\/]+$/, '')}\\${name}`;
}

/** Where the files land: the folder itself, or the recording's folder inside it, with a trailing separator. */
export function exportPathPreview(folder: string, createSubfolder: boolean, title: string, createdAt: string): string {
  const base = createSubfolder ? joinWindowsPath(folder, exportFolderName(title, createdAt)) : folder.replace(/[\\/]+$/, '');
  return `${base}\\`;
}

export const AUDIO_FORMAT_LABELS: Record<AudioExportFormat, string> = { flac: 'FLAC', wav: 'WAV', mp3: 'MP3' };

export const TRANSCRIPT_FORMAT_LABELS: Record<TranscriptExportFormat, string> = {
  json: 'JSON',
  markdown: 'Markdown',
  text: 'Text',
  srt: 'SRT',
};

/** The bitrate an MP3 export uses unless one is chosen. */
export const MP3_EXPORT_KBPS = 192;

/** A selection with every component ticked: the dialog asks for every row's size at once. */
export function everythingOn(selection: ExportSelection): ExportSelection {
  return {
    audioMixed: { ...selection.audioMixed, on: true },
    tracks: { ...selection.tracks, on: true },
    transcript: { ...selection.transcript, on: true },
    // Documents arrive in M4; nothing is exported for them in M3.
    documents: { ...selection.documents, on: false },
    details: { on: true },
    attachments: { on: true },
  };
}

export const EXPORT_COMPONENTS: readonly ExportComponent[] = ['audioMixed', 'tracks', 'transcript', 'documents', 'details', 'attachments'];

/** Is this component ticked in the selection? */
export function isOn(selection: ExportSelection, component: ExportComponent): boolean {
  return selection[component].on;
}

export interface ExportSummary {
  files: number;
  bytes: number;
}

/**
 * The footer's numbers from an estimate of every row: the files and bytes of the ticked rows that
 * are available.
 */
export function summarise(estimate: ExportEstimate, selection: ExportSelection): ExportSummary {
  const unavailable = new Set(estimate.unavailable.map((u) => u.component));
  let files = 0;
  let bytes = 0;
  for (const item of estimate.items) {
    if (isOn(selection, item.component) && !unavailable.has(item.component)) {
      files += 1;
      bytes += item.bytes;
    }
  }
  return { files, bytes };
}

/** "1 file", "4 files". */
export function fileCount(files: number): string {
  return `${files} ${files === 1 ? 'file' : 'files'}`;
}

/** "about 420 MB". */
export function aboutSize(bytes: number): string {
  return `about ${formatSize(bytes)}`;
}
