import { describe, expect, it } from 'vitest';
import type { TranscriptSegment } from '../bridge/types';
import { clearestLine, SAMPLE_SECONDS, sampleEndMs } from './voiceSample';

function line(id: string, speaker: string | null, start: number, seconds: number, speakerConfidence: number | null): TranscriptSegment {
  return { id, start, end: start + seconds, track: 'mic', speaker, speakerConfidence, text: id, confidence: 1, words: [], edited: null };
}

describe('clearestLine', () => {
  it('takes the speaker\'s surest line among those long enough to hear a voice in', () => {
    const segments = [
      line('short-sure', 'spk1', 0, 1, 0.99),
      line('long-doubtful', 'spk1', 10, 5, 0.5),
      line('long-sure', 'spk1', 20, 4, 0.9),
      line('other', 'spk2', 30, 8, 1),
    ];
    expect(clearestLine(segments, 'spk1')?.id).toBe('long-sure');
    expect(clearestLine(segments, 'spk2')?.id).toBe('other');
  });

  it('falls back to every line when none is long enough, and none counts as no confidence', () => {
    const segments = [line('a', 'spk1', 0, 1, null), line('b', 'spk1', 5, 2, 0.6), line('c', 'spk1', 9, 1.5, 0.6)];
    // b and c tie on confidence; the longer one wins.
    expect(clearestLine(segments, 'spk1')?.id).toBe('b');
  });

  it('breaks a tie on confidence and length by transcript order', () => {
    const segments = [line('first', 'spk1', 0, 4, 0.8), line('second', 'spk1', 10, 4, 0.8)];
    expect(clearestLine(segments, 'spk1')?.id).toBe('first');
  });

  it('is null for a speaker without lines', () => {
    expect(clearestLine([line('a', 'spk2', 0, 4, 1)], 'spk1')).toBeNull();
    expect(clearestLine([], 'spk1')).toBeNull();
  });
});

describe('sampleEndMs', () => {
  it('stops at the line\'s end or a few seconds in, whichever comes first', () => {
    expect(sampleEndMs(line('a', 'spk1', 10, 2, 1))).toBe(12_000);
    expect(sampleEndMs(line('b', 'spk1', 10, 20, 1))).toBe((10 + SAMPLE_SECONDS) * 1000);
  });
});
