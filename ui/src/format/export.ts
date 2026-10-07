// Export wording and the folder an export is written to (DESIGN.md §15, BRIDGE.md M3). The host
// names the subfolder the same way; the dialog only previews it.
import type { AudioExportFormat, ExportComponent, ExportEstimate, ExportSelection, TranscriptExportFormat } from '../bridge/types';

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

const RESERVED = /^(con|prn|aux|nul|com[1-9]|lpt[1-9])$/i;

/**
 * A name made safe as a Windows file name, as the host's FileNames.Sanitize: forbidden and control
 * characters become spaces, runs of spaces collapse, leading and trailing dots and spaces go, at most
 * 80 characters, never a device name ("con" -> "_con"); `fallback` when nothing is left.
 */
export function safeFileStem(name: string, fallback: string): string {
  // eslint-disable-next-line no-control-regex
  const spaced = name.normalize('NFC').replace(/[<>:"/\\|?*\u0000-\u001f\u007f-\u009f]/g, ' ');
  let text = spaced.replace(/\s+/g, ' ').trim().replace(/^\.+|\.+$/g, '').trim();
  if (text.length > 80) {
    text = text.slice(0, 80).trimEnd().replace(/\.+$/, '');
  }
  if (text === '') {
    return fallback;
  }
  return RESERVED.test(text.split('.')[0] ?? '') ? `_${text}` : text;
}

const TRANSCRIPT_SUFFIX: Record<TranscriptExportFormat, string> = {
  json: ' - transcript.json',
  markdown: ' - transcript.md',
  text: ' - transcript.txt',
  srt: '.srt',
};

/**
 * The names of exported files, as the host writes them (BRIDGE.md M3 integration clarification 16);
 * `base` is {@link exportFolderName}. Attachments are listed under `Attachments/`.
 */
export const exportFileNames = {
  mix: (base: string, format: AudioExportFormat): string => `${base}.${format}`,
  track: (base: string, trackName: string, trackId: string, format: AudioExportFormat): string => `${base} - ${safeFileStem(trackName, trackId)}.${format}`,
  transcript: (base: string, format: TranscriptExportFormat): string => base + TRANSCRIPT_SUFFIX[format],
  details: (base: string): string => `${base} - details.json`,
  attachment: (name: string): string => `Attachments/${name}`,
};

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
 * What the host's estimate adds beyond its items: manifest.json, written beside every export.
 * The host's `files` and `bytes` count it; the items do not list it.
 */
export function manifestShare(estimate: ExportEstimate): ExportSummary {
  const itemBytes = estimate.items.reduce((sum, item) => sum + item.bytes, 0);
  return { files: Math.max(0, estimate.files - estimate.items.length), bytes: Math.max(0, estimate.bytes - itemBytes) };
}

/**
 * The footer's numbers from an estimate of every row: the files and bytes of the ticked rows that
 * are available, plus the manifest when anything is written (BRIDGE.md M3: totals include it).
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
  if (files === 0) {
    return { files, bytes };
  }
  const manifest = manifestShare(estimate);
  return { files: files + manifest.files, bytes: bytes + manifest.bytes };
}

/** "1 file", "4 files". */
export function fileCount(files: number): string {
  return `${files} ${files === 1 ? 'file' : 'files'}`;
}
