// The Builder's unsaved work, kept beside the store while the Style editor is open so Back returns
// to the same structure, inputs, provider and tab. Leaving the Builder by its back control drops it.
import type { Template } from '../../bridge/types';
import type { AppStore } from '../../state/store';
import type { StructureState } from './structureState';

export type BuilderTab = 'preview' | 'inputs';

export interface BuilderDraft {
  /** Which Builder it belongs to: recording, template and regenerate target. */
  key: string;
  template: Template;
  structure: StructureState;
  tab: BuilderTab;
}

const drafts = new WeakMap<AppStore, { current: BuilderDraft | null }>();

export function draftOf(store: AppStore): { current: BuilderDraft | null } {
  let draft = drafts.get(store);
  if (draft === undefined) {
    draft = { current: null };
    drafts.set(store, draft);
  }
  return draft;
}

export function draftKey(recordingId: string | null, templateId: string | null, documentId: string | null): string {
  return `${recordingId ?? '-'}|${templateId ?? '-'}|${documentId ?? '-'}`;
}
