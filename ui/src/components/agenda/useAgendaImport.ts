// What the Details sheet's agenda section does with the host (DESIGN.md §14, BRIDGE.md M3): read a
// dropped, picked or pasted agenda into a preview that is not saved yet, then apply it with
// agenda.apply (or keep it with the recording until the recording exists), or discard it.
import { effect } from '@preact/signals';
import { useEffect, useRef, useState } from 'preact/hooks';
import { BridgeCallError, type BridgeClient } from '../../bridge/client';
import type { AgendaImportResult, AgendaParsePreview, AgendaSourceKind } from '../../bridge/types';
import { useServices } from '../../state/context';
import type { DetailsSaver } from '../../state/detailsSaver';
import { applyItems, checkLimits, previewItems, toAgendaItems, type EditableItem } from './agendaItems';

export type AgendaMode = 'list' | 'drop' | 'paste' | 'preview';

export interface PendingPreview {
  preview: AgendaParsePreview;
  items: EditableItem[];
}

export interface AgendaError {
  message: string;
  /** An ms-settings: page that fixes it (agenda.ocrUnavailable), when the host named one. */
  settingsUrl: string | null;
}

export interface AgendaImportController {
  mode: AgendaMode;
  setMode: (mode: AgendaMode) => void;
  pending: PendingPreview | null;
  setPendingItems: (items: EditableItem[]) => void;
  /** What is being read ("agenda.docx", "Pasted text"), or null. */
  reading: string | null;
  error: AgendaError | null;
  clearError: () => void;
  /** Problems agenda.apply would refuse, shown inline; Apply waits until they are fixed. */
  limitsOk: boolean;
  chooseFile: () => Promise<void>;
  /** Dropped files: their names go in the request and the files with the message, so the host reads their paths. */
  drop: (files: readonly File[]) => Promise<void>;
  parseText: (text: string) => Promise<boolean>;
  apply: () => Promise<boolean>;
  discard: () => void;
  /** Replace: back to the drop zone (a pending preview is discarded). */
  replace: () => void;
  /** Done or close: applies a pending preview. False keeps the sheet open (limits or a failure). */
  finish: () => Promise<boolean>;
}

function errorOf(error: unknown): AgendaError {
  if (error instanceof BridgeCallError) {
    return {
      message: error.message,
      settingsUrl: error.code === 'agenda.ocrUnavailable' && error.detail?.startsWith('ms-settings:') === true ? error.detail : null,
    };
  }
  return { message: error instanceof Error ? error.message : 'Memento did not answer. Nothing was changed.', settingsUrl: null };
}

interface DeferredOriginal {
  token: string;
  source: string;
  sourceKind: AgendaSourceKind;
}

/**
 * Before the recording exists the agenda goes with the other details when it starts; the original
 * file is applied (copied into the recording's attachments) as soon as there is a recording id.
 */
function applyWhenRecordingExists(bridge: BridgeClient, saver: DetailsSaver, original: DeferredOriginal): void {
  let done = false;
  const stop = effect(() => {
    const recordingId = saver.recordingId.value;
    if (recordingId === null || done) {
      return;
    }
    done = true;
    queueMicrotask(() => {
      stop();
    });
    void (async () => {
      // The details edited so far (the agenda among them) reach the project first.
      await saver.flush();
      const current = saver.details.value.agenda;
      try {
        const project = await bridge.call('agenda.apply', {
          recordingId,
          items: current.items.map((i) => ({ text: i.text, uncertain: i.uncertain, uncertainReason: i.uncertainReason })),
          source: original.source,
          sourceKind: original.sourceKind,
          attachmentToken: original.token,
        });
        // Items ticked off in the first seconds stay ticked.
        const items = project.details.agenda.items.map((item, index) => (current.items[index]?.covered === true ? { ...item, covered: true } : item));
        saver.receive({ agenda: { ...project.details.agenda, items } });
        for (const item of items.filter((i) => i.covered)) {
          void bridge.call('agenda.setCovered', { recordingId, itemId: item.id, covered: true }).catch(() => undefined);
        }
      } catch (error) {
        console.warn('[agenda] the original file could not be attached; the items are kept', error);
        void bridge.call('agenda.discard', { attachmentToken: original.token }).catch(() => undefined);
      }
    })();
  });
}

export function useAgendaImport(saver: DetailsSaver, initialMode: AgendaMode | null = null): AgendaImportController {
  const { bridge } = useServices();
  const hasItems = saver.details.value.agenda.items.length > 0;
  const [mode, setModeState] = useState<AgendaMode>(initialMode ?? (hasItems ? 'list' : 'drop'));
  const [pending, setPending] = useState<PendingPreview | null>(null);
  const [reading, setReading] = useState<string | null>(null);
  const [error, setError] = useState<AgendaError | null>(null);
  // The latest preview for the unmount cleanup and for finish() called right after a change.
  const pendingRef = useRef<PendingPreview | null>(null);
  pendingRef.current = pending;

  const restMode = (): AgendaMode => (saver.details.value.agenda.items.length > 0 ? 'list' : 'drop');

  const discardToken = (preview: AgendaParsePreview | undefined): void => {
    const token = preview?.attachmentToken ?? null;
    if (token !== null) {
      bridge.call('agenda.discard', { attachmentToken: token }).catch((e: unknown) => {
        console.warn('[agenda] discard failed; the host drops the file when Memento closes', e);
      });
    }
  };

  // Leaving without Done or Discard (the screen closed): the host may drop the original.
  useEffect(
    () => () => {
      discardToken(pendingRef.current?.preview);
    },
    [],
  );

  const take = (result: AgendaImportResult): void => {
    if (result.cancelled || result.preview === null) {
      return;
    }
    discardToken(pendingRef.current?.preview);
    const next = { preview: result.preview, items: previewItems(result.preview) };
    pendingRef.current = next;
    setPending(next);
    setModeState('preview');
  };

  const read = async (what: string, call: () => Promise<AgendaImportResult>): Promise<boolean> => {
    setError(null);
    setReading(what);
    try {
      take(await call());
      return true;
    } catch (e) {
      setError(errorOf(e));
      return false;
    } finally {
      setReading(null);
    }
  };

  const recordingId = (): string | null => saver.recordingId.value;

  const chooseFile = async (): Promise<void> => {
    await read('the file you choose', () => bridge.call('agenda.importFile', { recordingId: recordingId() }));
  };

  const drop = async (files: readonly File[]): Promise<void> => {
    const names = files.map((file) => file.name);
    const first = names[0] ?? 'the dropped file';
    setError(null);
    setReading(first);
    try {
      take(await bridge.callWithFiles('agenda.importDropped', { recordingId: recordingId(), paths: names }, files));
      setReading(null);
    } catch (e) {
      setReading(null);
      if (e instanceof BridgeCallError && e.code === 'agenda.dropUnavailable') {
        // The host could not resolve the drop: the picker asks for the same file instead.
        await chooseFile();
        return;
      }
      setError(errorOf(e));
    }
  };

  const parseText = (text: string): Promise<boolean> =>
    read('Pasted text', async () => {
      const { preview } = await bridge.call('agenda.parseText', { recordingId: recordingId(), text });
      return { preview, cancelled: false };
    });

  const apply = async (): Promise<boolean> => {
    const current = pendingRef.current;
    if (current === null) {
      return true;
    }
    if (!checkLimits(current.items).ok) {
      return false;
    }
    const { preview } = current;
    const items = applyItems(current.items);
    const id = recordingId();
    setError(null);
    if (id === null) {
      // No recording yet: the items go with the recording when it starts.
      saver.update({
        agenda: {
          source: preview.source,
          parsedLocally: true,
          items: toAgendaItems(current.items.filter((i) => i.text.trim() !== '').map((i) => ({ ...i, text: i.text.trim() }))),
        },
      });
      if (preview.attachmentToken !== null) {
        applyWhenRecordingExists(bridge, saver, { token: preview.attachmentToken, source: preview.source, sourceKind: preview.sourceKind });
      }
    } else {
      try {
        // A pending edit of the old agenda must not land after the new one.
        await saver.flush();
        const project = await bridge.call('agenda.apply', {
          recordingId: id,
          items,
          source: preview.source,
          sourceKind: preview.sourceKind,
          attachmentToken: preview.attachmentToken,
        });
        saver.receive({ agenda: project.details.agenda });
      } catch (e) {
        setError(errorOf(e));
        return false;
      }
    }
    pendingRef.current = null;
    setPending(null);
    setModeState(items.length > 0 ? 'list' : 'drop');
    return true;
  };

  const discard = (): void => {
    discardToken(pendingRef.current?.preview);
    pendingRef.current = null;
    setPending(null);
    setError(null);
    setModeState(restMode());
  };

  return {
    mode,
    setMode: (next) => {
      setError(null);
      setModeState(next);
    },
    pending,
    setPendingItems: (items) => {
      const current = pendingRef.current;
      if (current !== null) {
        const next = { ...current, items };
        pendingRef.current = next;
        setPending(next);
      }
    },
    reading,
    error,
    clearError: () => {
      setError(null);
    },
    limitsOk: pending === null || checkLimits(pending.items).ok,
    chooseFile,
    drop,
    parseText,
    apply,
    discard,
    replace: () => {
      discardToken(pendingRef.current?.preview);
      pendingRef.current = null;
      setPending(null);
      setError(null);
      setModeState('drop');
    },
    finish: apply,
  };
}
