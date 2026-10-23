import { describe, expect, it } from 'vitest';
import type { Speaker, TranscriptSegment } from '../bridge/types';
import { fromHereOn, sectionChoices, turnAround } from './speakerRuns';

const ana: Speaker = { id: 'spk1', name: 'Ana', renamed: true, color: 1, talkTimeMs: 0 };
const ben: Speaker = { id: 'spk2', name: 'Ben', renamed: true, color: 2, talkTimeMs: 0 };

/** Lines 10 s apart from 1:00, each with the speaker given (null: none). */
function lines(...speakers: (string | null)[]): TranscriptSegment[] {
  return speakers.map((speaker, i) => ({
    id: `s${i + 1}`,
    start: 60 + i * 10,
    end: 60 + i * 10 + 8,
    track: 'mic',
    speaker,
    speakerConfidence: speaker === null ? null : 1,
    text: `Line ${i + 1}`,
    confidence: 1,
    words: [],
    edited: null,
  }));
}

const ids = (segments: readonly TranscriptSegment[]): string[] => segments.map((s) => s.id);

describe('speakerRuns', () => {
  // Ana, Ana, Ana, Ben, Ana, Ana, (none), Ana
  const segments = lines('spk1', 'spk1', 'spk1', 'spk2', 'spk1', 'spk1', null, 'spk1');

  it('finds the turn around a line: the run of its speaker, broken by another speaker or no speaker', () => {
    expect(ids(turnAround(segments, 1))).toEqual(['s1', 's2', 's3']);
    expect(ids(turnAround(segments, 0))).toEqual(['s1', 's2', 's3']);
    expect(ids(turnAround(segments, 4))).toEqual(['s5', 's6']);
    expect(ids(turnAround(segments, 7))).toEqual(['s8']);
    expect(ids(turnAround(segments, 3))).toEqual(['s4']);
    // A line without a speaker is a turn of its own; an index outside the lines is nothing.
    expect(ids(turnAround(segments, 6))).toEqual(['s7']);
    expect(turnAround(segments, 9)).toEqual([]);
  });

  it('finds the lines from here on: the line and every later one of its speaker', () => {
    expect(ids(fromHereOn(segments, 1))).toEqual(['s2', 's3', 's5', 's6', 's8']);
    expect(ids(fromHereOn(segments, 7))).toEqual(['s8']);
    expect(ids(fromHereOn(segments, 6))).toEqual(['s7']);
    expect(fromHereOn(segments, 9)).toEqual([]);
  });

  it('offers Just this line, This turn, From here on, the swaps and Merge with their counts and spans', () => {
    const choices = sectionChoices(segments, 1, ana, ben);
    expect(choices.map((c) => [c.id, c.label])).toEqual([
      ['line', 'Just this line'],
      ['turn', 'This turn · 3 lines'],
      ['onward', 'From here on · 5 lines'],
      ['swapOnward', 'Swap Ana and Ben from here on · 5 + 1 lines'],
      ['swapAll', 'Swap Ana and Ben everywhere · 6 + 1 lines'],
      ['merge', 'Merge Ana into Ben · 6 lines'],
    ]);
    expect(choices.map((c) => c.detail)).toEqual([
      'The line at 1:10 moves to Ben.',
      'The 3 lines from 1:00 to 1:20 move to Ben; Ana keeps the others.',
      'The 5 lines of Ana from 1:10 to the end move to Ben; Ana keeps the line before.',
      "From 1:10 on, Ana's 5 lines become Ben's and Ben's 1 becomes Ana's. Both names stay.",
      "Every line of Ana becomes Ben's and every line of Ben becomes Ana's. Both names stay.",
      'Every line of Ana moves to Ben, and Ana goes away. Undo puts them back.',
    ]);
    expect(choices.map((c) => ids(c.lines))).toEqual([
      ['s2'],
      ['s1', 's2', 's3'],
      ['s2', 's3', 's5', 's6', 's8'],
      ['s2', 's3', 's4', 's5', 's6', 's8'],
      ['s1', 's2', 's3', 's4', 's5', 's6', 's8'],
      ['s1', 's2', 's3', 's5', 's6', 's8'],
    ]);
  });

  it('leaves out a turn of one line, and From here on when it is the turn or one line', () => {
    // The last line: its turn is itself and nothing follows (and Ben has nothing later to trade).
    expect(sectionChoices(segments, 7, ana, ben).map((c) => c.id)).toEqual(['line', 'swapAll', 'merge']);
    // Inside the last turn of Ana: From here on is the rest of that turn, no more.
    const tail = lines('spk1', 'spk2', 'spk1', 'spk1');
    expect(sectionChoices(tail, 2, ana, ben).map((c) => c.id)).toEqual(['line', 'turn', 'swapAll', 'merge']);
    // The first line, alone in its turn: From here on is every line of Ana.
    const first = sectionChoices(tail, 0, ana, ben);
    expect(first.map((c) => c.id)).toEqual(['line', 'onward', 'swapAll', 'merge']);
    expect(first[1]?.detail).toBe('All 3 lines of Ana, from 1:00 to the end, move to Ben.');
  });

  it('says "all" when a turn holds every line of the speaker', () => {
    const one = lines('spk2', 'spk1', 'spk1', 'spk2');
    const choices = sectionChoices(one, 1, ana, ben);
    expect(choices.map((c) => c.id)).toEqual(['line', 'turn', 'swapOnward', 'swapAll', 'merge']);
    expect(choices[1]?.detail).toBe('All 2 lines of Ana, from 1:10 to 1:20, move to Ben.');
    expect(choices[4]?.label).toBe('Merge Ana into Ben · 2 lines');
  });

  it('offers no swap when the chosen speaker has no lines, and no swap from here on when it would be everywhere', () => {
    const chris: Speaker = { id: 'spk3', name: 'Chris', renamed: true, color: 3, talkTimeMs: 0 };
    expect(sectionChoices(segments, 1, ana, chris).map((c) => c.id)).toEqual(['line', 'turn', 'onward', 'merge']);
    // From the first line of either speaker, from here on is everywhere.
    const late = lines(null, 'spk1', 'spk2', 'spk1');
    const choices = sectionChoices(late, 1, ana, ben);
    expect(choices.map((c) => c.id)).toEqual(['line', 'onward', 'swapAll', 'merge']);
    expect(choices[2]?.label).toBe('Swap Ana and Ben everywhere · 2 + 1 lines');
    expect(ids(choices[2]?.lines ?? [])).toEqual(['s2', 's3', 's4']);
  });

  it('counts both speakers from here on and keeps the swapped lines in transcript order', () => {
    // Ben, Ana, Ben, Ana, Ben: from the second Ana, Ana's 1 line and Ben's 1 line trade; the earlier three stay.
    const mixed = lines('spk2', 'spk1', 'spk2', 'spk1', 'spk2');
    const choices = sectionChoices(mixed, 3, ana, ben);
    const onward = choices.find((c) => c.id === 'swapOnward');
    expect(onward?.label).toBe('Swap Ana and Ben from here on · 1 + 1 lines');
    expect(onward?.detail).toBe("From 1:30 on, Ana's 1 line becomes Ben's and Ben's 1 becomes Ana's. Both names stay.");
    expect(ids(onward?.lines ?? [])).toEqual(['s4', 's5']);
    expect(choices.find((c) => c.id === 'swapAll')?.label).toBe('Swap Ana and Ben everywhere · 2 + 3 lines');
  });

  it('counts the lines the speaker keeps before From here on', () => {
    const many = lines('spk1', 'spk1', 'spk2', 'spk1', 'spk2', 'spk1');
    expect(sectionChoices(many, 3, ana, ben).find((c) => c.id === 'onward')?.detail).toBe(
      'The 2 lines of Ana from 1:30 to the end move to Ben; Ana keeps the 2 lines before.',
    );
  });

  it('is nothing for a line outside the transcript', () => {
    expect(sectionChoices(segments, 20, ana, ben)).toEqual([]);
  });
});
