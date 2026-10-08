// Review's History: which lines open a stored version (history.links), and the words for it (DESIGN.md §9, §17).
import type { HistoryLink } from '../bridge/types';
import { calendarDaysBetween, formatClock, formatShortDate, parseIso } from './when';

/** "10:42 AM" today, "Oct 5, 10:42 AM" on another day. */
export function asOfWording(iso: string, now: Date): string {
  const date = parseIso(iso);
  return calendarDaysBetween(date, now) <= 0 ? formatClock(date) : `${formatShortDate(date, now)}, ${formatClock(date)}`;
}

/** The banner's lead: "Transcript as of 10:42 AM, after speakers were identified". */
export function versionTitle(link: Pick<HistoryLink, 'kind' | 'after'>, documentName: string | null, at: string, now: Date): string {
  const what = link.kind === 'transcript' ? 'Transcript' : (documentName ?? 'The document');
  return `${what} as of ${asOfWording(at, now)}, ${link.after}`;
}

/** Index → link, for the History list. */
export function linksByIndex(links: readonly HistoryLink[] | null): Map<number, HistoryLink> {
  return new Map((links ?? []).map((l) => [l.index, l]));
}

/** What hovering a line that opens nothing says. */
export function noVersionNote(link: HistoryLink | undefined): string {
  if (link === undefined) {
    return 'Nothing to open: this did not change the transcript or a document.';
  }
  const what = link.kind === 'transcript' ? 'the transcript' : 'the document';
  return `No copy of ${what} from this moment is kept: it changed again before a version was saved, or its version was removed after the days set in Settings › Documents.`;
}
