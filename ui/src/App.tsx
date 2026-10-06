import type { JSX } from 'preact';
import { useMemo } from 'preact/hooks';
import type { BridgeClient } from './bridge/client';
import { DialogHost } from './components/DialogHost';
import { ToastStack } from './components/Toasts';
import { LibraryScreen } from './screens/library/LibraryScreen';
import { RecordScreen } from './screens/record/RecordScreen';
import { ReviewScreen } from './screens/review/ReviewScreen';
import { SettingsScreen } from './screens/settings/SettingsScreen';
import { AppContext, createMemoryRouter, type AppServices } from './state/context';
import { screenKey, type Route, type Router } from './state/router';
import type { AppStore } from './state/store';

interface AppProps {
  bridge: BridgeClient;
  store: AppStore;
  /** location.hash router in the app; tests leave it out and get an in-memory one. */
  router?: Router;
  now?: () => Date;
}

/**
 * One screen per route (hub and spoke, DESIGN.md §1). Each screen renders its own header and footer.
 */
function Screen({ route }: { route: Route }): JSX.Element {
  switch (route.name) {
    case 'library':
      return <LibraryScreen />;
    case 'record':
      return <RecordScreen />;
    case 'review':
      return <ReviewScreen recordingId={route.recordingId} />;
    case 'settings':
      return <SettingsScreen section={route.section} />;
  }
}

/** The shell: the current screen, then the global surfaces (toasts, dialogs) above it. */
export function App({ bridge, store, router, now }: AppProps): JSX.Element {
  const services = useMemo<AppServices>(
    () => ({ bridge, store, router: router ?? createMemoryRouter(store), now: now ?? (() => new Date()) }),
    [bridge, store, router, now],
  );
  const route = store.route.value;
  const modalOpen = store.overlays.value > 0;
  return (
    <AppContext.Provider value={services}>
      {/* Keyed by screen so a spoke change remounts and plays the cross-fade (styles/shell.css). */}
      <div class="shell screen" key={screenKey(route)} inert={modalOpen}>
        <Screen route={route} />
      </div>
      <ToastStack queue={store.toasts} />
      <DialogHost />
    </AppContext.Provider>
  );
}
