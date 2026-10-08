import { describe, expect, it } from 'vitest';
import type { Speaker, TranscriptSegment } from '../bridge/types';
import { effectiveCount, isDefaultSpeakerName, orderSpeakers, participantsFromSpeakers, whoSpokeFromParticipants } from './speakerOrder';
import { filterByName } from './speakerSearch';

const speaker = (id: string, name: string, renamed: boolean): Speaker => ({ id, name, renamed, color: 1, talkTimeMs: 0 });
const line = (speakerId: string | null, start: number): Pick<TranscriptSegment, 'speaker' | 'start'> => ({ speaker: speakerId, start });

describe('speaker order', () => {
  const speakers = [speaker('s1', 'Speaker 1', false), speaker('s2', 'Rowan Hale', true), speaker('s3', 'Speaker 3', false), speaker('s4', 'Avery Stone', true), speaker('s5', 'Sam Okafor', true)];
  // First lines: s3 at 1, s1 at 5, s4 at 8, s2 at 20; s5 has none.
  const segments = [line('s3', 1), line('s1', 5), line('s4', 8), line('s1', 9), line('s2', 20), line(null, 0)];

  it('puts named people first, then "Speaker n", each by first appearance; speakers without a line last in their group', () => {
    expect(orderSpeakers(speakers, segments).map((s) => s.name)).toEqual(['Avery Stone', 'Rowan Hale', 'Sam Okafor', 'Speaker 3', 'Speaker 1']);
  });

  it('keeps the given order when nothing has a line', () => {
    expect(orderSpeakers(speakers, []).map((s) => s.id)).toEqual(['s2', 's4', 's5', 's1', 's3']);
  });

  it('keeps prefix-first ranking within each group when searching', () => {
    const ordered = orderSpeakers(speakers, segments);
    const group = (s: Speaker): number => (s.renamed ? 0 : 1);
    expect(filterByName(ordered, 's', (s) => s.name, group).map((s) => s.name)).toEqual(['Sam Okafor', 'Avery Stone', 'Speaker 3', 'Speaker 1']);
    expect(filterByName(ordered, 'ro', (s) => s.name, group).map((s) => s.name)).toEqual(['Rowan Hale']);
  });

  it('knows the names Memento gives', () => {
    expect(isDefaultSpeakerName('Speaker 12')).toBe(true);
    expect(isDefaultSpeakerName(' Speaker 3 ')).toBe(true);
    expect(isDefaultSpeakerName('Speaker')).toBe(false);
    expect(isDefaultSpeakerName('Speaker Jones')).toBe(false);
  });
});

describe('participants from speakers', () => {
  it('adds the named speakers not listed yet, without "Speaker n" or repeats', () => {
    expect(participantsFromSpeakers(['avery stone'], ['Avery Stone', 'Speaker 2', 'Rowan Hale', ' ', 'rowan hale', 'Sam'])).toEqual(['Rowan Hale', 'Sam']);
    expect(participantsFromSpeakers(['A'], ['Speaker 1'])).toEqual([]);
  });
});

describe('who spoke', () => {
  it('uses the participants as the names, keeping the count', () => {
    expect(whoSpokeFromParticipants({ count: 3, names: ['Old'] }, ['Ana', 'ana', 'Speaker 2', 'Ben'])).toEqual({ count: 3, names: ['Ana', 'Ben'] });
  });

  it('takes at most twenty names', () => {
    const many = Array.from({ length: 25 }, (_, i) => `Person ${i + 1}`);
    expect(whoSpokeFromParticipants({ count: null, names: [] }, many).names).toHaveLength(20);
  });

  it('identifies with the count, else as many as the names, else leaves it to Settings', () => {
    expect(effectiveCount({ count: 2, names: ['A', 'B', 'C'] })).toBe(2);
    expect(effectiveCount({ count: null, names: ['A', 'B', 'C'] })).toBe(3);
    expect(effectiveCount({ count: null, names: [] })).toBeNull();
  });
});
