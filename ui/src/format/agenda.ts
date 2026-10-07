// Agenda helpers (DESIGN.md §8, §14): splitting pasted text into items locally, the covered /
// current / upcoming marks on the Recording session, and reordering in the Details sheet.
import type { AgendaItem } from '../bridge/types';

// Leading list markers people paste: "1.", "1)", "(1)", "1.2", "12:", "a.", "b)", "iv.", "-", "*",
// "•", "–", "#" headings and "[ ]" / "[x]" checkboxes. Applied repeatedly so "1. - Budget" loses
// both. A bare number or a time of day ("10 minute break", "10:30 Coffee") is content, not a marker.
const MARKERS = [
  /^\(?\d{1,3}(?:(?:\.\d{1,3})+\.?|[.)\]])(?:\s+|$)/,
  /^\d{1,3}[.)](?=[^\d\s])/,
  /^\d{1,3}:\s+/,
  /^\(?[a-z][.)]\s+/i,
  /^\(?[ivxlc]{1,6}[.)]\s+/i,
  /^[•·‣◦▪–—]\s*/,
  /^[-*+>](?=\s|$|[^\d\s\-*])\s*/,
  /^#{1,6}\s+/,
  /^\[[ xX]?\]\s*/,
];

/** Removes leading list markers ("1.", "-", "•", "[ ]") from one line. */
export function stripMarkers(line: string): string {
  let text = line.trim();
  for (let pass = 0; pass < 4; pass++) {
    const before = text;
    for (const marker of MARKERS) {
      text = text.replace(marker, '').trim();
    }
    if (text === before) {
      break;
    }
  }
  return text;
}

/**
 * One agenda item per non-empty line, with numbering and bullets removed. Handles Windows, old Mac
 * and Unix line endings. Lines that are only a marker ("-", "3.") are dropped.
 */
export function splitPastedAgenda(text: string): string[] {
  return text
    .split(/\r\n|\r|\n/)
    .map(stripMarkers)
    .filter((line) => line !== '');
}

let itemCounter = 0;

/** A new agenda item; ids only need to be unique within the recording. */
export function newAgendaItem(text: string): AgendaItem {
  itemCounter += 1;
  return {
    id: `agenda-${Date.now().toString(36)}-${itemCounter.toString(36)}`,
    text,
    covered: false,
    uncertain: false,
    uncertainReason: null,
  };
}

export type AgendaMark = 'covered' | 'current' | 'upcoming';

/** Covered items are done; the first one not covered is current; the rest are upcoming. */
export function agendaMarks(items: readonly AgendaItem[]): AgendaMark[] {
  let currentSeen = false;
  return items.map((item) => {
    if (item.covered) {
      return 'covered';
    }
    if (!currentSeen) {
      currentSeen = true;
      return 'current';
    }
    return 'upcoming';
  });
}

/** Moves the item at `from` to `to` (both clamped); returns a new array. */
export function moveItem<T>(items: readonly T[], from: number, to: number): T[] {
  const next = [...items];
  if (from < 0 || from >= next.length) {
    return next;
  }
  const [moved] = next.splice(from, 1);
  if (moved === undefined) {
    return next;
  }
  next.splice(Math.max(0, Math.min(next.length, to)), 0, moved);
  return next;
}

/** "Pasted text · parsed on this PC", "From agenda.docx · parsed on this PC". */
export function agendaSourceCaption(source: string | null, parsedLocally: boolean): string {
  const where = parsedLocally ? 'parsed on this PC' : 'extracted with AI';
  if (source === null || source === '') {
    return `Pasted text · ${where}`;
  }
  return `From ${source} · ${where}`;
}
