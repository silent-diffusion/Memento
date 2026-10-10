// The 2.0 Review actions (DESIGN.md §19), each registered with Undo (state/undo.ts) with its inverse, like
// reviewActions.ts: the known-voice match prompt (Use name, Not {name}), suggested chapters (accept one, accept all,
// dismiss) and what selection mode does to several lines at once (assign a speaker, highlight, remove highlights,
// mark a chapter start). Each calls the host, then registers; a refusal registers nothing.
import type { BridgeClient } from '../../bridge/client';
import type { ChapterSuggestion, Highlight, Project, Speaker, SpeakerRestore, TranscriptSegment, VoiceMatch } from '../../bridge/types';
import type { UndoManager } from '../../state/undo';
import type { ReviewActions } from './reviewActions';
import type { TranscriptApi } from './useTranscript';

export interface Review20Deps {
  bridge: BridgeClient;
  recordingId: string;
  undo: UndoManager;
  api: TranscriptApi;
  actions: ReviewActions;
  project: Project | null;
  segments: readonly TranscriptSegment[];
  speakers: readonly Speaker[];
  patch: (changes: Partial<Project>) => void;
  /** Reads voices.matches / annotations.suggestChapters again (after an undo that changed them). */
  refreshMatches: () => void;
  refreshSuggestions: () => void;
}

export interface Review20Actions {
  /** "Use name": names the speaker after the known voice and refines the voice. */
  acceptMatch(match: VoiceMatch, speaker: Speaker): Promise<void>;
  /** "Not {name}": the suggestion goes away for this recording. */
  declineMatch(match: VoiceMatch): Promise<void>;
  acceptSuggestion(suggestion: ChapterSuggestion): Promise<void>;
  acceptAllSuggestions(suggestions: readonly ChapterSuggestion[]): Promise<void>;
  dismissSuggestion(suggestion: ChapterSuggestion): Promise<void>;
  /** Several lines to one speaker (or a new one), in one host write. */
  assignLines(lines: readonly TranscriptSegment[], speaker: Speaker): Promise<void>;
  addSpeakerToLines(lines: readonly TranscriptSegment[], name: string): Promise<void>;
  /** A highlight on each line that has none; resolves to how many were added. */
  highlightLines(lines: readonly TranscriptSegment[]): Promise<number>;
  /** Removes every highlight on these lines; resolves to how many. */
  removeLineHighlights(highlights: readonly Highlight[]): Promise<number>;
  /** A chapter starting at the line, titled with its first words. */
  chapterAtLine(line: TranscriptSegment): Promise<void>;
}

function restoreOf(speaker: Speaker): SpeakerRestore {
  return { id: speaker.id, name: speaker.name, color: speaker.color, renamed: speaker.renamed };
}

/** "So the budget for next year is…" → "So the budget for next year". */
export function chapterTitleFromLine(text: string): string {
  const words = text.trim().split(/\s+/).filter((w) => w !== '');
  const title = words.slice(0, 6).join(' ').replace(/[,.;:!?…]+$/, '');
  return title.length > 60 ? `${title.slice(0, 59).trimEnd()}…` : title;
}

/** Lines grouped by the speaker they have now (null: none), to put back in one call per speaker. */
function bySpeaker(lines: readonly TranscriptSegment[]): Map<string | null, string[]> {
  const groups = new Map<string | null, string[]>();
  for (const line of lines) {
    groups.set(line.speaker, [...(groups.get(line.speaker) ?? []), line.id]);
  }
  return groups;
}

export function createReview20Actions(get: () => Review20Deps): Review20Actions {
  const deps = get;

  const setLines = async (segmentIds: string[], speakerId: string | null, newSpeakerName?: string) => {
    const { bridge, recordingId } = deps();
    return bridge.call('transcript.setSegmentsSpeaker', { recordingId, segmentIds, speakerId, ...(newSpeakerName === undefined ? {} : { newSpeakerName }) });
  };
  const putBack = async (groups: Map<string | null, string[]>): Promise<void> => {
    for (const [speakerId, ids] of groups) {
      await setLines(ids, speakerId);
    }
  };
  const addHighlightRaw = async (highlight: Partial<Highlight>): Promise<string> => {
    const { bridge, recordingId, patch, project } = deps();
    const before = new Set((project?.highlights ?? []).map((h) => h.id));
    const { highlights } = await bridge.call('annotations.addHighlight', { recordingId, highlight });
    patch({ highlights });
    // The deps hold the project of the last render; the next add must see this one too.
    const fresh = highlights.find((h) => !before.has(h.id));
    if (project !== null) {
      project.highlights = highlights;
    }
    return fresh?.id ?? '';
  };
  const removeHighlightRaw = async (id: string): Promise<void> => {
    const { bridge, recordingId, patch, undo, project } = deps();
    const { highlights } = await bridge.call('annotations.removeHighlight', { recordingId, highlightId: undo.resolveId(id) });
    patch({ highlights });
    if (project !== null) {
      project.highlights = highlights;
    }
  };
  const addChapterRaw = async (atMs: number, title: string): Promise<string> => {
    const { bridge, recordingId, patch, project } = deps();
    const before = new Set((project?.chapters ?? []).map((c) => c.id));
    const { chapters } = await bridge.call('annotations.addChapter', { recordingId, chapter: { atMs, title, origin: 'local' } });
    patch({ chapters });
    if (project !== null) {
      project.chapters = chapters;
    }
    return chapters.find((c) => !before.has(c.id))?.id ?? '';
  };
  const removeChapterRaw = async (id: string): Promise<void> => {
    const { bridge, recordingId, patch, undo, project } = deps();
    const { chapters } = await bridge.call('annotations.removeChapter', { recordingId, chapterId: undo.resolveId(id) });
    patch({ chapters });
    if (project !== null) {
      project.chapters = chapters;
    }
  };

  return {
    async acceptMatch(match, speaker) {
      const { bridge, recordingId, undo, refreshMatches } = deps();
      const before = restoreOf(speaker);
      let { changeId } = await bridge.call('voices.acceptMatch', { recordingId, speakerId: speaker.id, voiceId: match.voiceId });
      refreshMatches();
      undo.push({
        label: `use the name ${match.name}`,
        undo: async () => {
          await deps().api.restoreSpeaker(before, []);
          if (changeId !== null) {
            await deps().bridge.call('voices.revert', { changeId });
            changeId = null;
          }
          deps().refreshMatches();
        },
        redo: async () => {
          ({ changeId } = await deps().bridge.call('voices.acceptMatch', { recordingId, speakerId: speaker.id, voiceId: match.voiceId }));
          deps().refreshMatches();
        },
      });
    },

    async declineMatch(match) {
      const { bridge, recordingId, undo, refreshMatches } = deps();
      await bridge.call('voices.decline', { recordingId, voiceId: match.voiceId, declined: true });
      refreshMatches();
      const set = async (declined: boolean): Promise<void> => {
        await deps().bridge.call('voices.decline', { recordingId, voiceId: match.voiceId, declined });
        deps().refreshMatches();
      };
      undo.push({ label: `not ${match.name}`, undo: () => set(false), redo: () => set(true) });
    },

    async acceptSuggestion(suggestion) {
      const id = await addChapterRaw(suggestion.atMs, suggestion.title);
      deps().undo.push({
        label: 'accept the suggested chapter',
        undo: () => removeChapterRaw(id),
        redo: async () => {
          deps().undo.aliasId(id, await addChapterRaw(suggestion.atMs, suggestion.title));
        },
      });
    },

    async acceptAllSuggestions(suggestions) {
      const ids: string[] = [];
      for (const s of suggestions) {
        ids.push(await addChapterRaw(s.atMs, s.title));
      }
      if (ids.length === 0) {
        return;
      }
      deps().undo.push({
        label: `accept ${ids.length} suggested ${ids.length === 1 ? 'chapter' : 'chapters'}`,
        undo: async () => {
          for (const id of [...ids].reverse()) {
            await removeChapterRaw(id);
          }
        },
        redo: async () => {
          for (const [i, s] of suggestions.entries()) {
            const id = ids[i];
            if (id !== undefined) {
              deps().undo.aliasId(id, await addChapterRaw(s.atMs, s.title));
            }
          }
        },
      });
    },

    async dismissSuggestion(suggestion) {
      const { bridge, recordingId, undo, refreshSuggestions } = deps();
      await bridge.call('annotations.dismissSuggestion', { recordingId, atMs: suggestion.atMs });
      refreshSuggestions();
      const call = async (method: 'annotations.dismissSuggestion' | 'annotations.restoreSuggestion'): Promise<void> => {
        await deps().bridge.call(method, { recordingId, atMs: suggestion.atMs });
        deps().refreshSuggestions();
      };
      undo.push({
        label: 'dismiss the suggested chapter',
        undo: () => call('annotations.restoreSuggestion'),
        redo: () => call('annotations.dismissSuggestion'),
      });
    },

    async assignLines(lines, speaker) {
      const ids = lines.map((l) => l.id);
      const before = bySpeaker(lines);
      await setLines(ids, speaker.id);
      deps().undo.push({
        label: `move ${ids.length} ${ids.length === 1 ? 'line' : 'lines'} to ${speaker.name}`,
        undo: () => putBack(before),
        redo: () => setLines(ids, speaker.id),
      });
    },

    async addSpeakerToLines(lines, name) {
      const ids = lines.map((l) => l.id);
      const before = bySpeaker(lines);
      const answer = await setLines(ids, null, name);
      const createdId = answer.segments[0]?.speaker ?? null;
      const created = answer.speakers.find((s) => s.id === createdId);
      if (created === undefined) {
        return;
      }
      const restore = restoreOf(created);
      deps().undo.push({
        label: `add speaker ${created.name}`,
        undo: async () => {
          await putBack(before);
          await deps().api.removeSpeaker(created.id);
        },
        redo: () => deps().api.restoreSpeaker(restore, ids),
      });
    },

    async highlightLines(lines) {
      const { project } = deps();
      const marked = new Set((project?.highlights ?? []).map((h) => h.segmentId));
      const todo = lines.filter((l) => !marked.has(l.id));
      const made: { id: string; line: TranscriptSegment }[] = [];
      for (const line of todo) {
        made.push({ id: await addHighlightRaw({ atMs: Math.round(line.start * 1000), note: '', segmentId: line.id }), line });
      }
      if (made.length > 0) {
        deps().undo.push({
          label: made.length === 1 ? 'add highlight' : `highlight ${made.length} lines`,
          undo: async () => {
            for (const m of [...made].reverse()) {
              await removeHighlightRaw(m.id);
            }
          },
          redo: async () => {
            for (const m of made) {
              deps().undo.aliasId(m.id, await addHighlightRaw({ atMs: Math.round(m.line.start * 1000), note: '', segmentId: m.line.id }));
            }
          },
        });
      }
      return made.length;
    },

    async removeLineHighlights(highlights) {
      const removed = [...highlights];
      for (const h of removed) {
        await removeHighlightRaw(h.id);
      }
      if (removed.length > 0) {
        deps().undo.push({
          label: removed.length === 1 ? 'remove highlight' : `remove ${removed.length} highlights`,
          undo: async () => {
            for (const h of removed) {
              const { atMs, note, origin, segmentId } = h;
              deps().undo.aliasId(h.id, await addHighlightRaw({ atMs, note, origin, segmentId }));
            }
          },
          redo: async () => {
            for (const h of removed) {
              await removeHighlightRaw(h.id);
            }
          },
        });
      }
      return removed.length;
    },

    async chapterAtLine(line) {
      await deps().actions.addChapter(Math.round(line.start * 1000), chapterTitleFromLine(line.text));
    },
  };
}
