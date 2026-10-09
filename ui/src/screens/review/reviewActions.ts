// Everything Review changes, each registered with Undo (state/undo.ts) together with its inverse:
// transcript lines, speakers (move a line, add, rename, merge), highlights, chapters, topics, people,
// tags and the reviewed mark. Each action calls the host, applies the answer, then registers; it
// rejects with the host's error and registers nothing when the host refuses. The inverses call the
// same host methods (and transcript.restoreSpeaker / removeSpeaker), so an undo can be refused too.
import type { BridgeClient } from '../../bridge/client';
import type { Chapter, Highlight, Project, RecordingDetails, Speaker, SpeakerRestore, Topic, Transcript, TranscriptSegment, WhoSpoke } from '../../bridge/types';
import type { UndoManager } from '../../state/undo';
import type { TranscriptApi } from './useTranscript';

export interface ReviewActionDeps {
  bridge: BridgeClient;
  recordingId: string;
  undo: UndoManager;
  api: TranscriptApi;
  transcript: Transcript | null;
  project: Project | null;
  /** Applies part of the project from the host's answer. */
  patch: (changes: Partial<Project>) => void;
  setProject: (project: Project) => void;
}

export interface ReviewActions {
  editLine(segment: TranscriptSegment, text: string): Promise<void>;
  assignSpeaker(segment: TranscriptSegment, speaker: Speaker): Promise<void>;
  addSpeaker(segment: TranscriptSegment, name: string): Promise<void>;
  renameSpeaker(speaker: Speaker, name: string): Promise<void>;
  mergeSpeakers(from: Speaker, into: Speaker): Promise<void>;
  /** Merges the most alike speakers until `count` are left (one Undo step); resolves to how many were merged and who is left. */
  reduceSpeakers(count: number): Promise<{ merged: number; left: Speaker[] }>;
  /** The recording's own speaker count and names (Who spoke). */
  setWhoSpoke(whoSpoke: WhoSpoke): Promise<void>;
  addHighlight(atMs: number): Promise<void>;
  renameHighlight(highlight: Highlight, note: string): Promise<void>;
  removeHighlight(highlight: Highlight): Promise<void>;
  addChapter(atMs: number, title: string): Promise<void>;
  renameChapter(chapter: Chapter, title: string): Promise<void>;
  removeChapter(chapter: Chapter): Promise<void>;
  addTopic(label: string): Promise<void>;
  removeTopic(topic: Topic): Promise<void>;
  renamePerson(index: number, name: string): Promise<void>;
  setTags(tags: string[]): Promise<void>;
  markReviewed(reviewed: boolean): Promise<void>;
}

function restoreOf(speaker: Speaker): SpeakerRestore {
  return { id: speaker.id, name: speaker.name, color: speaker.color, renamed: speaker.renamed };
}

/** `get` returns what the screen holds now, so actions and their inverses always use the latest state. */
export function createReviewActions(get: () => ReviewActionDeps): ReviewActions {
  const deps = get;
  const params = <T extends object>(rest: T): T & { recordingId: string } => ({ recordingId: deps().recordingId, ...rest });

  // Annotations: the host answers the whole list; an add is found by the id that is new.
  const addHighlightRaw = async (highlight: Partial<Highlight>): Promise<string> => {
    const { bridge, project, patch } = deps();
    const before = new Set((project?.highlights ?? []).map((h) => h.id));
    const { highlights } = await bridge.call('annotations.addHighlight', params({ highlight }));
    patch({ highlights });
    return highlights.find((h) => !before.has(h.id))?.id ?? '';
  };
  const updateHighlight = async (id: string, note: string): Promise<void> => {
    const { bridge, patch, undo } = deps();
    const { highlights } = await bridge.call('annotations.updateHighlight', params({ highlight: { id: undo.resolveId(id), note } }));
    patch({ highlights });
  };
  const removeHighlightRaw = async (id: string): Promise<void> => {
    const { bridge, patch, undo } = deps();
    const { highlights } = await bridge.call('annotations.removeHighlight', params({ highlightId: undo.resolveId(id) }));
    patch({ highlights });
  };
  const addChapterRaw = async (chapter: Partial<Chapter>): Promise<string> => {
    const { bridge, project, patch } = deps();
    const before = new Set((project?.chapters ?? []).map((c) => c.id));
    const { chapters } = await bridge.call('annotations.addChapter', params({ chapter }));
    patch({ chapters });
    return chapters.find((c) => !before.has(c.id))?.id ?? '';
  };
  const updateChapter = async (id: string, title: string): Promise<void> => {
    const { bridge, patch, undo } = deps();
    const { chapters } = await bridge.call('annotations.updateChapter', params({ chapter: { id: undo.resolveId(id), title } }));
    patch({ chapters });
  };
  const removeChapterRaw = async (id: string): Promise<void> => {
    const { bridge, patch, undo } = deps();
    const { chapters } = await bridge.call('annotations.removeChapter', params({ chapterId: undo.resolveId(id) }));
    patch({ chapters });
  };
  const addTopicRaw = async (topic: Partial<Topic>): Promise<string> => {
    const { bridge, project, patch } = deps();
    const before = new Set((project?.topics ?? []).map((t) => t.id));
    const { topics } = await bridge.call('annotations.addTopic', params({ topic }));
    patch({ topics });
    return topics.find((t) => !before.has(t.id))?.id ?? '';
  };
  const removeTopicRaw = async (id: string): Promise<void> => {
    const { bridge, patch, undo } = deps();
    const { topics } = await bridge.call('annotations.removeTopic', params({ topicId: undo.resolveId(id) }));
    patch({ topics });
  };
  const updateDetails = async (details: Partial<RecordingDetails>): Promise<void> => {
    const { bridge, setProject } = deps();
    setProject(await bridge.call('project.updateDetails', params({ details })));
  };
  const markReviewedRaw = async (reviewed: boolean): Promise<void> => {
    const message = await deps().api.markReviewed(reviewed);
    if (message !== null) {
      throw new Error(message);
    }
  };

  /** Re-creates something removed and points its old id at the new one. */
  const recreate = async (id: string, add: () => Promise<string>): Promise<void> => {
    const fresh = await add();
    deps().undo.aliasId(id, fresh);
  };

  return {
    async editLine(segment, text) {
      const { api, undo } = deps();
      const before = segment.text;
      await api.editSegment(segment.id, text);
      undo.push({
        label: 'edit line',
        undo: () => deps().api.editSegment(segment.id, before),
        redo: () => deps().api.editSegment(segment.id, text),
      });
    },

    async assignSpeaker(segment, speaker) {
      const { api, undo } = deps();
      const before = segment.speaker;
      await api.setSpeaker(segment.id, speaker.id);
      undo.push({
        label: `move line to ${speaker.name}`,
        undo: () => deps().api.setSpeaker(segment.id, before),
        redo: () => deps().api.setSpeaker(segment.id, speaker.id),
      });
    },

    async addSpeaker(segment, name) {
      const { api, undo } = deps();
      const before = segment.speaker;
      const answer = await api.setSpeaker(segment.id, null, name);
      const created = answer.speakers.find((s) => s.id === answer.segment.speaker);
      if (created === undefined) {
        return;
      }
      const restore = restoreOf(created);
      undo.push({
        label: `add speaker ${created.name}`,
        undo: async () => {
          await deps().api.setSpeaker(segment.id, before);
          await deps().api.removeSpeaker(created.id);
        },
        redo: () => deps().api.restoreSpeaker(restore, [segment.id]),
      });
    },

    async renameSpeaker(speaker, name) {
      const { api, undo } = deps();
      const before = restoreOf(speaker);
      await api.renameSpeaker(speaker.id, name);
      undo.push({
        label: 'rename speaker',
        undo: () => deps().api.restoreSpeaker(before, []),
        redo: () => deps().api.renameSpeaker(speaker.id, name),
      });
    },

    async mergeSpeakers(from, into) {
      const { api, undo, transcript } = deps();
      const lines = (transcript?.segments ?? []).filter((s) => s.speaker === from.id).map((s) => s.id);
      const before = restoreOf(from);
      await api.mergeSpeakers(from.id, into.id);
      undo.push({
        label: 'merge speakers',
        undo: () => deps().api.restoreSpeaker(before, lines),
        redo: () => deps().api.mergeSpeakers(from.id, into.id),
      });
    },

    async reduceSpeakers(count) {
      const { api, undo } = deps();
      const { merged, speakers } = await api.reduceSpeakers(count);
      if (merged.length === 0) {
        return { merged: 0, left: speakers };
      }
      // Undo puts every merged speaker back, the last merge first, in one host call.
      const restores = [...merged].reverse().map((m) => ({ speaker: m.speaker, segmentIds: m.segmentIds }));
      undo.push({
        label: `reduce to ${count} ${count === 1 ? 'speaker' : 'speakers'}`,
        undo: () => deps().api.restoreSpeakers(restores),
        redo: () => deps().api.reduceSpeakers(count),
      });
      return { merged: merged.length, left: speakers };
    },

    async setWhoSpoke(whoSpoke) {
      const before = deps().project?.details.whoSpoke ?? { count: null, names: [] };
      await updateDetails({ whoSpoke });
      deps().undo.push(
        {
          label: 'change who spoke',
          undo: () => updateDetails({ whoSpoke: before }),
          redo: () => updateDetails({ whoSpoke }),
        },
        { mergeKey: 'who-spoke' },
      );
    },

    async addHighlight(atMs) {
      const id = await addHighlightRaw({ atMs, note: '' });
      deps().undo.push({
        label: 'add highlight',
        undo: () => removeHighlightRaw(id),
        redo: () => recreate(id, () => addHighlightRaw({ atMs, note: '' })),
      });
    },

    async renameHighlight(highlight, note) {
      const before = highlight.note;
      await updateHighlight(highlight.id, note);
      deps().undo.push({
        label: 'rename highlight',
        undo: () => updateHighlight(highlight.id, before),
        redo: () => updateHighlight(highlight.id, note),
      });
    },

    async removeHighlight(highlight) {
      await removeHighlightRaw(highlight.id);
      const { atMs, note, origin, segmentId } = highlight;
      deps().undo.push({
        label: 'remove highlight',
        undo: () => recreate(highlight.id, () => addHighlightRaw({ atMs, note, origin, segmentId })),
        redo: () => removeHighlightRaw(highlight.id),
      });
    },

    async addChapter(atMs, title) {
      const id = await addChapterRaw({ atMs, title });
      deps().undo.push({
        label: 'add chapter',
        undo: () => removeChapterRaw(id),
        redo: () => recreate(id, () => addChapterRaw({ atMs, title })),
      });
    },

    async renameChapter(chapter, title) {
      const before = chapter.title;
      await updateChapter(chapter.id, title);
      deps().undo.push({
        label: 'rename chapter',
        undo: () => updateChapter(chapter.id, before),
        redo: () => updateChapter(chapter.id, title),
      });
    },

    async removeChapter(chapter) {
      await removeChapterRaw(chapter.id);
      const { atMs, title, origin } = chapter;
      deps().undo.push({
        label: 'remove chapter',
        undo: () => recreate(chapter.id, () => addChapterRaw({ atMs, title, origin })),
        redo: () => removeChapterRaw(chapter.id),
      });
    },

    async addTopic(label) {
      const id = await addTopicRaw({ label });
      if (id === '') {
        return; // The recording already had it: nothing changed.
      }
      deps().undo.push({
        label: 'add topic',
        undo: () => removeTopicRaw(id),
        redo: () => recreate(id, () => addTopicRaw({ label })),
      });
    },

    async removeTopic(topic) {
      await removeTopicRaw(topic.id);
      deps().undo.push({
        label: 'remove topic',
        undo: () => recreate(topic.id, () => addTopicRaw({ label: topic.label, origin: topic.origin })),
        redo: () => removeTopicRaw(topic.id),
      });
    },

    async renamePerson(index, name) {
      const before = deps().project?.details.participants ?? [];
      const after = before.map((p, i) => (i === index ? name : p));
      await updateDetails({ participants: after });
      deps().undo.push({
        label: 'rename person',
        undo: () => updateDetails({ participants: before }),
        redo: () => updateDetails({ participants: after }),
      });
    },

    async setTags(tags) {
      const before = deps().project?.details.tags ?? [];
      await updateDetails({ tags });
      deps().undo.push({
        label: 'change tags',
        undo: () => updateDetails({ tags: before }),
        redo: () => updateDetails({ tags }),
      });
    },

    async markReviewed(reviewed) {
      await markReviewedRaw(reviewed);
      deps().undo.push({
        label: reviewed ? 'mark as reviewed' : 'mark as not reviewed',
        undo: () => markReviewedRaw(!reviewed),
        redo: () => markReviewedRaw(reviewed),
      });
    },
  };
}
