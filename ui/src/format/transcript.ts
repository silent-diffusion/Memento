// Transcript helpers (DESIGN.md §5.12, §9): where low-confidence words and search matches fall in a
// segment's text, the playhead's segment, word re-alignment after an edit, talk-time shares, and the
// wording of the transcript stage while it runs.
import type { Speaker, SpeakerColour, StageStatus, TranscriptSegment, TranscriptWord } from '../bridge/types';

/** Below this a speaker assignment is shown as uncertain (dotted ring, "Speaker uncertain"). */
export const UNCERTAIN_SPEAKER = 0.7;

/** A half-open character range [start, end) in a segment's text. */
export interface TextRange {
  start: number;
  end: number;
}

/** One piece of a segment's text with the marks that apply to all of it. */
export interface TextRun {
  text: string;
  /** Inside a word whose confidence is below the threshold. */
  low: boolean;
  /** Inside a search match. */
  match: boolean;
  /** Inside the match the search is on. */
  current: boolean;
}

const LETTER = /[\p{L}\p{N}]/u;

function isWordChar(ch: string | undefined): boolean {
  return ch !== undefined && LETTER.test(ch);
}

/** The bare form used to compare words: lower case, punctuation around it dropped. */
export function normalizeWord(word: string): string {
  return word
    .trim()
    .toLocaleLowerCase()
    .replace(/^[^\p{L}\p{N}]+|[^\p{L}\p{N}]+$/gu, '');
}

/**
 * Where each word sits in `text`, found in order (each search starts after the previous word), so
 * the engine's spacing and punctuation never matter. A word that cannot be found is skipped.
 */
export function locateWords(text: string, words: readonly TranscriptWord[]): (TextRange | null)[] {
  const lower = text.toLocaleLowerCase();
  let cursor = 0;
  return words.map((word) => {
    const needle = word.w.trim().toLocaleLowerCase();
    if (needle === '') {
      return null;
    }
    const at = lower.indexOf(needle, cursor);
    if (at < 0) {
      return null;
    }
    cursor = at + needle.length;
    return { start: at, end: at + needle.length };
  });
}

/** Character ranges of the words below `threshold`, without surrounding punctuation. */
export function lowConfidenceRanges(text: string, words: readonly TranscriptWord[], threshold: number): TextRange[] {
  const located = locateWords(text, words);
  const ranges: TextRange[] = [];
  words.forEach((word, i) => {
    const range = located[i];
    if (range === null || range === undefined || !(word.c < threshold)) {
      return;
    }
    let { start, end } = range;
    while (start < end && !isWordChar(text[start])) {
      start += 1;
    }
    while (end > start && !isWordChar(text[end - 1])) {
      end -= 1;
    }
    if (end > start) {
      ranges.push({ start, end });
    }
  });
  return ranges;
}

function escapeRegExp(value: string): string {
  return value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

/**
 * Where `query` occurs in `text`: case-insensitive and word-boundary aware (a match may not start or
 * end inside a word), as transcript.search matches. Blank queries match nothing.
 */
export function queryRanges(text: string, query: string): TextRange[] {
  const needle = query.trim();
  if (needle === '') {
    return [];
  }
  const pattern = new RegExp(`(?<![\\p{L}\\p{N}])${escapeRegExp(needle).replace(/\s+/g, '\\s+')}(?![\\p{L}\\p{N}])`, 'giu');
  const ranges: TextRange[] = [];
  for (const match of text.matchAll(pattern)) {
    ranges.push({ start: match.index, end: match.index + match[0].length });
  }
  return ranges;
}

/** True when `text` contains `query` the way transcript.search counts a match. */
export function matchesQuery(text: string, query: string): boolean {
  return queryRanges(text, query).length > 0;
}

/**
 * Splits `text` at every boundary of the low-confidence and match ranges, so each run carries one
 * set of marks. `current` is the index into `matches` of the match the search is on, or -1.
 */
export function textRuns(text: string, low: readonly TextRange[], matches: readonly TextRange[] = [], current = -1): TextRun[] {
  const cuts = new Set<number>([0, text.length]);
  for (const r of [...low, ...matches]) {
    cuts.add(Math.max(0, Math.min(text.length, r.start)));
    cuts.add(Math.max(0, Math.min(text.length, r.end)));
  }
  const points = [...cuts].sort((a, b) => a - b);
  const inside = (ranges: readonly TextRange[], at: number): boolean => ranges.some((r) => r.start <= at && at < r.end);
  const currentRange = current >= 0 ? matches[current] : undefined;
  const runs: TextRun[] = [];
  for (let i = 0; i < points.length - 1; i++) {
    const from = points[i] ?? 0;
    const to = points[i + 1] ?? from;
    if (to <= from) {
      continue;
    }
    const run: TextRun = {
      text: text.slice(from, to),
      low: inside(low, from),
      match: inside(matches, from),
      current: currentRange !== undefined && currentRange.start <= from && from < currentRange.end,
    };
    const last = runs.at(-1);
    if (last?.low === run.low && last.match === run.match && last.current === run.current) {
      last.text += run.text;
    } else {
      runs.push(run);
    }
  }
  return runs;
}

/** A snippet split into plain and bold parts: the query words are bold (Library and search results). */
export function snippetParts(snippet: string, query: string): { text: string; bold: boolean }[] {
  const words = query
    .trim()
    .split(/\s+/)
    .filter((w) => w !== '');
  const ranges = words.flatMap((w) => queryRanges(snippet, w));
  if (ranges.length === 0) {
    return [{ text: snippet, bold: false }];
  }
  return textRuns(snippet, [], ranges).map((run) => ({ text: run.text, bold: run.match }));
}

/** Rounds to the millisecond so re-aligned times stay readable in the JSON. */
const ms = (seconds: number): number => Math.round(seconds * 1000) / 1000;

/** Longest common subsequence of two word lists: pairs of [oldIndex, newIndex]. */
function lcsPairs(a: readonly string[], b: readonly string[]): [number, number][] {
  const rows = a.length + 1;
  const cols = b.length + 1;
  const table = new Uint16Array(rows * cols);
  for (let i = a.length - 1; i >= 0; i--) {
    for (let j = b.length - 1; j >= 0; j--) {
      table[i * cols + j] =
        a[i] === b[j] && a[i] !== '' ? (table[(i + 1) * cols + j + 1] ?? 0) + 1 : Math.max(table[(i + 1) * cols + j] ?? 0, table[i * cols + j + 1] ?? 0);
    }
  }
  const pairs: [number, number][] = [];
  let i = 0;
  let j = 0;
  while (i < a.length && j < b.length) {
    if (a[i] === b[j] && a[i] !== '') {
      pairs.push([i, j]);
      i += 1;
      j += 1;
    } else if ((table[(i + 1) * cols + j] ?? 0) >= (table[i * cols + j + 1] ?? 0)) {
      i += 1;
    } else {
      j += 1;
    }
  }
  return pairs;
}

/**
 * Words for an edited segment (BRIDGE.md transcript.editSegment). Words the edit kept keep their
 * timing and confidence; new or changed words share the time between the kept words around them in
 * proportion to their length, with confidence 1. Without old words the whole segment is shared out.
 */
export function realignWords(oldWords: readonly TranscriptWord[], text: string, start: number, end: number): TranscriptWord[] {
  const tokens = text.split(/\s+/).filter((t) => t !== '');
  if (tokens.length === 0) {
    return [];
  }
  const pairs = lcsPairs(
    oldWords.map((w) => normalizeWord(w.w)),
    tokens.map(normalizeWord),
  );
  const kept = new Map<number, TranscriptWord>();
  for (const [oldIndex, newIndex] of pairs) {
    const old = oldWords[oldIndex];
    if (old !== undefined) {
      kept.set(newIndex, { ...old, w: tokens[newIndex] ?? old.w });
    }
  }
  const out: TranscriptWord[] = [];
  let index = 0;
  while (index < tokens.length) {
    const keptWord = kept.get(index);
    if (keptWord !== undefined) {
      out.push(keptWord);
      index += 1;
      continue;
    }
    // A run of new words: from the end of the previous word to the start of the next kept one.
    let runEnd = index;
    while (runEnd < tokens.length && !kept.has(runEnd)) {
      runEnd += 1;
    }
    const from = out.at(-1)?.e ?? start;
    const to = kept.get(runEnd)?.s ?? end;
    const span = Math.max(0, to - from);
    const run = tokens.slice(index, runEnd);
    const total = run.reduce((sum, t) => sum + t.length, 0);
    let at = from;
    for (const token of run) {
      const length = total === 0 ? span / run.length : (span * token.length) / total;
      out.push({ w: token, s: ms(at), e: ms(at + length), c: 1 });
      at += length;
    }
    index = runEnd;
  }
  return out;
}

/**
 * The segment the playhead is in: the last one starting at or before `seconds`, or -1 before the
 * first. `segments` are sorted by start.
 */
export function segmentIndexAt(segments: readonly Pick<TranscriptSegment, 'start'>[], seconds: number): number {
  let lo = 0;
  let hi = segments.length - 1;
  let found = -1;
  while (lo <= hi) {
    const mid = (lo + hi) >> 1;
    if ((segments[mid]?.start ?? 0) <= seconds) {
      found = mid;
      lo = mid + 1;
    } else {
      hi = mid - 1;
    }
  }
  return found;
}

/** "38%" of the talk time, or "—" when no time is known. */
export function talkShare(speaker: Pick<Speaker, 'talkTimeMs'>, speakers: readonly Pick<Speaker, 'talkTimeMs'>[]): string {
  const total = speakers.reduce((sum, s) => sum + Math.max(0, s.talkTimeMs), 0);
  if (total <= 0) {
    return '—';
  }
  return `${Math.round((Math.max(0, speaker.talkTimeMs) / total) * 100)}%`;
}

/** `var(--sp1)` … `var(--sp4)`. */
export function speakerColourVar(color: number): string {
  const n = ((Math.max(1, Math.round(color)) - 1) % 4) + 1;
  return `var(--sp${n})`;
}

/** The colour a new speaker gets: the next one after those in use, cycling after four. */
export function nextSpeakerColour(speakers: readonly Pick<Speaker, 'color'>[]): SpeakerColour {
  return ((speakers.length % 4) + 1) as SpeakerColour;
}

export function isSpeakerUncertain(segment: Pick<TranscriptSegment, 'speaker' | 'speakerConfidence'>): boolean {
  return segment.speaker !== null && segment.speakerConfidence !== null && segment.speakerConfidence < UNCERTAIN_SPEAKER;
}

/** "00:18:10": the segment timecode under the speaker (mono, always hh:mm:ss). */
export function segmentTimecode(seconds: number): string {
  const total = Math.max(0, Math.floor(seconds));
  const pad = (n: number): string => String(n).padStart(2, '0');
  return `${pad(Math.floor(total / 3600))}:${pad(Math.floor((total % 3600) / 60))}:${pad(total % 60)}`;
}

/** The next match index when stepping through `count` matches, wrapping at both ends. */
export function stepMatch(current: number, count: number, direction: 1 | -1): number {
  if (count <= 0) {
    return -1;
  }
  if (current < 0) {
    return direction === 1 ? 0 : count - 1;
  }
  return (current + direction + count) % count;
}

/** "3 of 12", "No matches". */
export function matchCountText(current: number, count: number): string {
  if (count === 0) {
    return 'No matches';
  }
  return current < 0 ? `${count} ${count === 1 ? 'match' : 'matches'}` : `${current + 1} of ${count}`;
}

/** True when a host label says the stage is waiting for the PC ("Paused · PC is busy"). */
export function isPausedLabel(label: string | null): boolean {
  return label !== null && /^paused\b/i.test(label);
}

/**
 * The running transcript stage in words: "Transcribing 64% · local GPU" from the host's label
 * "64% · local GPU"; a paused label is shown as it is.
 */
export function transcribingText(stage: Pick<StageStatus, 'label' | 'percent'> | null): string {
  if (stage === null) {
    return 'Transcribing';
  }
  if (isPausedLabel(stage.label)) {
    return stage.label ?? 'Paused';
  }
  if (stage.label !== null && stage.label !== '' && stage.label !== 'Done' && stage.label !== 'Queued') {
    return /^\d/.test(stage.label) ? `Transcribing ${stage.label}` : stage.label;
  }
  return stage.percent === null ? 'Transcribing' : `Transcribing ${Math.round(stage.percent)}%`;
}
