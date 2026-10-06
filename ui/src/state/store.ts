import { signal, type Signal } from '@preact/signals';
import type { BridgeClient } from '../bridge/client';
import type {
  AppVersionResult,
  AudioSource,
  FooterStatusPayload,
  LibraryListResult,
  LibraryProcessingResult,
  RecordingLevelsPayload,
  RecordingSourceLostPayload,
  RecordingStatePayload,
  RecoveredRecording,
  SettingsSnapshot,
  StageStatus,
  StorageLowSpacePayload,
} from '../bridge/types';
import { sourceLostCopy, stoppedByHostCopy } from '../format/messages';
import type { DialogRequest } from './dialogs';
import { INITIAL_LIBRARY_VIEW, type LibraryViewState } from './libraryView';
import { LIBRARY_ROUTE, type Route } from './router';
import { createToastQueue, type ToastQueue } from './toasts';

/** Everything the UI shows. `null` means "not reported yet", never a made-up value. */
export interface AppStore {
  isDark: Signal<boolean>;
  version: Signal<AppVersionResult | null>;
  settings: Signal<SettingsSnapshot | null>;
  footer: Signal<FooterStatusPayload | null>;
  loadError: Signal<string | null>;

  route: Signal<Route>;

  /** The whole library, unfiltered: decides the empty state, the custom type chips and Storage usage. */
  library: Signal<LibraryListResult | null>;
  /** What the Library lists for the current search, filter and sort. */
  libraryResult: Signal<LibraryListResult | null>;
  libraryView: Signal<LibraryViewState>;
  processing: Signal<LibraryProcessingResult | null>;

  sources: Signal<AudioSource[] | null>;
  /** The active (or just finished) recording session. */
  recording: Signal<RecordingStatePayload | null>;
  levels: Signal<RecordingLevelsPayload | null>;
  /** The latest source loss of the active session; cleared when the session ends. */
  lostSource: Signal<RecordingSourceLostPayload | null>;
  lowSpace: Signal<StorageLowSpacePayload | null>;

  toasts: ToastQueue;
  dialog: Signal<DialogRequest | null>;
  /** Open modal surfaces (dialogs, side sheets). While above zero the shell is inert. */
  overlays: Signal<number>;
  /** Recovered projects still to be shown, one dialog at a time. */
  recoveryQueue: Signal<RecoveredRecording[]>;
}

export function createStore(initialDark: boolean, toasts: ToastQueue = createToastQueue()): AppStore {
  return {
    isDark: signal(initialDark),
    version: signal<AppVersionResult | null>(null),
    settings: signal<SettingsSnapshot | null>(null),
    footer: signal<FooterStatusPayload | null>(null),
    loadError: signal<string | null>(null),
    route: signal<Route>(LIBRARY_ROUTE),
    library: signal<LibraryListResult | null>(null),
    libraryResult: signal<LibraryListResult | null>(null),
    libraryView: signal<LibraryViewState>(INITIAL_LIBRARY_VIEW),
    processing: signal<LibraryProcessingResult | null>(null),
    sources: signal<AudioSource[] | null>(null),
    recording: signal<RecordingStatePayload | null>(null),
    levels: signal<RecordingLevelsPayload | null>(null),
    lostSource: signal<RecordingSourceLostPayload | null>(null),
    lowSpace: signal<StorageLowSpacePayload | null>(null),
    toasts,
    dialog: signal<DialogRequest | null>(null),
    overlays: signal(0),
    recoveryQueue: signal<RecoveredRecording[]>([]),
  };
}

function withStages(result: LibraryListResult | null, recordingId: string, stages: StageStatus[]): LibraryListResult | null {
  if (!result?.recordings.some((r) => r.id === recordingId)) {
    return result;
  }
  return {
    ...result,
    recordings: result.recordings.map((r) =>
      r.id === recordingId
        ? { ...r, stages, isProcessing: stages.some((s) => s.state === 'active' || s.state === 'queued') }
        : r,
    ),
  };
}

/** Puts live stage progress into the processing card and the matching row, without a refetch. */
export function applyProgress(store: AppStore, recordingId: string, stages: StageStatus[]): void {
  store.library.value = withStages(store.library.value, recordingId, stages);
  store.libraryResult.value = withStages(store.libraryResult.value, recordingId, stages);
  const processing = store.processing.value;
  if (processing?.current?.recordingId === recordingId) {
    store.processing.value = { ...processing, current: { ...processing.current, stages } };
  }
}

export interface EventHooks {
  /** library.changed: refetch what the Library shows. */
  onLibraryChanged?: (recordingIds: string[]) => void;
}

/** Routes host events into the store. Returns the unsubscribe function. */
export function connectEvents(bridge: BridgeClient, store: AppStore, hooks: EventHooks = {}): () => void {
  const offs = [
    bridge.on('theme.changed', (payload) => {
      store.isDark.value = payload.isDark;
    }),
    bridge.on('status.footer', (payload) => {
      store.footer.value = payload;
      // The banner stays until the condition is resolved, which the footer reports.
      if (!payload.storage.lowSpace) {
        store.lowSpace.value = null;
      }
    }),
    bridge.on('recording.state', (payload) => {
      store.recording.value = payload;
      if (payload.state === 'ready' || payload.state === 'stopped') {
        store.lostSource.value = null;
        store.levels.value = null;
      }
    }),
    bridge.on('recording.levels', (payload) => {
      store.levels.value = payload;
    }),
    bridge.on('recording.sourceLost', (payload) => {
      store.lostSource.value = payload;
      const copy = sourceLostCopy(payload);
      store.toasts.show({
        key: `lost:${payload.sourceId}`,
        tone: 'warning',
        title: copy.title,
        body: copy.body,
        actions: [
          {
            label: `Reconnect ${payload.name}`,
            run: () => {
              bridge
                .call('recording.setSource', { sessionId: payload.sessionId, sourceId: payload.sourceId, enabled: true })
                .then(() => {
                  if (store.lostSource.value?.sourceId === payload.sourceId) {
                    store.lostSource.value = null;
                  }
                })
                .catch((error: unknown) => {
                  store.toasts.show({
                    tone: 'danger',
                    title: `${payload.name} could not be reconnected`,
                    body: `${error instanceof Error ? error.message : 'Memento did not answer.'} The other sources are still recording.`,
                  });
                });
            },
          },
          { label: 'Dismiss', quiet: true, run: () => undefined },
        ],
      });
    }),
    bridge.on('recording.stoppedByHost', (payload) => {
      const copy = stoppedByHostCopy(payload);
      store.toasts.show({ tone: 'danger', title: copy.title, body: copy.body });
    }),
    bridge.on('library.changed', (payload) => {
      hooks.onLibraryChanged?.(payload.recordingIds);
    }),
    bridge.on('processing.progress', (payload) => {
      applyProgress(store, payload.recordingId, payload.stages);
    }),
    bridge.on('storage.lowSpace', (payload) => {
      store.lowSpace.value = payload;
    }),
  ];
  return () => {
    for (const off of offs) {
      off();
    }
  };
}
