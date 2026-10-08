// The browser preview's history.links (BRIDGE.md, History links): the host's rule in miniature. A History line
// that changed the transcript or a document belongs to the stored copy that was current at its time; only the
// last line inside a copy opens it.
import type { HistoryEntry, HistoryLink } from './types';

/** A stored copy: the current content ('current') or a kept version, current from `start` until `end`. */
export interface MockCopy {
  kind: HistoryLink['kind'];
  documentId: string | null;
  versionId: string;
  /** ISO time the content came about; null when unknown. */
  start: string | null;
  /** ISO time it was replaced; null for the current content. */
  end: string | null;
}

/** How long after a document's write its line may come. */
const DOCUMENT_LINE_MS = 2 * 60_000;

/** What a line changed and the banner's words, as the host decides them (HistoryLinker.Describe). */
export function describeHistory(entry: HistoryEntry): { kind: HistoryLink['kind']; after: string } | null {
  const s = entry.summary;
  if (entry.stage === 'transcript' && entry.event === 'completed') {
    return { kind: 'transcript', after: 'after it was transcribed' };
  }
  if (entry.stage === 'speakers' && entry.event === 'completed') {
    return { kind: 'transcript', after: 'after speakers were identified' };
  }
  if (entry.stage === 'minutes' && entry.event === 'completed') {
    return { kind: 'document', after: s.includes(' regenerated ') ? 'after it was generated again' : 'after it was generated' };
  }
  if (entry.stage !== 'edited' || entry.event !== 'info') {
    return null;
  }
  const transcript: Record<string, string> = {
    'Transcript edited': 'after a line was edited',
    'Speaker changed': "after a line's speaker was changed",
    'Speaker renamed': 'after a speaker was renamed',
    'Speakers merged': 'after speakers were merged',
    'Speaker restored': 'after a speaker change was undone',
    'Speaker removed': 'after a speaker change was undone',
    'Transcript version restored': 'after an earlier version was restored',
  };
  const known = transcript[s];
  if (known !== undefined) {
    return { kind: 'transcript', after: known };
  }
  if (s.startsWith('Speaker')) {
    return { kind: 'transcript', after: 'after speakers were changed' };
  }
  if (s.startsWith('Document "')) {
    if (s.endsWith('" created as a copy')) return { kind: 'document', after: 'after it was copied' };
    if (s.endsWith('" created')) return { kind: 'document', after: 'after it was created' };
    if (s.endsWith('" edited')) return { kind: 'document', after: 'after it was edited' };
    if (s.endsWith('" restored')) return { kind: 'document', after: 'after an earlier version was restored' };
  }
  return null;
}

export function linkHistory(entries: readonly HistoryEntry[], copies: readonly MockCopy[]): HistoryLink[] {
  const found: { index: number; kind: HistoryLink['kind']; copy: MockCopy | null; after: string }[] = [];
  entries.forEach((entry, index) => {
    const change = describeHistory(entry);
    if (change === null) {
      return;
    }
    const at = Date.parse(entry.at);
    let copy: MockCopy | null = null;
    for (const c of copies) {
      if (c.kind !== change.kind || (c.start !== null && Date.parse(c.start) > at) || (c.end !== null && at >= Date.parse(c.end))) {
        continue;
      }
      if (copy === null || (c.start === null ? -Infinity : Date.parse(c.start)) > (copy.start === null ? -Infinity : Date.parse(copy.start))) {
        copy = c;
      }
    }
    if (copy !== null && change.kind === 'document' && copy.start !== null && at - Date.parse(copy.start) > DOCUMENT_LINE_MS) {
      copy = null;
    }
    found.push({ index, kind: change.kind, copy, after: change.after });
  });
  const last = new Map<MockCopy, number>();
  for (const f of found) {
    if (f.copy !== null) {
      last.set(f.copy, f.index);
    }
  }
  return found.map((f) => ({
    index: f.index,
    kind: f.kind,
    documentId: f.copy?.documentId ?? null,
    versionId: f.copy !== null && last.get(f.copy) === f.index ? f.copy.versionId : null,
    after: f.after,
  }));
}
