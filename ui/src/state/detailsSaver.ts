// Recording details being edited (Details sheet, DESIGN.md §14; the Record header title and type).
// Edits apply locally at once and reach the host through project.updateDetails after a short pause
// in typing, merged into one call. Before a recording exists the edits stay local ("Saved with the
// recording when it starts") and are sent the moment recording.start returns the recording id.
import { signal, type Signal } from '@preact/signals';
import type { BridgeClient } from '../bridge/client';
import type { RecordingDetails, RecordingType } from '../bridge/types';

export const DETAILS_SAVE_DEBOUNCE_MS = 600;

/** `local`: no recording yet. `pending`: waiting for the pause in typing. */
export type SaveStatus = 'local' | 'idle' | 'pending' | 'saving' | 'saved' | 'error';

export interface DetailsSaver {
  details: Signal<RecordingDetails>;
  status: Signal<SaveStatus>;
  /** The host's message when the last save failed. */
  error: Signal<string | null>;
  recordingId: Signal<string | null>;
  update(patch: Partial<RecordingDetails>): void;
  /** The recording now exists: send everything edited so far. */
  attach(recordingId: string): Promise<void>;
  /** An existing recording (rejoin, Review): take the host's details as they are. */
  adopt(recordingId: string, details: RecordingDetails): void;
  /** Sends a pending change now (closing the sheet, leaving the screen). */
  flush(): Promise<void>;
  dispose(): void;
}

export function emptyDetails(title: string, type: RecordingType): RecordingDetails {
  return {
    title,
    type,
    participants: [],
    purpose: '',
    platform: '',
    organization: '',
    location: '',
    notes: '',
    tags: [],
    agenda: { source: null, parsedLocally: true, items: [] },
  };
}

function messageOf(error: unknown): string {
  return error instanceof Error ? error.message : 'Memento did not answer.';
}

export function createDetailsSaver(
  bridge: BridgeClient,
  initial: RecordingDetails,
  debounceMs: number = DETAILS_SAVE_DEBOUNCE_MS,
): DetailsSaver {
  const details = signal(initial);
  const status = signal<SaveStatus>('local');
  const error = signal<string | null>(null);
  const recordingId = signal<string | null>(null);
  let pending: Partial<RecordingDetails> = {};
  let timer: ReturnType<typeof setTimeout> | undefined;
  let inFlight: Promise<void> | null = null;
  let disposed = false;

  const send = async (): Promise<void> => {
    clearTimeout(timer);
    timer = undefined;
    const id = recordingId.value;
    if (id === null || Object.keys(pending).length === 0) {
      return;
    }
    // One save at a time, in order; edits made meanwhile wait for the next one.
    if (inFlight !== null) {
      await inFlight;
      return send();
    }
    const patch = pending;
    pending = {};
    status.value = 'saving';
    error.value = null;
    inFlight = bridge
      .call('project.updateDetails', { recordingId: id, details: patch })
      .then(() => {
        if (!disposed) {
          status.value = Object.keys(pending).length > 0 ? 'pending' : 'saved';
        }
      })
      .catch((e: unknown) => {
        // Keep the edit so the next change (or Retry) sends it again.
        pending = { ...patch, ...pending };
        if (!disposed) {
          status.value = 'error';
          error.value = messageOf(e);
        }
      })
      .finally(() => {
        inFlight = null;
      });
    await inFlight;
  };

  const schedule = (): void => {
    clearTimeout(timer);
    status.value = 'pending';
    timer = setTimeout(() => {
      void send();
    }, debounceMs);
  };

  return {
    details,
    status,
    error,
    recordingId,
    update(patch) {
      details.value = { ...details.value, ...patch };
      pending = { ...pending, ...patch };
      if (recordingId.value === null) {
        status.value = 'local';
        return;
      }
      schedule();
    },
    async attach(id) {
      recordingId.value = id;
      // Title and type went with recording.start; everything else edited so far goes now.
      const rest = { ...pending };
      delete rest.title;
      delete rest.type;
      pending = rest;
      if (Object.keys(pending).length === 0) {
        status.value = 'idle';
        return;
      }
      await send();
    },
    adopt(id, hostDetails) {
      clearTimeout(timer);
      pending = {};
      recordingId.value = id;
      details.value = hostDetails;
      status.value = 'idle';
      error.value = null;
    },
    flush: send,
    dispose() {
      disposed = true;
      clearTimeout(timer);
      // A change still waiting is sent rather than lost.
      if (recordingId.value !== null && Object.keys(pending).length > 0) {
        void send();
      }
    },
  };
}

/** The sheet's save line. */
export function saveStatusText(status: SaveStatus, error: string | null): string {
  switch (status) {
    case 'local':
      return 'Saved with the recording when it starts.';
    case 'idle':
      return 'Everything here can be changed later.';
    case 'pending':
    case 'saving':
      return 'Saving…';
    case 'saved':
      return 'Saved';
    case 'error':
      return error === null ? 'Not saved yet. Memento will try again with your next change.' : `${error} Your edit is kept and will be sent again.`;
  }
}
