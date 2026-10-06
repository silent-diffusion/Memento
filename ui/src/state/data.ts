// Reads from the host into the store. Every failure is kept as the host's own message.
import type { BridgeClient } from '../bridge/client';
import type { LibraryListResult } from '../bridge/types';
import { libraryViewReducer, listParams, type LibraryViewAction } from './libraryView';
import type { AppStore } from './store';

function messageOf(error: unknown): string {
  return error instanceof Error ? error.message : 'The host did not answer.';
}

// Answers to an older request that arrive after a newer one are dropped, so fast typing never
// shows stale rows. The whole-library read and the current view are sequenced separately.
let wholeSequence = 0;
let viewSequence = 0;

/** Refetches only the current view (search, filter, sort changed). */
export async function refreshView(bridge: BridgeClient, store: AppStore): Promise<void> {
  const sequence = ++viewSequence;
  const view: LibraryListResult = await bridge.call('library.list', listParams(store.libraryView.value));
  if (sequence === viewSequence) {
    store.libraryResult.value = view;
  }
}

/** Refetches the whole library, the current view of it and the processing card. */
export async function refreshLibrary(bridge: BridgeClient, store: AppStore): Promise<void> {
  const sequence = ++wholeSequence;
  const [whole, processing] = await Promise.all([
    bridge.call('library.list'),
    bridge.call('library.processing'),
    refreshView(bridge, store),
  ]);
  if (sequence === wholeSequence) {
    store.library.value = whole;
    store.processing.value = processing;
  }
}

export const SEARCH_DEBOUNCE_MS = 150;

let searchTimer: ReturnType<typeof setTimeout> | undefined;

/**
 * Applies a Library view change. Search, filter and sort refetch from the host (search after a short
 * pause in typing); layout, scroll and selection are local.
 */
export function updateLibraryView(bridge: BridgeClient, store: AppStore, action: LibraryViewAction): void {
  const before = store.libraryView.value;
  const after = libraryViewReducer(before, action);
  if (after === before) {
    return;
  }
  store.libraryView.value = after;
  const refetch = (): void => {
    refreshView(bridge, store).catch((error: unknown) => {
      store.loadError.value = messageOf(error);
    });
  };
  if (action.type === 'search') {
    clearTimeout(searchTimer);
    searchTimer = setTimeout(refetch, SEARCH_DEBOUNCE_MS);
  } else if (action.type === 'filter' || action.type === 'sort' || action.type === 'clear') {
    clearTimeout(searchTimer);
    refetch();
  }
}

/** Reads what the shell needs on start. Failures land in `loadError` with the host's own message. */
export async function loadInitialData(bridge: BridgeClient, store: AppStore): Promise<void> {
  store.loadError.value = null;
  try {
    const [version, settings, status, current, recovery] = await Promise.all([
      bridge.call('app.version'),
      bridge.call('settings.get'),
      bridge.call('status.get'),
      bridge.call('recording.current'),
      bridge.call('recovery.list'),
      refreshLibrary(bridge, store),
    ]);
    store.version.value = version;
    store.isDark.value = version.isDarkTheme;
    store.settings.value = settings;
    // A footer event may already have arrived; status.get is the same payload, so either is current.
    store.footer.value ??= status;
    store.recording.value = current.session;
    store.recoveryQueue.value = recovery.items;
  } catch (error) {
    store.loadError.value = messageOf(error);
  }
}
