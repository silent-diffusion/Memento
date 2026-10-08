// The Builder's part of Undo (state/undo.ts, DESIGN.md §10): every change to the structure or the
// template's settings is a step holding the template and rows before and after it. The steps go
// through the Builder that is open for their scope, so the stack survives a trip to the Style editor
// (the draft keeps the Builder's state, and the Builder that opens again takes over).
import type { ModuleInfo, ModuleSettings, Template } from '../../bridge/types';
import type { UndoManager } from '../../state/undo';
import type { Rows, StructureAction } from './structureState';

export interface BuilderSnapshot {
  template: Template;
  rows: Rows;
}

const appliers = new Map<string, (snapshot: BuilderSnapshot) => void>();

/** The open Builder for `scope` applies snapshots; returns the unbinding. */
export function bindBuilderUndo(scope: string, apply: (snapshot: BuilderSnapshot) => void): () => void {
  appliers.set(scope, apply);
  return () => {
    if (appliers.get(scope) === apply) {
      appliers.delete(scope);
    }
  };
}

function applyTo(scope: string, snapshot: BuilderSnapshot): void {
  const apply = appliers.get(scope);
  if (apply === undefined) {
    throw new Error('The builder is not open, so the change was not undone. Open the builder again and press Undo there.');
  }
  apply(snapshot);
}

/** Registers a change as one step; `mergeKey` joins typing in one field into one step. */
export function recordBuilderStep(undo: UndoManager, scope: string, before: BuilderSnapshot, after: BuilderSnapshot, step: { label: string; mergeKey?: string }): void {
  undo.push(
    {
      label: step.label,
      undo: () => {
        applyTo(scope, before);
      },
      redo: () => {
        applyTo(scope, after);
      },
    },
    step.mergeKey === undefined ? {} : { mergeKey: step.mergeKey },
  );
}

const FIELD_WORDS: Record<string, string> = {
  instructions: 'instructions',
  length: 'length',
  textSize: 'text size',
  linkToTranscript: 'link to transcript',
  customTitle: 'heading',
  customText: 'text',
};

/** Fields typed into: consecutive edits are one step. */
const TYPED: ReadonlySet<string> = new Set(['instructions', 'customTitle', 'customText']);

/** How a structure change reads on the Undo button ("remove Agenda"), or null for one that is not a step. */
export function structureStep(
  rows: Rows,
  action: StructureAction,
  nameOf: (module: ModuleSettings) => string,
): { label: string; mergeKey?: string } | null {
  const named = (id: string): string => {
    const module = rows.flat().find((m) => m.id === id);
    return module === undefined ? 'module' : nameOf(module);
  };
  const paletteName = (info: ModuleInfo): string => info.name;
  switch (action.type) {
    case 'append':
      return { label: `add ${paletteName(action.module)}` };
    case 'dropRow':
    case 'dropBeside':
      return action.payload.kind === 'palette' ? { label: `add ${paletteName(action.payload.module)}` } : { label: `move ${named(action.payload.id)}` };
    case 'up':
    case 'down':
    case 'left':
    case 'right':
      return { label: `move ${named(action.id)}` };
    case 'remove':
      return { label: `remove ${named(action.id)}` };
    case 'update': {
      const fields = Object.keys(action.patch);
      const field = fields.length === 1 ? (fields[0] ?? '') : '';
      const words = FIELD_WORDS[field] ?? 'settings';
      return { label: `change ${named(action.id)} ${words}`, ...(TYPED.has(field) ? { mergeKey: `${field}:${action.id}` } : {}) };
    }
    case 'load':
    case 'select':
    case 'restore':
      return null;
  }
}

/** How a change to the template's own settings reads on the Undo button. */
export function templateStep(patch: Partial<Template>): { label: string; mergeKey?: string } {
  if ('name' in patch) {
    return { label: 'rename template', mergeKey: 'template-name' };
  }
  if ('inputs' in patch) {
    return { label: 'change what the AI receives' };
  }
  if ('providerId' in patch) {
    return { label: 'change provider' };
  }
  if ('styleId' in patch) {
    return { label: 'change style' };
  }
  if ('output' in patch) {
    return { label: 'change output' };
  }
  return { label: 'change template' };
}
