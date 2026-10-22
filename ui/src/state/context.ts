import { createContext } from 'preact';
import { useContext } from 'preact/hooks';
import type { BridgeClient } from '../bridge/client';
import { navigationOf, recordMove } from './navigation';
import { formatRoute, type NavigateOptions, type Route, type Router } from './router';
import type { AppStore } from './store';

/** What every screen and component needs: the host, the state and navigation. */
export interface AppServices {
  bridge: BridgeClient;
  store: AppStore;
  router: Router;
  /** The clock used for date wording; tests pin it. */
  now: () => Date;
}

export const AppContext = createContext<AppServices | null>(null);

export function useServices(): AppServices {
  const services = useContext(AppContext);
  if (services === null) {
    throw new Error('useServices must be used inside <App>.');
  }
  return services;
}

/** A router without location.hash, for tests and embedding: only the store changes. */
export function createMemoryRouter(store: AppStore): Router {
  return {
    navigate(route: Route, options?: NavigateOptions) {
      const current = store.route.value;
      if (current.name === 'library' && route.name !== 'library') {
        store.libraryView.value = {
          ...store.libraryView.value,
          scrollY: typeof window === 'undefined' ? 0 : window.scrollY,
        };
      }
      if (formatRoute(current) !== formatRoute(route)) {
        recordMove(navigationOf(store), current, route, options?.back === true ? 'back' : 'forward');
      }
      store.route.value = route;
    },
    dispose: () => undefined,
  };
}
