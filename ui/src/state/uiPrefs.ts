// Small interface preferences remembered on this PC for this Windows user (the page's local storage
// lives in Memento's WebView2 profile under the user's AppData). Nothing here is sent anywhere or
// written to the library; a storage error just means the default is used.
import { useState } from 'preact/hooks';
import type { TranscriptCopyFormat, TranscriptLayout, TranscriptTextOptions } from '../bridge/types';

const PREFIX = 'memento.ui.';

export type UiFlag = 'review.skipSilences';

/** Preferences kept as JSON (after 1.2.0). */
export type UiPref = 'export.transcriptText' | 'copy.transcriptFormat';

export function readUiFlag(key: UiFlag, fallback: boolean): boolean {
  try {
    const value = globalThis.localStorage.getItem(PREFIX + key);
    return value === null ? fallback : value === '1';
  } catch {
    return fallback;
  }
}

export function writeUiFlag(key: UiFlag, value: boolean): void {
  try {
    globalThis.localStorage.setItem(PREFIX + key, value ? '1' : '0');
  } catch {
    // Storage is off or full: the choice lasts until the screen closes.
  }
}

/** A remembered on/off preference: read once, written on every change. */
export function useUiFlag(key: UiFlag, fallback: boolean): [boolean, (value: boolean) => void] {
  const [value, setValue] = useState(() => readUiFlag(key, fallback));
  return [
    value,
    (next) => {
      setValue(next);
      writeUiFlag(key, next);
    },
  ];
}

/** A remembered JSON preference; `parse` returns null for anything it does not recognise, and the fallback is used. */
export function readUiPref<T>(key: UiPref, fallback: T, parse: (value: unknown) => T | null): T {
  try {
    const raw = globalThis.localStorage.getItem(PREFIX + key);
    return raw === null ? fallback : (parse(JSON.parse(raw)) ?? fallback);
  } catch {
    return fallback;
  }
}

export function writeUiPref(key: UiPref, value: unknown): void {
  try {
    globalThis.localStorage.setItem(PREFIX + key, JSON.stringify(value));
  } catch {
    // Storage is off or full: the choice lasts until the screen closes.
  }
}

/** The transcript text options as earlier versions wrote the files: timestamps, speakers, each format's own layout. */
export const DEFAULT_TRANSCRIPT_TEXT: TranscriptTextOptions = { timestamps: true, speakers: true, layout: 'auto' };

const LAYOUTS: readonly TranscriptLayout[] = ['auto', 'turns', 'lines'];

export function parseTranscriptText(value: unknown): TranscriptTextOptions | null {
  if (typeof value !== 'object' || value === null) {
    return null;
  }
  const { timestamps, speakers, layout } = value as Record<string, unknown>;
  return typeof timestamps === 'boolean' && typeof speakers === 'boolean' && typeof layout === 'string' && (LAYOUTS as readonly string[]).includes(layout)
    ? { timestamps, speakers, layout: layout as TranscriptLayout }
    : null;
}

/** Timestamps, speakers and layout of transcript exports and copies, remembered per user (BRIDGE.md, after 1.2.0). */
export function readTranscriptText(): TranscriptTextOptions {
  return readUiPref('export.transcriptText', DEFAULT_TRANSCRIPT_TEXT, parseTranscriptText);
}

export function writeTranscriptText(options: TranscriptTextOptions): void {
  writeUiPref('export.transcriptText', options);
}

/** The text form the last transcript copy used ("Copy" in Review's filter line uses it). */
export function readCopyFormat(): TranscriptCopyFormat {
  return readUiPref<TranscriptCopyFormat>('copy.transcriptFormat', 'text', (v) => (v === 'text' || v === 'markdown' ? v : null));
}

export function writeCopyFormat(format: TranscriptCopyFormat): void {
  writeUiPref('copy.transcriptFormat', format);
}
