import { signal, type Signal } from '@preact/signals';
import type { BridgeClient } from '../bridge/client';
import type { AppVersionResult, FooterStatusPayload, LibraryListResult, SettingsSnapshot } from '../bridge/types';

/** Everything the Library shell shows. `null` means "not reported yet", never a made-up value. */
export interface AppStore {
  isDark: Signal<boolean>;
  version: Signal<AppVersionResult | null>;
  settings: Signal<SettingsSnapshot | null>;
  library: Signal<LibraryListResult | null>;
  footer: Signal<FooterStatusPayload | null>;
  loadError: Signal<string | null>;
}

export function createStore(initialDark: boolean): AppStore {
  return {
    isDark: signal(initialDark),
    version: signal<AppVersionResult | null>(null),
    settings: signal<SettingsSnapshot | null>(null),
    library: signal<LibraryListResult | null>(null),
    footer: signal<FooterStatusPayload | null>(null),
    loadError: signal<string | null>(null),
  };
}

/** Routes host events into the store. Returns the unsubscribe function. */
export function connectEvents(bridge: BridgeClient, store: AppStore): () => void {
  const offTheme = bridge.on('theme.changed', (payload) => {
    store.isDark.value = payload.isDark;
  });
  const offFooter = bridge.on('status.footer', (payload) => {
    store.footer.value = payload;
  });
  return () => {
    offTheme();
    offFooter();
  };
}

/** Reads what the shell needs on start. Failures land in `loadError` with the host's own message. */
export async function loadInitialData(bridge: BridgeClient, store: AppStore): Promise<void> {
  store.loadError.value = null;
  try {
    const [version, settings, library] = await Promise.all([
      bridge.call('app.version'),
      bridge.call('settings.get'),
      bridge.call('library.list'),
    ]);
    store.version.value = version;
    store.isDark.value = version.isDarkTheme;
    store.settings.value = settings;
    store.library.value = library;
  } catch (error) {
    store.loadError.value = error instanceof Error ? error.message : 'The host did not answer.';
  }
}
