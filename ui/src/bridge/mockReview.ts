// The browser-preview host's 2.0 Review methods (BRIDGE.md "Review (2.0)"): known voices, suggested chapters and the
// selection mode's several-lines speaker change. The real host compares voice-model signatures (VoiceMatcher) and finds
// subject changes in the words (ChapterSuggester); this stand-in is simpler and says so: a speaker "sounds like" a known
// voice when it has the same speaker id in another sample recording, and chapters are suggested at the quarters of the
// transcript, titled with the word said most there. Nothing here is a measurement.
import type { MockProject } from './mockData';
import { MockHostError } from './mockSession';
import type { MockTranscription } from './mockTranscription';
import type {
  ChapterSuggestion,
  ChapterSuggestionParams,
  ChapterSuggestionsResult,
  EmptyParams,
  EmptyResult,
  KnownVoiceInfo,
  KnownVoicesResult,
  RecordingIdParams,
  SettingsSnapshot,
  Speaker,
  SpeakerColour,
  SpeakerRestore,
  TranscriptSameVoiceParams,
  TranscriptSameVoiceResult,
  TranscriptSplitSpeakerParams,
  TranscriptSplitSpeakerResult,
  TranscriptSegment,
  TranscriptSetSegmentsSpeakerParams,
  TranscriptSetSegmentsSpeakerResult,
  TranscriptSwapSpeakersParams,
  VoiceAcceptParams,
  VoiceAcceptResult,
  VoiceChangeParams,
  VoiceDeclineParams,
  VoiceIdParams,
  VoiceMatch,
  VoiceMatchesResult,
  VoiceRememberParams,
  VoiceRememberResult,
  VoiceSuggestParams,
} from './types';

export interface MockReviewEnvironment {
  /** The project; throws project.notFound. */
  find: (recordingId: string) => MockProject;
  transcription: MockTranscription;
  settings: () => SettingsSnapshot;
  now: () => number;
}

export interface MockReview {
  handlers: {
    'voices.list': (params: EmptyParams) => KnownVoicesResult;
    'voices.setSuggest': (params: VoiceSuggestParams) => KnownVoicesResult;
    'voices.forget': (params: VoiceIdParams) => KnownVoicesResult;
    'voices.forgetAll': (params: EmptyParams) => KnownVoicesResult;
    'voices.remember': (params: VoiceRememberParams) => VoiceRememberResult;
    'voices.revert': (params: VoiceChangeParams) => EmptyResult;
    'voices.matches': (params: RecordingIdParams) => VoiceMatchesResult;
    'voices.decline': (params: VoiceDeclineParams) => VoiceMatchesResult;
    'voices.acceptMatch': (params: VoiceAcceptParams) => VoiceAcceptResult;
    'annotations.suggestChapters': (params: RecordingIdParams) => ChapterSuggestionsResult;
    'annotations.dismissSuggestion': (params: ChapterSuggestionParams) => ChapterSuggestionsResult;
    'annotations.restoreSuggestion': (params: ChapterSuggestionParams) => ChapterSuggestionsResult;
    'transcript.setSegmentsSpeaker': (params: TranscriptSetSegmentsSpeakerParams) => TranscriptSetSegmentsSpeakerResult;
    'transcript.swapSpeakers': (params: TranscriptSwapSpeakersParams) => TranscriptSetSegmentsSpeakerResult;
    'transcript.sameVoice': (params: TranscriptSameVoiceParams) => TranscriptSameVoiceResult;
    'transcript.splitSpeaker': (params: TranscriptSplitSpeakerParams) => TranscriptSplitSpeakerResult;
  };
  /** Adds a known voice directly (tests and the preview's sample data). */
  seedVoice: (voice: { name: string; speakerId: string; recordings?: number }) => string;
}

interface MockVoice {
  id: string;
  name: string;
  /** Confirmations: "recordingId/speakerId" → the speaker id, which stands for the voice here. */
  samples: Map<string, string>;
  recordings: Set<string>;
  declinedIn: Set<string>;
  suggest: boolean;
  lastConfirmedAt: string;
  /** Recordings counted before the preview started (sample data). */
  extraRecordings: number;
}

/** The host needs 10 s of voiced speech; the preview asks 5 s, so its short sample recordings can show a match. */
const MIN_SECONDS = 5;
const NEAR_MS = 60_000;
const MIN_RECORDING_MS = 6 * 60_000;
const SHORT_WORDS = new Set(['about', 'there', 'their', 'which', 'would', 'could', 'should', 'think', 'because', 'going', 'really', 'other', 'where', 'these', 'those', 'thing', 'things', 'right', 'maybe', 'yeah']);

const notFound = (voiceId: string): MockHostError =>
  new MockHostError('voices.notFound', 'That voice is not known any more; it may have been forgotten in Settings › Speakers. Nothing was changed.', voiceId);

function clone(voice: MockVoice): MockVoice {
  return { ...voice, samples: new Map(voice.samples), recordings: new Set(voice.recordings), declinedIn: new Set(voice.declinedIn) };
}

export function createMockReview(env: MockReviewEnvironment): MockReview {
  let voices: MockVoice[] = [];
  let counter = 0;
  const journal = new Map<string, { voiceId: string; before: MockVoice | null }[]>();
  const dismissed = new Map<string, Set<number>>();
  const iso = (): string => new Date(env.now()).toISOString();
  const remember = (): boolean => env.settings().speakers.rememberVoices;

  const info = (voice: MockVoice): KnownVoiceInfo => ({
    id: voice.id,
    name: voice.name,
    recordings: voice.recordings.size + voice.extraRecordings,
    lastConfirmedAt: voice.lastConfirmedAt,
    suggest: voice.suggest,
  });
  const list = (): KnownVoicesResult => ({
    remember: remember(),
    voices: [...voices].sort((a, b) => a.name.localeCompare(b.name)).map(info),
  });
  const voiceOf = (voiceId: string): MockVoice => {
    const voice = voices.find((v) => v.id === voiceId);
    if (voice === undefined) {
      throw notFound(voiceId);
    }
    return voice;
  };
  const purge = (voiceId: string | null): void => {
    for (const [id, entries] of journal) {
      if (voiceId === null || entries.some((e) => e.voiceId === voiceId)) {
        journal.delete(id);
      }
    }
  };

  const transcriptOf = (recordingId: string) => {
    env.find(recordingId);
    const transcript = env.transcription.get(recordingId).transcript;
    if (transcript === null) {
      throw new MockHostError('transcript.none', 'This recording has no transcript yet, so there are no speakers to remember. Nothing was changed.', recordingId);
    }
    return transcript;
  };
  // 2.6.0: what voices.json would say. The real host keeps each raw diarizer voice with the lines it won; this stand-in
  // deals every speaker's lines (as they stand when first asked) into two voices by turns, so "the lines that sound the
  // same" is half of a speaker's lines, and keeps that until the transcript's lines change (a new pass).
  const voicesOf = new Map<string, { ids: string; voices: string[][] }>();
  const voicesFor = (recordingId: string): string[][] => {
    const transcript = transcriptOf(recordingId);
    const ids = transcript.segments.map((s) => s.id).join('\n');
    let kept = voicesOf.get(recordingId);
    if (kept?.ids !== ids) {
      const voices = new Map<string, string[]>();
      const dealt = new Map<string, number>();
      for (const s of transcript.segments) {
        if (s.speaker === null) {
          continue;
        }
        const n = dealt.get(s.speaker) ?? 0;
        dealt.set(s.speaker, n + 1);
        const voice = `${s.speaker}/${n % 2}`;
        voices.set(voice, [...(voices.get(voice) ?? []), s.id]);
      }
      kept = { ids, voices: [...voices.values()] };
      voicesOf.set(recordingId, kept);
    }
    return kept.voices;
  };
  const sameVoice = (recordingId: string, segmentIds: string[]): string[] => {
    const transcript = transcriptOf(recordingId);
    const asked = new Set(segmentIds);
    const alike = new Set(voicesFor(recordingId).filter((lines) => lines.some((id) => asked.has(id))).flat());
    return transcript.segments.filter((s) => alike.has(s.id) && !asked.has(s.id)).map((s) => s.id);
  };
  // 2.6.0: "{name} is two people": of the mock voices holding the speaker's lines now, the one with less talk time
  // becomes a new speaker (lines the user placed stay). One voice, or none, cannot be split.
  const splitSpeaker = (recordingId: string, speakerId: string): TranscriptSplitSpeakerResult => {
    const transcript = transcriptOf(recordingId);
    const speaker = transcript.speakers.find((s) => s.id === speakerId);
    if (speaker === undefined) {
      throw new MockHostError('transcript.speakerNotFound', 'That speaker is not in this transcript any more; speakers may have been merged or identified again. Nothing was changed.', speakerId);
    }
    const own = new Map(transcript.segments.filter((s) => s.speaker === speakerId).map((s) => [s.id, s]));
    const voices = voicesFor(recordingId)
      .map((lines) => lines.map((id) => own.get(id)).filter((s): s is TranscriptSegment => s !== undefined))
      .filter((lines) => lines.length > 0)
      .map((lines) => ({ lines, seconds: lines.reduce((sum, s) => sum + (s.end - s.start), 0) }));
    if (voices.length < 2) {
      throw new MockHostError('transcript.cannotSplit', `${speaker.name} was heard as one voice, so there is nothing to split by sound. Move lines with their menu instead. Nothing was changed.`, speakerId);
    }
    voices.sort((a, b) => b.seconds - a.seconds);
    const moving = voices.slice(1).flatMap((v) => v.lines).filter((s) => s.speakerByUser !== true);
    if (moving.length === 0) {
      throw new MockHostError('transcript.cannotSplit', `${speaker.name}'s other voice holds only lines you placed there yourself, so nothing can move. Nothing was changed.`, speakerId);
    }
    const numbers = transcript.speakers.map((s) => Number.parseInt(s.id.replace(/^\D+/, ''), 10)).filter((n) => Number.isFinite(n));
    const prefix = transcript.speakers[0]?.id.replace(/\d+$/, '') ?? 'spk';
    const number = Math.max(0, ...numbers) + 1;
    const colours: SpeakerColour[] = [1, 2, 3, 4];
    const created: SpeakerRestore = { id: `${prefix}${number}`, name: `Speaker ${number}`, color: colours[transcript.speakers.length % 4] ?? 1, renamed: false };
    const order = new Set(moving.map((s) => s.id));
    const segmentIds = transcript.segments.filter((s) => order.has(s.id)).map((s) => s.id);
    const { speakers } = env.transcription.restoreSpeaker({ recordingId, speaker: created, segmentIds });
    const made = speakers.find((s) => s.id === created.id);
    if (made === undefined) {
      throw new MockHostError('bridge.internal', 'The mock host lost the speaker it made.');
    }
    return { speakers, created: made, segmentIds };
  };
  const talkSeconds = (segments: TranscriptSegment[], speakerId: string): number =>
    segments.filter((s) => s.speaker === speakerId).reduce((sum, s) => sum + (s.end - s.start), 0);

  const rememberSpeaker = (recordingId: string, speakerId: string): VoiceRememberResult => {
    const transcript = transcriptOf(recordingId);
    const speaker = transcript.speakers.find((s) => s.id === speakerId);
    if (speaker === undefined) {
      throw new MockHostError('transcript.speakerNotFound', 'That speaker is not in this transcript any more; speakers may have been merged or identified again. Nothing was remembered.', speakerId);
    }
    if (!remember()) {
      return { remembered: false, changeId: null, voice: null, reason: '"Remember speakers by voice" is off in Settings › Speakers, so no voice was learned.' };
    }
    if (!speaker.renamed) {
      return { remembered: false, changeId: null, voice: null, reason: `${speaker.name} has no name yet, so there is nothing to remember the voice under.` };
    }
    const seconds = talkSeconds(transcript.segments, speaker.id);
    if (seconds < MIN_SECONDS) {
      return {
        remembered: false,
        changeId: null,
        voice: null,
        reason: `${speaker.name} speaks for ${Math.round(seconds)} s with a usable voice here; Memento learns a voice from ${MIN_SECONDS} s or more, so it was not remembered.`,
      };
    }
    const key = `${recordingId}/${speaker.id}`;
    const before: { voiceId: string; before: MockVoice | null }[] = [];
    const next: MockVoice[] = [];
    for (const voice of voices) {
      if (!voice.samples.has(key)) {
        next.push(voice);
        continue;
      }
      before.push({ voiceId: voice.id, before: clone(voice) });
      const changed = clone(voice);
      changed.samples.delete(key);
      if (changed.samples.size > 0 || changed.name.toLocaleLowerCase() === speaker.name.toLocaleLowerCase()) {
        next.push(changed);
      }
    }
    let target = next.find((v) => v.name.toLocaleLowerCase() === speaker.name.toLocaleLowerCase());
    if (target === undefined) {
      counter += 1;
      target = { id: `v${String(counter).padStart(4, '0')}`, name: speaker.name, samples: new Map(), recordings: new Set(), declinedIn: new Set(), suggest: true, lastConfirmedAt: iso(), extraRecordings: 0 };
      before.push({ voiceId: target.id, before: null });
      next.push(target);
    } else if (!before.some((b) => b.voiceId === target?.id)) {
      before.push({ voiceId: target.id, before: clone(target) });
      const fresh = clone(target);
      next[next.indexOf(target)] = fresh;
      target = fresh;
    }
    target.name = speaker.name;
    target.samples.set(key, speaker.id);
    target.recordings.add(recordingId);
    target.declinedIn.delete(recordingId);
    target.lastConfirmedAt = iso();
    voices = next;
    counter += 1;
    const changeId = `change-${counter}`;
    journal.set(changeId, before);
    return { remembered: true, changeId, voice: info(target), reason: null };
  };

  const matches = (recordingId: string): VoiceMatch[] => {
    env.find(recordingId);
    if (!remember()) {
      return [];
    }
    const transcript = env.transcription.get(recordingId).transcript;
    if (transcript === null) {
      return [];
    }
    const named = new Set(transcript.speakers.filter((s) => s.renamed).map((s) => s.name.toLocaleLowerCase()));
    const found: VoiceMatch[] = [];
    for (const speaker of transcript.speakers.filter((s) => !s.renamed)) {
      if (talkSeconds(transcript.segments, speaker.id) < MIN_SECONDS) {
        continue;
      }
      const voice = voices.find(
        (v) =>
          v.suggest &&
          !v.declinedIn.has(recordingId) &&
          !named.has(v.name.toLocaleLowerCase()) &&
          !found.some((f) => f.voiceId === v.id) &&
          [...v.samples.entries()].some(([key, id]) => id === speaker.id && !key.startsWith(`${recordingId}/`)),
      );
      if (voice !== undefined) {
        found.push({ speakerId: speaker.id, voiceId: voice.id, name: voice.name, similarity: 0.86, recordings: voice.recordings.size + voice.extraRecordings });
      }
    }
    return found;
  };

  const suggestions = (recordingId: string): ChapterSuggestion[] => {
    const project = env.find(recordingId);
    const transcript = env.transcription.get(recordingId).transcript;
    const lines = (transcript?.segments ?? []).filter((s) => s.text.trim() !== '');
    if (lines.length < 20) {
      return [];
    }
    const first = lines[0];
    const last = lines[lines.length - 1];
    if (first === undefined || last === undefined || (last.end - first.start) * 1000 < MIN_RECORDING_MS) {
      return [];
    }
    const span = last.end - first.start;
    const starts: TranscriptSegment[] = [first];
    for (const quarter of [0.25, 0.5, 0.75]) {
      const at = first.start + span * quarter;
      const line = lines.find((s) => s.start >= at);
      if (line !== undefined && !starts.includes(line)) {
        starts.push(line);
      }
    }
    const taken = [...project.chapters.map((c) => c.atMs), ...(dismissed.get(recordingId) ?? [])];
    const titles: string[] = [];
    return starts
      .map((line, i) => {
        const end = starts[i + 1]?.start ?? Number.POSITIVE_INFINITY;
        const counts = new Map<string, number>();
        for (const s of lines.filter((l) => l.start >= line.start && l.start < end)) {
          for (const word of s.text.toLowerCase().match(/[a-z]{5,}/g) ?? []) {
            if (!SHORT_WORDS.has(word)) {
              counts.set(word, (counts.get(word) ?? 0) + 1);
            }
          }
        }
        const best = [...counts.entries()].sort((a, b) => b[1] - a[1] || a[0].localeCompare(b[0])).map(([w]) => w).find((w) => !titles.includes(w)) ?? `part ${i + 1}`;
        titles.push(best);
        const atMs = Math.round(line.start * 1000);
        // 2.7.0: the agenda's items, in order, name the chapters (the real host places them by what is said).
        const item = project.details.agenda.items[i]?.text.trim();
        if (item !== undefined && item !== '') {
          return { id: `sc${atMs}`, atMs, title: item, basis: `Agenda item ${i + 1}`, fromAgenda: true };
        }
        return { id: `sc${atMs}`, atMs, title: best.charAt(0).toUpperCase() + best.slice(1), basis: i === 0 ? 'Start of the recording' : 'The subject changes', fromAgenda: false };
      })
      .filter((s) => taken.every((t) => Math.abs(t - s.atMs) >= NEAR_MS));
  };

  return {
    seedVoice: ({ name, speakerId, recordings = 1 }) => {
      counter += 1;
      const id = `v${String(counter).padStart(4, '0')}`;
      voices.push({
        id,
        name,
        samples: new Map([[`seed/${speakerId}`, speakerId]]),
        recordings: new Set(),
        declinedIn: new Set(),
        suggest: true,
        lastConfirmedAt: iso(),
        extraRecordings: recordings,
      });
      return id;
    },
    handlers: {
      'voices.list': () => list(),
      'voices.setSuggest': ({ voiceId, suggest }) => {
        voiceOf(voiceId).suggest = suggest;
        return list();
      },
      'voices.forget': ({ voiceId }) => {
        voiceOf(voiceId);
        voices = voices.filter((v) => v.id !== voiceId);
        purge(voiceId);
        return list();
      },
      'voices.forgetAll': () => {
        voices = [];
        purge(null);
        return list();
      },
      'voices.remember': ({ recordingId, speakerId }) => rememberSpeaker(recordingId, speakerId),
      'voices.revert': ({ changeId }) => {
        const entries = journal.get(changeId);
        if (entries === undefined) {
          throw new MockHostError('voices.notFound', 'That voice change cannot be taken back any more: Memento was restarted since, or the voice was forgotten. Nothing was changed.', changeId);
        }
        for (const { voiceId, before } of entries) {
          const index = voices.findIndex((v) => v.id === voiceId);
          if (before === null) {
            voices = voices.filter((v) => v.id !== voiceId);
          } else if (index >= 0) {
            const current = voices[index];
            voices[index] = { ...clone(before), suggest: current?.suggest ?? before.suggest, declinedIn: new Set(current?.declinedIn ?? before.declinedIn) };
          } else {
            voices.push(clone(before));
          }
        }
        journal.delete(changeId);
        return {};
      },
      'voices.matches': ({ recordingId }) => ({ matches: matches(recordingId) }),
      'voices.decline': ({ recordingId, voiceId, declined }) => {
        env.find(recordingId);
        const voice = voiceOf(voiceId);
        if (declined) {
          voice.declinedIn.add(recordingId);
        } else {
          voice.declinedIn.delete(recordingId);
        }
        return { matches: matches(recordingId) };
      },
      'voices.acceptMatch': ({ recordingId, speakerId, voiceId }) => {
        const voice = voiceOf(voiceId);
        const { speakers } = env.transcription.renameSpeaker({ recordingId, speakerId, name: voice.name });
        const { changeId } = rememberSpeaker(recordingId, speakerId);
        return { speakers, changeId };
      },
      'annotations.suggestChapters': ({ recordingId }) => ({ suggestions: suggestions(recordingId) }),
      'annotations.dismissSuggestion': ({ recordingId, atMs }) => {
        env.find(recordingId);
        dismissed.set(recordingId, new Set([...(dismissed.get(recordingId) ?? []), atMs]));
        return { suggestions: suggestions(recordingId) };
      },
      'annotations.restoreSuggestion': ({ recordingId, atMs }) => {
        env.find(recordingId);
        dismissed.get(recordingId)?.delete(atMs);
        return { suggestions: suggestions(recordingId) };
      },
      'transcript.setSegmentsSpeaker': ({ recordingId, segmentIds, speakerId, newSpeakerName }) => {
        if (segmentIds.length === 0) {
          throw new MockHostError('bridge.invalidParams', 'Choose 1 to 100000 lines.');
        }
        const transcript = transcriptOf(recordingId);
        for (const id of segmentIds) {
          if (!transcript.segments.some((s) => s.id === id)) {
            throw new MockHostError('transcript.segmentNotFound', 'That line is not in the transcript any more; it may have been transcribed again. Nothing was changed.', id);
          }
        }
        let target = speakerId;
        let speakers: Speaker[] = transcript.speakers;
        const [head, ...rest] = segmentIds;
        if (newSpeakerName !== undefined && head !== undefined) {
          const created = env.transcription.setSegmentSpeaker({ recordingId, segmentId: head, speakerId: null, newSpeakerName });
          target = created.segment.speaker;
          speakers = created.speakers;
        }
        for (const id of newSpeakerName !== undefined ? rest : segmentIds) {
          speakers = env.transcription.setSegmentSpeaker({ recordingId, segmentId: id, speakerId: target }).speakers;
        }
        const wanted = new Set(segmentIds);
        const after = env.transcription.get(recordingId).transcript;
        return { segments: (after?.segments ?? []).filter((s) => wanted.has(s.id)), speakers };
      },
      // 2.6.0: two speakers trade their lines, everywhere or from a line on; the same call again puts them back.
      'transcript.swapSpeakers': ({ recordingId, speakerId, otherSpeakerId, fromSegmentId }) => {
        if (speakerId === otherSpeakerId) {
          throw new MockHostError('bridge.invalidParams', 'Choose two different speakers.');
        }
        const transcript = transcriptOf(recordingId);
        for (const id of [speakerId, otherSpeakerId]) {
          if (!transcript.speakers.some((s) => s.id === id)) {
            throw new MockHostError('transcript.speakerNotFound', 'That speaker is not in this transcript any more; speakers may have been merged or identified again. Nothing was changed.', id);
          }
        }
        const from = fromSegmentId ?? null;
        if (from !== null && !transcript.segments.some((s) => s.id === from)) {
          throw new MockHostError('transcript.segmentNotFound', 'That line is not in the transcript any more; it may have been transcribed again. Nothing was changed.', from);
        }
        let started = from === null;
        const moves: { id: string; to: string }[] = [];
        for (const s of transcript.segments) {
          started ||= s.id === from;
          if (started && (s.speaker === speakerId || s.speaker === otherSpeakerId)) {
            moves.push({ id: s.id, to: s.speaker === speakerId ? otherSpeakerId : speakerId });
          }
        }
        let speakers: Speaker[] = transcript.speakers;
        for (const move of moves) {
          speakers = env.transcription.setSegmentSpeaker({ recordingId, segmentId: move.id, speakerId: move.to }).speakers;
        }
        const wanted = new Set(moves.map((m) => m.id));
        const after = env.transcription.get(recordingId).transcript;
        return { segments: (after?.segments ?? []).filter((s) => wanted.has(s.id)), speakers };
      },
      // 2.6.0: the other lines heard as the same voice as these (reads only).
      'transcript.sameVoice': ({ recordingId, segmentIds }) => {
        if (segmentIds.length === 0) {
          throw new MockHostError('bridge.invalidParams', 'Choose 1 to 100000 lines.');
        }
        const transcript = transcriptOf(recordingId);
        for (const id of segmentIds) {
          if (!transcript.segments.some((s) => s.id === id)) {
            throw new MockHostError('transcript.segmentNotFound', 'That line is not in the transcript any more; it may have been transcribed again. Nothing was changed.', id);
          }
        }
        return { segmentIds: sameVoice(recordingId, segmentIds) };
      },
      // 2.6.0: a speaker's voices grouped into two by sound; the smaller half becomes a new speaker.
      'transcript.splitSpeaker': ({ recordingId, speakerId }) => splitSpeaker(recordingId, speakerId),
    },
  };
}
