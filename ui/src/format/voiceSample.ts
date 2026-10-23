// Hear a speaker (DESIGN.md §9, 2.6.0): which line of theirs to play so a "Speaker 3" can be named by ear.
// The clearest line is the one the speaker pass was surest about, among lines long enough to hear a
// voice in when there are any; the sample is its first few seconds.
import type { TranscriptSegment } from '../bridge/types';

/** How much of the line plays, at most. */
export const SAMPLE_SECONDS = 6;

/** A line at least this long is preferred: a word or two is too little to hear a voice in. */
export const CLEAR_SECONDS = 3;

function length(line: TranscriptSegment): number {
  return Math.max(0, line.end - line.start);
}

/**
 * The speaker's line to play: the highest `speakerConfidence` (none counts as 0) among their lines of
 * `CLEAR_SECONDS` or more when they have any, else among all of them; ties go to the longer line, then
 * the earlier one. Null when the speaker has no lines.
 */
export function clearestLine(segments: readonly TranscriptSegment[], speakerId: string): TranscriptSegment | null {
  const own = segments.filter((s) => s.speaker === speakerId);
  const long = own.filter((s) => length(s) >= CLEAR_SECONDS);
  const candidates = long.length > 0 ? long : own;
  let best: TranscriptSegment | null = null;
  for (const line of candidates) {
    if (best === null) {
      best = line;
      continue;
    }
    const confidence = line.speakerConfidence ?? 0;
    const bestConfidence = best.speakerConfidence ?? 0;
    if (confidence > bestConfidence || (confidence === bestConfidence && length(line) > length(best))) {
      best = line;
    }
  }
  return best;
}

/** Where the sample of a line stops, in milliseconds: its end, or `SAMPLE_SECONDS` in, whichever is first. */
export function sampleEndMs(line: TranscriptSegment): number {
  return Math.min(line.end, line.start + SAMPLE_SECONDS) * 1000;
}
