// Wording for documents, templates, styles and providers (DESIGN.md §10, §12, §13, §17).
import type { DocumentVersionReason, InputSelection, ProviderId, StyleSettings } from '../bridge/types';
import { formatSize } from './storage';
import { calendarDaysBetween, formatClock, formatShortDate, parseIso } from './when';

/**
 * "Generate minutes": the last word of the template's name ("Meeting minutes", "Interview notes"). Only words made of
 * letters count, so a copy's "(copy)" or a number does not become the noun ("Meeting minutes (copy)" → "minutes").
 */
export function documentWord(templateName: string): string {
  const words = templateName
    .replace(/\([^)]*\)|\[[^\]]*\]/g, ' ')
    .trim()
    .split(/\s+/)
    .filter((w) => /^\p{L}[\p{L}'’-]*$/u.test(w));
  const last = words[words.length - 1];
  return last === undefined ? 'document' : last.toLocaleLowerCase();
}

export const PROVIDER_SHORT: Record<ProviderId, string> = { anthropic: 'Claude', openai: 'ChatGPT', local: 'Local model' };

/** "Claude (Anthropic)", "Local model (this PC)". */
export const PROVIDER_FULL: Record<ProviderId, string> = {
  anthropic: 'Claude (Anthropic)',
  openai: 'ChatGPT (OpenAI)',
  local: 'Local model (this PC)',
};

export const INPUT_KEYS: readonly (keyof InputSelection)[] = ['transcript', 'details', 'participants', 'agenda', 'highlights', 'attachments', 'previousDocuments'];

/** The Builder's checklist names (§5.9). */
export const INPUT_NAMES: Record<keyof InputSelection, string> = {
  transcript: 'Transcript',
  details: 'Recording details',
  participants: 'Participants',
  agenda: 'Agenda',
  highlights: 'Highlights and notes',
  attachments: 'Imported documents',
  previousDocuments: 'Earlier documents',
};

/** "How this was made" pills. */
export const INPUT_PILLS: Record<keyof InputSelection, string> = {
  transcript: 'Transcript',
  details: 'Details',
  participants: 'Participants',
  agenda: 'Agenda',
  highlights: 'Highlights',
  attachments: 'Imported documents',
  previousDocuments: 'Earlier documents',
};

export function inputsUsed(inputs: InputSelection): (keyof InputSelection)[] {
  return INPUT_KEYS.filter((key) => inputs[key]);
}

/** "38 KB in 1 chunk". */
export function payloadSize(bytes: number, chunks: number): string {
  return `${formatSize(bytes)} in ${chunks} ${chunks === 1 ? 'chunk' : 'chunks'}`;
}

/** "just now", "12 min ago", "Today, 4:00 PM", "Yesterday, 5:14 PM", "Oct 5, 5:14 PM". */
export function whenWords(iso: string, now: Date): string {
  const date = parseIso(iso);
  const minutes = Math.floor((now.getTime() - date.getTime()) / 60_000);
  if (minutes < 1) {
    return 'just now';
  }
  if (minutes < 60) {
    return `${minutes} min ago`;
  }
  const days = calendarDaysBetween(date, now);
  const prefix = days <= 0 ? 'Today' : days === 1 ? 'Yesterday' : formatShortDate(date, now);
  return `${prefix}, ${formatClock(date)}`;
}

/** The same in the middle of a sentence ("generated yesterday, 5:14 PM"). */
export function whenInline(iso: string, now: Date): string {
  const words = whenWords(iso, now);
  return words.startsWith('Today') || words.startsWith('Yesterday') ? words.charAt(0).toLocaleLowerCase() + words.slice(1) : words;
}

/** "38 s", "1 min 12 s", "3 min". */
export function shortDuration(ms: number): string {
  const seconds = Math.max(1, Math.round(ms / 1000));
  if (seconds < 60) {
    return `${seconds} s`;
  }
  const minutes = Math.floor(seconds / 60);
  const rest = seconds % 60;
  return rest === 0 ? `${minutes} min` : `${minutes} min ${rest} s`;
}

export const VERSION_REASONS: Record<DocumentVersionReason, string> = {
  generated: 'Generated',
  edited: 'Edited by you',
  restored: 'Restored',
  regenerated: 'Regenerated',
};

/** The paper size's name in captions. */
export function paperName(paper: StyleSettings['paper']): string {
  return paper === 'a4' ? 'A4' : 'Letter';
}

/** "1 module", "9 modules". */
export function moduleWords(count: number): string {
  return `${count} ${count === 1 ? 'module' : 'modules'}`;
}

/** "1 row", "6 rows". */
export function rowWords(count: number): string {
  return `${count} ${count === 1 ? 'row' : 'rows'}`;
}
