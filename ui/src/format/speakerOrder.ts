// The order of every speaker list (DESIGN.md §9: the People pane, the speaker menu and its search,
// the merge menu, participant pickers): people the user named come first, then the unnamed
// "Speaker n" ones; within each group, in order of first appearance in the transcript (speakers
// with no line keep their place at the end of their group).
import type { Speaker, SpeakerAdded, SpokeMark, TranscriptSegment, WhoSpoke } from '../bridge/types';

/** At most this many people in Who spoke (count and names), as the host allows. */
export const WHO_SPOKE_MAX = 20;

/** 0 for a speaker the user named (renamed, added by name, or named from Who spoke), 1 otherwise. */
export function speakerGroup(speaker: Pick<Speaker, 'renamed'>): 0 | 1 {
  return speaker.renamed ? 0 : 1;
}

/** Named speakers first, then unnamed ones; each group by first appearance. */
export function orderSpeakers<T extends Pick<Speaker, 'id' | 'renamed'>>(speakers: readonly T[], segments: readonly Pick<TranscriptSegment, 'speaker' | 'start'>[]): T[] {
  const first = new Map<string, number>();
  for (const segment of segments) {
    if (segment.speaker !== null) {
      const at = first.get(segment.speaker);
      if (at === undefined || segment.start < at) {
        first.set(segment.speaker, segment.start);
      }
    }
  }
  return speakers
    .map((speaker, index) => ({ speaker, index, group: speakerGroup(speaker), at: first.get(speaker.id) ?? Number.POSITIVE_INFINITY }))
    .sort((a, b) => a.group - b.group || a.at - b.at || a.index - b.index)
    .map((x) => x.speaker);
}

/** "Speaker 3", the name Memento gives before anyone names a speaker. */
export function isDefaultSpeakerName(name: string): boolean {
  return /^Speaker \d+$/u.test(name.trim());
}

/**
 * Names to add to the participants from the speakers (Details › Add from speakers): the named
 * speakers in list order, without "Speaker n", blanks and names already listed (ignoring case).
 */
export function participantsFromSpeakers(participants: readonly string[], names: readonly string[]): string[] {
  const seen = new Set(participants.map((p) => p.trim().toLocaleLowerCase()));
  const added: string[] = [];
  for (const raw of names) {
    const name = raw.trim();
    const key = name.toLocaleLowerCase();
    if (name !== '' && !isDefaultSpeakerName(name) && !seen.has(key)) {
      seen.add(key);
      added.push(name);
    }
  }
  return added;
}

/** Who spoke with the participants as its names ("Use participants"): replaces the names, deduplicated, at most 20. */
export function whoSpokeFromParticipants(whoSpoke: WhoSpoke, participants: readonly string[]): WhoSpoke {
  const names = participantsFromSpeakers([], participants).slice(0, WHO_SPOKE_MAX);
  return { count: whoSpoke.count, names, marks: marksFor(whoSpoke.marks, names), added: addedFor(whoSpoke.added, names) };
}

/** The added times that still name one of `names` (2.7.0). */
export function addedFor(added: readonly SpeakerAdded[] | undefined, names: readonly string[]): SpeakerAdded[] {
  const kept = new Set(names.map((n) => n.trim().toLocaleLowerCase()));
  return (added ?? []).filter((a) => kept.has(a.name.trim().toLocaleLowerCase()));
}

/** The moment a name is added while recording: the timer's time, the wall clock, and whether they are speaking now. */
export interface AddMoment {
  atMs: number;
  at: string;
  speakingNow: boolean;
}

/**
 * Who spoke with `names` as its names: marks and added times of names taken off go with them, and
 * while recording (`moment`) each new name gets its added time, and a Speaking now mark when the
 * person is speaking as they are added (2.7.0).
 */
export function withNames(whoSpoke: WhoSpoke, names: readonly string[], moment?: AddMoment): WhoSpoke {
  const had = new Set(whoSpoke.names.map((n) => n.trim().toLocaleLowerCase()));
  const fresh = moment === undefined ? [] : names.filter((n) => !had.has(n.trim().toLocaleLowerCase()));
  const added = addedFor(whoSpoke.added, names);
  const marks = marksFor(whoSpoke.marks, names);
  for (const name of fresh) {
    if (moment !== undefined && !added.some((a) => a.name.trim().toLocaleLowerCase() === name.trim().toLocaleLowerCase())) {
      added.push({ name, atMs: moment.atMs, at: moment.at });
    }
    if (moment?.speakingNow === true) {
      marks.push({ name, atMs: moment.atMs });
    }
  }
  return { ...whoSpoke, names: [...names], marks, added };
}

/** The Speaking now marks that still name one of `names` (a name taken off the list takes its marks with it). */
export function marksFor(marks: readonly SpokeMark[], names: readonly string[]): SpokeMark[] {
  const kept = new Set(names.map((n) => n.trim().toLocaleLowerCase()));
  return marks.filter((m) => kept.has(m.name.trim().toLocaleLowerCase()));
}

/** The count speakers are identified with: the count, else the number of names, else null (Settings decide). */
export function effectiveCount(whoSpoke: WhoSpoke): number | null {
  return whoSpoke.count ?? (whoSpoke.names.length > 0 ? whoSpoke.names.length : null);
}
