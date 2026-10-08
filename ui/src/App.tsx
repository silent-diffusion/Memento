import type { JSX } from 'preact';
import { useEffect, useMemo } from 'preact/hooks';
import type { BridgeClient } from './bridge/client';
import { DialogHost } from './components/DialogHost';
import { ToastStack } from './components/Toasts';
import { LibraryScreen } from './screens/library/LibraryScreen';
import { RecordScreen } from './screens/record/RecordScreen';
import { ReviewScreen } from './screens/review/ReviewScreen';
import { SettingsScreen } from './screens/settings/SettingsScreen';
import { BuilderScreen } from './screens/builder/BuilderScreen';
import { connectGeneration } from './screens/builder/generation';
import { DocumentScreen } from './screens/docview/DocumentScreen';
import { StyleEditorScreen } from './screens/styleeditor/StyleEditorScreen';
import { AppContext, createMemoryRouter, type AppServices } from './state/context';
import { connectJobEvents } from './state/jobs';
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
      return <ReviewScreen recordingId={route.recordingId} {...(route.atMs === undefined ? {} : { startAtMs: route.atMs })} />;
    case 'settings':
      return <SettingsScreen section={route.section} />;
    // M4
    case 'builder':
      return <BuilderScreen recordingId={route.recordingId} templateId={route.templateId} documentId={route.documentId} />;
    case 'document':
      return <DocumentScreen recordingId={route.recordingId} documentId={route.documentId} />;
    case 'style':
      return <StyleEditorScreen styleId={route.styleId} />;
  }
}

/** The shell: the current screen, then the global surfaces (toasts, dialogs) above it. */
export function App({ bridge, store, router, now }: AppProps): JSX.Element {
  const services = useMemo<AppServices>(
    () => ({ bridge, store, router: router ?? createMemoryRouter(store), now: now ?? (() => new Date()) }),
    [bridge, store, router, now],
  );
  // M3: export, library move and reclaim progress (state/jobs.ts).
  useEffect(() => connectJobEvents(bridge, store), [bridge, store]);
  // M4: generation.progress for the Builder and for jobs that end while it is closed.
  useEffect(() => connectGeneration(services), [services]);
  const route = store.route.value;
  const modalOpen = store.overlays.value > 0;
  return (
    <AppContext.Provider value={services}>
      {/* Keyed by screen so a spoke change remounts and plays the cross-fade (styles/shell.css). */}
      {/* Review fills the window and scrolls inside its panes (DESIGN.md §9); the rest scroll the page. */}
      <div class={route.name === 'review' ? 'shell screen shell--panes' : 'shell screen'} key={screenKey(route)} inert={modalOpen}>
        <Screen route={route} />
      </div>
      <ToastStack queue={store.toasts} />
      <DialogHost />
    </AppContext.Provider>
  );
}
