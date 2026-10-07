// "Edits save as you type" (DESIGN.md §12): the viewer's paper goes to documents.saveEdit once
// typing pauses, one save at a time; a change during a save is saved right after it. Leaving the
// viewer (or opening Review from a chip) saves what is pending first.
import type { BridgeClient } from '../../bridge/client';

export const SAVE_DEBOUNCE_MS = 800;

export type SaveState =
  | { kind: 'saved'; version: number | null }
  | { kind: 'pending' }
  | { kind: 'saving' }
  | { kind: 'failed'; message: string };

export interface EditSaver {
  /** Something in the paper changed. */
  changed(): void;
  /** Saves now if anything is waiting; resolves when the document is saved (or the save failed). */
  flush(): Promise<void>;
  dispose(): void;
}

export interface EditSaverOptions {
  bridge: BridgeClient;
  recordingId: string;
  documentId: string;
  /** The paper's markup as it is now, or null while there is no paper. */
  read: () => string | null;
  onState: (state: SaveState) => void;
  /** After a save: the host's version number. */
  onSaved?: (version: number) => void;
  delayMs?: number;
}

function messageOf(error: unknown): string {
  return error instanceof Error ? error.message : 'Memento did not answer.';
}

export function createEditSaver(options: EditSaverOptions): EditSaver {
  const delay = options.delayMs ?? SAVE_DEBOUNCE_MS;
  let timer: ReturnType<typeof setTimeout> | null = null;
  // Changed by changed() and dispose() while a save awaits, so kept in an object.
  const state = { dirty: false, disposed: false };
  const isDirty = (): boolean => state.dirty;
  let inFlight: Promise<void> | null = null;

  const save = async (): Promise<void> => {
    if (timer !== null) {
      clearTimeout(timer);
      timer = null;
    }
    if (inFlight !== null) {
      await inFlight;
    }
    if (!state.dirty) {
      return;
    }
    const html = options.read();
    if (html === null) {
      return;
    }
    state.dirty = false;
    options.onState({ kind: 'saving' });
    inFlight = options.bridge
      .call('documents.saveEdit', { recordingId: options.recordingId, documentId: options.documentId, html })
      .then(({ version }) => {
        if (!state.dirty) {
          options.onState({ kind: 'saved', version });
        }
        options.onSaved?.(version);
      })
      .catch((error: unknown) => {
        options.onState({ kind: 'failed', message: messageOf(error) });
      })
      .finally(() => {
        inFlight = null;
      });
    await inFlight;
    if (isDirty() && !state.disposed) {
      // Typed during the save: save again once typing pauses.
      schedule();
    }
  };

  const schedule = (): void => {
    if (timer !== null) {
      clearTimeout(timer);
    }
    timer = setTimeout(() => {
      void save();
    }, delay);
  };

  return {
    changed() {
      state.dirty = true;
      options.onState({ kind: 'pending' });
      schedule();
    },
    flush: save,
    dispose() {
      state.disposed = true;
      if (timer !== null) {
        clearTimeout(timer);
        timer = null;
      }
    },
  };
}
