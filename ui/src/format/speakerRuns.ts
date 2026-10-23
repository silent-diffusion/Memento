// What a line's speaker menu can move besides the one line (DESIGN.md §9, 2.6.0): the turn the line is in
// (the run of consecutive lines of its speaker around it), every later line of that speaker ("From here
// on", for people who correct from the top down), the two speakers' lines traded with each other (a swap,
// from here on or everywhere, when the names are simply the wrong way round), or every line (the merge).
// Each choice carries its exact lines, so the menu can say how many and show them while the choice is
// hovered or active.
import type { Speaker, TranscriptSegment } from '../bridge/types';
import { formatDuration } from './duration';

/** One way to apply a picked speaker to a line with a speaker, with the lines it moves. */
export interface SectionChoice {
  id: 'line' | 'turn' | 'onward' | 'swapOnward' | 'swapAll' | 'merge';
  label: string;
  /** What happens, in one sentence (DESIGN.md §17). */
  detail: string;
  /**
   * The lines the choice moves, in transcript order (every line of the speaker for the merge; both speakers'
   * lines for a swap).
   */
  lines: readonly TranscriptSegment[];
}

/** The run of consecutive lines around `index` that have the same speaker as the line there, in transcript order. */
export function turnAround(segments: readonly TranscriptSegment[], index: number): TranscriptSegment[] {
  const line = segments[index];
  if (line === undefined) {
    return [];
  }
  if (line.speaker === null) {
    return [line];
  }
  let first = index;
  while (first > 0 && segments[first - 1]?.speaker === line.speaker) {
    first -= 1;
  }
  let last = index;
  while (last < segments.length - 1 && segments[last + 1]?.speaker === line.speaker) {
    last += 1;
  }
  return segments.slice(first, last + 1);
}

/** The line at `index` and every later line with its speaker, in transcript order. */
export function fromHereOn(segments: readonly TranscriptSegment[], index: number): TranscriptSegment[] {
  const line = segments[index];
  if (line === undefined) {
    return [];
  }
  return segments.slice(index).filter((s) => s.id === line.id || (line.speaker !== null && s.speaker === line.speaker));
}

function count(n: number): string {
  return `${n} ${n === 1 ? 'line' : 'lines'}`;
}

function at(segment: TranscriptSegment): string {
  return formatDuration(segment.start * 1000);
}

function sameLines(a: readonly TranscriptSegment[], b: readonly TranscriptSegment[]): boolean {
  return a.length === b.length && a.every((s, i) => s.id === b[i]?.id);
}

/** "Ana's 18 lines become Ben's and Ben's 20 become Ana's." */
function swapSentence(from: Speaker, to: Speaker, fromCount: number, toCount: number): string {
  return `${from.name}'s ${count(fromCount)} ${fromCount === 1 ? 'becomes' : 'become'} ${to.name}'s and ${to.name}'s ${toCount} ${toCount === 1 ? 'becomes' : 'become'} ${from.name}'s.`;
}

/**
 * The swaps between `from` (the speaker of the line at `index`) and `to`, when `to` has lines of its own:
 * from here on (the lines of either speaker from this line to the end), and everywhere. From here on is left
 * out when no line of either speaker comes before this one (it would be everywhere) and when `to` has no
 * later line (nothing to trade: it would be From here on or Just this line).
 */
function swapChoices(segments: readonly TranscriptSegment[], index: number, from: Speaker, to: Speaker): SectionChoice[] {
  const line = segments[index];
  const both = segments.filter((s) => s.speaker === from.id || s.speaker === to.id);
  const toAll = both.filter((s) => s.speaker === to.id).length;
  if (line === undefined || toAll === 0) {
    return [];
  }
  const fromAll = both.length - toAll;
  const later = segments.slice(index).filter((s) => s.speaker === from.id || s.speaker === to.id);
  const choices: SectionChoice[] = [];
  const fromLater = later.filter((s) => s.speaker === from.id).length;
  const toLater = later.length - fromLater;
  if (later.length < both.length && toLater > 0) {
    choices.push({
      id: 'swapOnward',
      label: `Swap ${from.name} and ${to.name} from here on · ${fromLater} + ${toLater} lines`,
      detail: `From ${at(line)} on, ${swapSentence(from, to, fromLater, toLater)} Both names stay.`,
      lines: later,
    });
  }
  choices.push({
    id: 'swapAll',
    label: `Swap ${from.name} and ${to.name} everywhere · ${fromAll} + ${toAll} lines`,
    detail: `Every line of ${from.name} becomes ${to.name}'s and every line of ${to.name} becomes ${from.name}'s. Both names stay.`,
    lines: both,
  });
  return choices;
}

/**
 * The choices for moving the line at `index` (a line of `from`) to `to`: Just this line, This turn, From here
 * on, the swaps (when `to` has lines), Merge. A choice that would move the same lines as one before it is left
 * out: a turn of one line is just the line, and From here on that is the turn (or one line) says nothing new.
 * The merge is always last.
 */
export function sectionChoices(segments: readonly TranscriptSegment[], index: number, from: Speaker, to: Speaker): SectionChoice[] {
  const line = segments[index];
  if (line === undefined) {
    return [];
  }
  const all = segments.filter((s) => s.speaker === from.id);
  const turn = turnAround(segments, index);
  const onward = fromHereOn(segments, index);
  const choices: SectionChoice[] = [{ id: 'line', label: 'Just this line', detail: `The line at ${at(line)} moves to ${to.name}.`, lines: [line] }];
  const first = turn[0];
  const last = turn[turn.length - 1];
  if (turn.length > 1 && first !== undefined && last !== undefined) {
    choices.push({
      id: 'turn',
      label: `This turn · ${count(turn.length)}`,
      detail:
        turn.length === all.length
          ? `All ${count(turn.length)} of ${from.name}, from ${at(first)} to ${at(last)}, move to ${to.name}.`
          : `The ${count(turn.length)} from ${at(first)} to ${at(last)} move to ${to.name}; ${from.name} keeps the others.`,
      lines: turn,
    });
  }
  if (onward.length > 1 && !sameLines(onward, turn)) {
    const before = all.length - onward.length;
    choices.push({
      id: 'onward',
      label: `From here on · ${count(onward.length)}`,
      detail:
        before === 0
          ? `All ${count(onward.length)} of ${from.name}, from ${at(line)} to the end, move to ${to.name}.`
          : `The ${count(onward.length)} of ${from.name} from ${at(line)} to the end move to ${to.name}; ${from.name} keeps the ${before === 1 ? 'line' : count(before)} before.`,
      lines: onward,
    });
  }
  choices.push(...swapChoices(segments, index, from, to));
  choices.push({
    id: 'merge',
    label: `Merge ${from.name} into ${to.name} · ${count(all.length)}`,
    detail: `Every line of ${from.name} moves to ${to.name}, and ${from.name} goes away. Undo puts them back.`,
    lines: all,
  });
  return choices;
}
