import type { JSX } from 'preact';
import type { BridgeClient } from './bridge/client';
import { EmptyLibrary } from './components/EmptyLibrary';
import { LibraryHeader, SEARCH_PLACEHOLDER, SEARCH_PLACEHOLDER_EMPTY } from './components/LibraryHeader';
import { StatusFooter } from './components/StatusFooter';
import { loadInitialData, type AppStore } from './state/store';

interface AppProps {
  bridge: BridgeClient;
  store: AppStore;
}

// Recording, import and Settings arrive in M1 and M3; the controls are real but only log for now.
function notYetAvailable(action: string): () => void {
  return () => {
    console.info(`[library] ${action} is not available in this version yet.`);
  };
}

/** The Library hub: header, content, status footer (DESIGN.md §3). */
export function App({ bridge, store }: AppProps): JSX.Element {
  const library = store.library.value;
  const loadError = store.loadError.value;
  const isEmpty = library !== null && library.recordings.length === 0;

  return (
    <div class="shell">
      <LibraryHeader
        searchDisabled={library === null || isEmpty}
        searchPlaceholder={isEmpty ? SEARCH_PLACEHOLDER_EMPTY : SEARCH_PLACEHOLDER}
        onNewRecording={notYetAvailable('New recording')}
        onOpenSettings={notYetAvailable('Settings')}
      />
      <main class="library-main library-main--centred">
        {loadError !== null ? (
          <div class="load-error" role="alert">
            <p class="load-error-text">The library could not be shown. {loadError}</p>
            <button
              class="btn ghost load-error-retry"
              type="button"
              onClick={() => {
                void loadInitialData(bridge, store);
              }}
            >
              Try again
            </button>
          </div>
        ) : isEmpty ? (
          <EmptyLibrary
            onStartRecording={notYetAvailable('Start your first recording')}
            onImport={notYetAvailable('Import audio or video')}
          />
        ) : null}
      </main>
      <StatusFooter status={store.footer.value} />
    </div>
  );
}
