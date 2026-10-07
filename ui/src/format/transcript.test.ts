import { describe, expect, it } from 'vitest';
import type { TranscriptWord } from '../bridge/types';
import {
  gapNoticeText,
  gapPlacement,
  isSpeakerUncertain,
  lowConfidenceRanges,
  matchCountText,
  otherModelFor,
  queryRanges,
  realignWords,
  segmentIndexAt,
  segmentTimecode,
  snippetParts,
  speakerColourVar,
  stepMatch,
  talkShare,
  textRuns,
  transcribingText,
} from './transcript';

const words = (spec: [string, number][], start = 0, step = 0.5): TranscriptWord[] =>
  spec.map(([w, c], i) => ({ w, s: start + i * step, e: start + i * step + step * 0.9, c }));

describe('low-confidence words (DESIGN.md §5.12)', () => {
  it('marks only the words below the threshold, without their punctuation', () => {
    const text = 'Can you update the Figma, and I will take it?';
    const ws = words([
      ['Can', 0.95],
      ['you', 0.97],
      ['update', 0.9],
      ['the', 0.99],
      ['Figma,', 0.31],
      ['and', 0.93],
      ['I', 0.98],
      ['will', 0.96],
      ['take', 0.5],
      ['it?', 0.49],
    ]);
    const ranges = lowConfidenceRanges(text, ws, 0.5);
    expect(ranges.map((r) => text.slice(r.start, r.end))).toEqual(['Figma', 'it']);
    // The threshold is "below": exactly 0.5 is not marked.
    expect(lowConfidenceRanges(text, ws, 0.4).map((r) => text.slice(r.start, r.end))).toEqual(['Figma']);
  });

  it('finds words in order even when the engine spaces them differently', () => {
    const text = 'the the cat';
    const ranges = lowConfidenceRanges(text, words([[' the', 0.9], [' the', 0.2], [' cat', 0.9]]), 0.5);
    expect(ranges).toEqual([{ start: 4, end: 7 }]);
  });

  it('skips words that are not in the text', () => {
    expect(lowConfidenceRanges('hello there', words([['missing', 0.1], ['there', 0.1]]), 0.5)).toEqual([{ start: 6, end: 11 }]);
  });

  it('splits text into runs that carry the low, match and current marks', () => {
    const text = 'rows stay at 68 pixels for now';
    const low = [{ start: 13, end: 15 }];
    const matches = queryRanges(text, 'pixels');
    const runs = textRuns(text, low, matches, 0);
    expect(runs.map((r) => [r.text, r.low, r.match, r.current])).toEqual([
      ['rows stay at ', false, false, false],
      ['68', true, false, false],
      [' ', false, false, false],
      ['pixels', false, true, true],
      [' for now', false, false, false],
    ]);
    expect(runs.map((r) => r.text).join('')).toBe(text);
  });
});

describe('transcript search ranges', () => {
  it('is case-insensitive and word-boundary aware', () => {
    expect(queryRanges('Dark theme and the darker grey. DARK!', 'dark')).toEqual([
      { start: 0, end: 4 },
      { start: 32, end: 36 },
    ]);
    expect(queryRanges('anything', '')).toEqual([]);
    expect(queryRanges('status pills', 'status   pills')).toEqual([{ start: 0, end: 12 }]);
    expect(queryRanges('a (b) c', '(b)')).toEqual([{ start: 2, end: 5 }]);
  });

  it('steps through matches with wrap-around and counts them', () => {
    expect(stepMatch(-1, 3, 1)).toBe(0);
    expect(stepMatch(-1, 3, -1)).toBe(2);
    expect(stepMatch(2, 3, 1)).toBe(0);
    expect(stepMatch(0, 3, -1)).toBe(2);
    expect(stepMatch(0, 0, 1)).toBe(-1);
    expect(matchCountText(-1, 0)).toBe('No matches');
    expect(matchCountText(-1, 1)).toBe('1 match');
    expect(matchCountText(1, 12)).toBe('2 of 12');
  });

  it('bolds the query words in a snippet', () => {
    expect(snippetParts('…the processing card should disappear…', 'processing card')).toEqual([
      { text: '…the ', bold: false },
      { text: 'processing', bold: true },
      { text: ' ', bold: false },
      { text: 'card', bold: true },
      { text: ' should disappear…', bold: false },
    ]);
    expect(snippetParts('nothing here', 'zebra')).toEqual([{ text: 'nothing here', bold: false }]);
  });
});

describe('re-aligning words after an edit (transcript.editSegment)', () => {
  const old = words(
    [
      ['Can', 0.95],
      ['you', 0.97],
      ['update', 0.9],
      ['the', 0.99],
      ['Figgma', 0.31],
      ['file?', 0.93],
    ],
    10,
    1,
  );

  it('keeps the timing and confidence of words the edit kept and gives new words confidence 1', () => {
    const next = realignWords(old, 'Can you update the Figma file?', 10, 16);
    expect(next.map((w) => w.w)).toEqual(['Can', 'you', 'update', 'the', 'Figma', 'file?']);
    expect(next[0]).toEqual(old[0]);
    expect(next[5]).toEqual(old[5]);
    // The corrected word takes the time between its neighbours.
    expect(next[4]).toEqual({ w: 'Figma', s: old[3]?.e, e: old[5]?.s, c: 1 });
  });

  it('shares a run of new words in proportion to their length', () => {
    const next = realignWords(old, 'Can you please update the Figma file?', 10, 16);
    const please = next[2];
    expect(please?.w).toBe('please');
    expect(please?.s).toBe(old[1]?.e);
    expect(please?.e).toBe(old[2]?.s);
    expect(please?.c).toBe(1);
  });

  it('spreads the whole segment proportionally when there were no words', () => {
    const next = realignWords([], 'ab abcd ab', 0, 8);
    expect(next.map((w) => [w.s, w.e, w.c])).toEqual([
      [0, 2, 1],
      [2, 6, 1],
      [6, 8, 1],
    ]);
  });

  it('returns no words for an empty text', () => {
    expect(realignWords(old, '   ', 0, 1)).toEqual([]);
  });

  it('keeps timings ordered and inside the segment', () => {
    const next = realignWords(old, 'Totally different words here now please', 10, 16);
    for (let i = 0; i < next.length; i++) {
      const w = next[i];
      expect(w?.s).toBeGreaterThanOrEqual(10);
      expect(w?.e).toBeLessThanOrEqual(16);
      if (i > 0) {
        expect(w?.s).toBeGreaterThanOrEqual(next[i - 1]?.e ?? 0);
      }
    }
  });
});

describe('playhead, speakers and wording', () => {
  const segments = [{ start: 0 }, { start: 5 }, { start: 9.5 }, { start: 20 }];

  it('finds the segment the playhead is in', () => {
    expect(segmentIndexAt(segments, -1)).toBe(-1);
    expect(segmentIndexAt(segments, 0)).toBe(0);
    expect(segmentIndexAt(segments, 9.49)).toBe(1);
    expect(segmentIndexAt(segments, 9.5)).toBe(2);
    expect(segmentIndexAt(segments, 999)).toBe(3);
    expect(segmentIndexAt([], 3)).toBe(-1);
  });

  it('shares talk time and colours', () => {
    const speakers = [{ talkTimeMs: 380 }, { talkTimeMs: 270 }, { talkTimeMs: 350 }];
    expect(talkShare(speakers[0] ?? { talkTimeMs: 0 }, speakers)).toBe('38%');
    expect(talkShare({ talkTimeMs: 0 }, [{ talkTimeMs: 0 }])).toBe('—');
    expect(speakerColourVar(1)).toBe('var(--sp1)');
    expect(speakerColourVar(5)).toBe('var(--sp1)');
    expect(speakerColourVar(4)).toBe('var(--sp4)');
  });

  it('calls a speaker assignment below 0.7 uncertain', () => {
    expect(isSpeakerUncertain({ speaker: 'sp1', speakerConfidence: 0.69 })).toBe(true);
    expect(isSpeakerUncertain({ speaker: 'sp1', speakerConfidence: 0.7 })).toBe(false);
    expect(isSpeakerUncertain({ speaker: null, speakerConfidence: 0.2 })).toBe(false);
  });

  it('writes the timecode and the running stage', () => {
    expect(segmentTimecode(18 * 60 + 10)).toBe('00:18:10');
    expect(segmentTimecode(3725.7)).toBe('01:02:05');
    expect(transcribingText({ label: '64% · local GPU', percent: 64 })).toBe('Transcribing 64% · local GPU');
    expect(transcribingText({ label: 'Paused · PC is busy', percent: 40 })).toBe('Paused · PC is busy');
    expect(transcribingText({ label: null, percent: 12 })).toBe('Transcribing 12%');
    expect(transcribingText(null)).toBe('Transcribing');
  });
});

describe('coverage notices', () => {
  const at = (...starts: number[]) => starts.map((start) => ({ start }));

  it('words a gap in the transcript’s own time format', () => {
    expect(gapNoticeText({ start: 3.4, end: 18.9 })).toBe('Nothing was transcribed between 0:03 and 0:18, although there was speech.');
    expect(gapNoticeText({ start: 3605, end: 3640 })).toBe('Nothing was transcribed between 1:00:05 and 1:00:40, although there was speech.');
  });

  it('places each gap before the first line at or after it, or after the last line', () => {
    const segments = at(0, 3, 20, 25);
    const placed = gapPlacement(segments, [
      { start: 40, end: 60, track: 'mic' },
      { start: 4, end: 19, track: 'mic' },
      { start: 4.5, end: 19, track: 'system' },
    ]);
    expect([...placed.keys()]).toEqual([2, 4]);
    expect(placed.get(2)?.map((g) => g.track)).toEqual(['mic', 'system']);
    expect(placed.get(4)?.[0]?.start).toBe(40);
    expect(gapPlacement([], [{ start: 0, end: 30, track: null }]).get(0)).toHaveLength(1);
  });

  it('offers the CPU fallback model, or another installed one, but never the one that made the transcript', () => {
    const model = (id: string, installed = true, engine: 'transcription' | 'speakers' = 'transcription') => ({ id, name: id.toUpperCase(), installed, engine });
    const models = [model('whisper-large-v3-turbo'), model('whisper-medium'), model('whisper-small'), model('nemo-titanet-small', true, 'speakers')];
    expect(otherModelFor('whisper-large-v3-turbo', 'whisper-small', models)?.id).toBe('whisper-small');
    expect(otherModelFor('whisper-small', 'whisper-small', models)?.id).toBe('whisper-large-v3-turbo');
    expect(otherModelFor('whisper-large-v3-turbo', 'whisper-small', [model('whisper-large-v3-turbo'), model('whisper-small', false)])).toBeNull();
  });
});
