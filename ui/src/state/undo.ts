// One Undo for the whole app (DESIGN.md §9, §10, §12): every user action that changes a recording,
// a template or a document registers an entry with a label and its inverse. Ctrl+Z undoes and
// Ctrl+Y or Ctrl+Shift+Z redoes anywhere, except in a text field that has its own history for the
// edit in progress; the Undo button in the Review, Builder and viewer headers does the same.
//
// The stack belongs to one screen's subject at a time (a scope: `review:<id>`, `builder:<draft>`,
// `document:<recording>/<document>`): a screen claims its scope when it opens, and claiming a
// different one clears the stack. Entries are closures over the screen that made them, so Undo
// does nothing while no screen holds the scope (the Builder keeps its stack while the Style editor
// is open and its draft waits).
import { computed, signal, type ReadonlySignal } from '@preact/signals';
import type { AppStore } from './store';

/** At most this many steps are kept; the oldest go first. */
export const UNDO_LIMIT = 50;

/** Pushes with the same merge key this close together are one step (typing in a field, dragging a slider). */
export const MERGE_WINDOW_MS = 1500;

/** How long "Undone: …" and "Saved" stay visible beside the Undo button. */
export const STATUS_MS = 4000;

export interface UndoEntry {
  /** What the action did, lower case: "merge speakers". The button reads "Undo merge speakers". */
  label: string;
  /** Puts things back. May call the host; a rejection keeps the entry (the refusal is shown). */
  undo: () => unknown;
  /** Does the action again after an undo. */
  redo: () => unknown;
}

export interface PushOptions {
  /** Consecutive pushes with the same key within MERGE_WINDOW_MS are one step: the first one's undo, the last one's redo. */
  mergeKey?: string;
}

export interface UndoStatus {
  text: string;
  tone: 'ok' | 'failed';
  /** Increases with every status, so the same words twice still announce. */
  serial: number;
}

export interface UndoManager {
  /** The scope the stack belongs to, or null. */
  readonly scope: ReadonlySignal<string | null>;
  /** A screen holds the scope: Undo and Redo work. */
  readonly active: ReadonlySignal<boolean>;
  /** The label of the step Undo would take back, or null when there is none (or no screen holds the scope). */
  readonly undoLabel: ReadonlySignal<string | null>;
  readonly redoLabel: ReadonlySignal<string | null>;
  /** An undo or redo is waiting for the host. */
  readonly busy: ReadonlySignal<boolean>;
  /** "Undone: merge speakers", "Saved", or a refusal; for the header's live region. */
  readonly status: ReadonlySignal<UndoStatus | null>;
  /** The number of steps that can be undone. */
  readonly depth: ReadonlySignal<number>;
  /** A screen opened on `scope`: a different scope than before starts an empty stack. */
  claim(scope: string): void;
  /** The screen closed. `keep` holds the stack for when the same scope is claimed again (the Builder's draft). */
  release(scope: string, options?: { keep?: boolean }): void;
  /** Registers a step that was just done. Clears what could be redone. Ignored while undoing or redoing. */
  push(entry: UndoEntry, options?: PushOptions): void;
  /** Takes back the last step; resolves true when it did. */
  undo(): Promise<boolean>;
  redo(): Promise<boolean>;
  clear(): void;
  /** A confirmation for the live region ("Saved"). */
  announce(text: string): void;
  /**
   * Ids the host assigns again when an undo re-creates something (a removed highlight comes back
   * with a new id): entries name things by the id they first had and resolve it when they run.
   */
  resolveId(id: string): string;
  aliasId(original: string, current: string): void;
  /** True while an entry's undo or redo runs (so the actions it calls do not register again). */
  readonly replaying: boolean;
}

export interface UndoManagerOptions {
  now?: () => number;
  /** How a refusal is shown (§17): a warning toast in the app. */
  report?: (title: string, body: string) => void;
}

interface Step {
  entry: UndoEntry;
  mergeKey: string | null;
  at: number;
}

function messageOf(error: unknown): string {
  return error instanceof Error ? error.message : 'Memento did not answer.';
}

function capitalised(text: string): string {
  return text.charAt(0).toLocaleUpperCase() + text.slice(1);
}

export function createUndoManager(options: UndoManagerOptions = {}): UndoManager {
  const now = options.now ?? (() => Date.now());
  const report = options.report ?? ((): void => undefined);
  const scope = signal<string | null>(null);
  const active = signal(false);
  const undoStack = signal<Step[]>([]);
  const redoStack = signal<Step[]>([]);
  const busy = signal(false);
  const status = signal<UndoStatus | null>(null);
  const aliases = new Map<string, string>();
  let serial = 0;
  let replaying = false;
  let chain: Promise<unknown> = Promise.resolve();

  const setStatus = (text: string, tone: UndoStatus['tone']): void => {
    serial += 1;
    status.value = { text, tone, serial };
  };

  const resolve = (id: string): string => {
    let current = id;
    // Follow a chain (removed, restored, removed again, restored again) without looping forever.
    for (let i = 0; i < 100; i++) {
      const next = aliases.get(current);
      if (next === undefined || next === current) {
        return current;
      }
      current = next;
    }
    return current;
  };

  const clear = (): void => {
    undoStack.value = [];
    redoStack.value = [];
    aliases.clear();
  };

  /** Runs one step off `from` and moves it onto `to` when it worked. */
  const step = (direction: 'undo' | 'redo'): Promise<boolean> => {
    const run = async (): Promise<boolean> => {
      const from = direction === 'undo' ? undoStack : redoStack;
      const to = direction === 'undo' ? redoStack : undoStack;
      const top = from.value.at(-1);
      if (!active.value || top === undefined) {
        return false;
      }
      const owner = scope.value;
      busy.value = true;
      replaying = true;
      try {
        await (direction === 'undo' ? top.entry.undo() : top.entry.redo());
      } catch (error) {
        // The host refused: the step stays where it was, so it can be tried again.
        const verb = direction === 'undo' ? 'undone' : 'redone';
        setStatus(`Not ${verb}: ${top.entry.label}`, 'failed');
        report(`${capitalised(top.entry.label)} was not ${verb}`, `${messageOf(error)} Nothing else changed; the step is still there to try again.`);
        return false;
      } finally {
        replaying = false;
        busy.value = false;
      }
      if (scope.value !== owner) {
        return false;
      }
      from.value = from.value.filter((s) => s !== top);
      to.value = [...to.value, { ...top, mergeKey: null }].slice(-UNDO_LIMIT);
      setStatus(`${direction === 'undo' ? 'Undone' : 'Redone'}: ${top.entry.label}`, 'ok');
      return true;
    };
    const next = chain.then(run, run);
    chain = next;
    return next;
  };

  return {
    scope,
    active,
    undoLabel: computed(() => (active.value ? (undoStack.value.at(-1)?.entry.label ?? null) : null)),
    redoLabel: computed(() => (active.value ? (redoStack.value.at(-1)?.entry.label ?? null) : null)),
    busy,
    status,
    depth: computed(() => undoStack.value.length),
    claim(next) {
      if (scope.value !== next) {
        clear();
        status.value = null;
        scope.value = next;
      }
      active.value = true;
    },
    release(current, opts = {}) {
      if (scope.value !== current) {
        return;
      }
      active.value = false;
      if (opts.keep !== true) {
        clear();
        scope.value = null;
      }
    },
    push(entry, opts = {}) {
      if (replaying) {
        return;
      }
      const at = now();
      const key = opts.mergeKey ?? null;
      const top = undoStack.value.at(-1);
      redoStack.value = [];
      if (key !== null && top?.mergeKey === key && at - top.at <= MERGE_WINDOW_MS) {
        const merged: Step = { entry: { label: entry.label, undo: top.entry.undo, redo: entry.redo }, mergeKey: key, at };
        undoStack.value = [...undoStack.value.slice(0, -1), merged];
        return;
      }
      undoStack.value = [...undoStack.value, { entry, mergeKey: key, at }].slice(-UNDO_LIMIT);
    },
    undo: () => step('undo'),
    redo: () => step('redo'),
    clear,
    announce(text) {
      setStatus(text, 'ok');
    },
    resolveId: resolve,
    aliasId(original, current) {
      const last = resolve(original);
      if (last !== current) {
        aliases.set(last, current);
      }
    },
    get replaying() {
      return replaying;
    },
  };
}

const managers = new WeakMap<AppStore, UndoManager>();

/** The app's undo manager; refusals show as warning toasts. */
export function undoOf(store: AppStore): UndoManager {
  let manager = managers.get(store);
  if (manager === undefined) {
    manager = createUndoManager({
      report: (title, body) => {
        store.toasts.show({ tone: 'warning', title, body, key: 'undo-refused' });
      },
    });
    managers.set(store, manager);
  }
  return manager;
}

// ---------------------------------------------------------------------------------------------
// Keyboard
// ---------------------------------------------------------------------------------------------

export type UndoKey = 'undo' | 'redo';

/** Ctrl+Z undoes; Ctrl+Y and Ctrl+Shift+Z redo (Cmd on a Mac keyboard). */
export function undoKeyOf(event: Pick<KeyboardEvent, 'key' | 'ctrlKey' | 'metaKey' | 'shiftKey' | 'altKey'>): UndoKey | null {
  if (!(event.ctrlKey || event.metaKey) || event.altKey) {
    return null;
  }
  const key = event.key.toLowerCase();
  if (key === 'z') {
    return event.shiftKey ? 'redo' : 'undo';
  }
  if (key === 'y' && !event.shiftKey) {
    return 'redo';
  }
  return null;
}

const TEXT_INPUTS: ReadonlySet<string> = new Set(['text', 'search', 'email', 'url', 'tel', 'password', 'number', '']);

/** The text field (input, textarea or rich-text area) an event target belongs to, or null. */
export function textFieldOf(target: EventTarget | null): HTMLElement | null {
  if (target instanceof HTMLTextAreaElement) {
    return target;
  }
  if (target instanceof HTMLInputElement) {
    return TEXT_INPUTS.has(target.type) ? target : null;
  }
  if (target instanceof HTMLElement && target.isContentEditable) {
    return target.closest<HTMLElement>('[contenteditable="true"]') ?? target;
  }
  return null;
}

function valueOf(field: HTMLElement): string {
  return field instanceof HTMLInputElement || field instanceof HTMLTextAreaElement ? field.value : field.innerHTML;
}

/**
 * Whether the browser's own undo has something to take back in a text field: the field changed
 * since it was focused (for redo: it was typed in since it was focused). There is no way to ask
 * the browser for its history, so this is what the app tracks.
 */
export interface FieldHistory {
  hasNativeUndo(field: HTMLElement): boolean;
  hasNativeRedo(field: HTMLElement): boolean;
  dispose(): void;
}

export function trackFieldHistory(root: Document | HTMLElement = document): FieldHistory {
  const initial = new WeakMap<HTMLElement, string>();
  const typed = new WeakSet<HTMLElement>();
  const onFocus = (event: Event): void => {
    const field = textFieldOf(event.target);
    if (field !== null) {
      initial.set(field, valueOf(field));
      typed.delete(field);
    }
  };
  const onInput = (event: Event): void => {
    const field = textFieldOf(event.target);
    if (field !== null) {
      if (!initial.has(field)) {
        initial.set(field, '');
      }
      typed.add(field);
    }
  };
  root.addEventListener('focusin', onFocus, true);
  root.addEventListener('input', onInput, true);
  return {
    hasNativeUndo: (field) => initial.has(field) && valueOf(field) !== initial.get(field),
    hasNativeRedo: (field) => typed.has(field),
    dispose: () => {
      root.removeEventListener('focusin', onFocus, true);
      root.removeEventListener('input', onInput, true);
    },
  };
}

/**
 * Ctrl+Z / Ctrl+Y / Ctrl+Shift+Z for the whole window. A text field with its own history for the
 * current edit keeps the keys; otherwise the app's undo takes them. Nothing happens while a dialog
 * is open (`blocked`).
 */
export function connectUndoKeys(manager: UndoManager, blocked: () => boolean = () => false, root: Document = document): () => void {
  const history = trackFieldHistory(root);
  const onKey = (event: KeyboardEvent): void => {
    const which = undoKeyOf(event);
    if (which === null || event.defaultPrevented || blocked()) {
      return;
    }
    const field = textFieldOf(event.target);
    if (field !== null && (which === 'undo' ? history.hasNativeUndo(field) : history.hasNativeRedo(field))) {
      return;
    }
    if (!manager.active.value) {
      return;
    }
    event.preventDefault();
    void (which === 'undo' ? manager.undo() : manager.redo());
  };
  root.addEventListener('keydown', onKey);
  return () => {
    root.removeEventListener('keydown', onKey);
    history.dispose();
  };
}
