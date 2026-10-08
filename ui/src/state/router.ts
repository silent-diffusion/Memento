// In-page hub-and-spoke routing (DESIGN.md §1, ARCHITECTURE.md §3). The Library is home; Record,
// Review and Settings are spokes with a back control. The route is mirrored in location.hash so a
// reload lands on the same screen, but the store is the source of truth.
import type { Signal } from '@preact/signals';
import type { LibraryViewState } from './libraryView';

export const SETTINGS_SECTIONS = [
  'general',
  'recording',
  'transcription',
  'speakers',
  'ai-privacy',
  'documents',
  'export',
  'storage',
] as const;

export type SettingsSection = (typeof SETTINGS_SECTIONS)[number];

export type Route =
  | { name: 'library' }
  /** `sessionId` rejoins an active session; without it the screen opens ready to record. */
  | { name: 'record'; sessionId: string | null }
  /** `atMs` (M4): open at that moment (a document's timestamp chip). */
  | { name: 'review'; recordingId: string; atMs?: number }
  | { name: 'settings'; section: SettingsSection }
  // M4
  /** The Document builder for a recording (null: a template opened from Settings), with a template and the document Regenerate writes into. */
  | { name: 'builder'; recordingId: string | null; templateId: string | null; documentId: string | null }
  /** `versionId` (after 1.2.0): open that kept version read-only (Review's History opens it). */
  | { name: 'document'; recordingId: string; documentId: string; versionId?: string }
  | { name: 'style'; styleId: string };

export const LIBRARY_ROUTE: Route = { name: 'library' };

function isSettingsSection(value: string): value is SettingsSection {
  return (SETTINGS_SECTIONS as readonly string[]).includes(value);
}

function decode(part: string | undefined): string | null {
  if (part === undefined || part === '') {
    return null;
  }
  try {
    return decodeURIComponent(part);
  } catch {
    return null;
  }
}

/** `#/review/abc` -> route. Anything unknown is the Library. */
export function parseRoute(hash: string): Route {
  const [path = '', search = ''] = hash.replace(/^#\/?/, '').split('?');
  const parts = path.split('/');
  const query = new URLSearchParams(search);
  const [name, arg] = parts;
  switch (name) {
    case 'record':
      return { name: 'record', sessionId: decode(arg) };
    case 'review': {
      const recordingId = decode(arg);
      if (recordingId === null) {
        return LIBRARY_ROUTE;
      }
      const t = Number(query.get('t'));
      return query.has('t') && Number.isFinite(t) && t >= 0 ? { name: 'review', recordingId, atMs: Math.round(t * 1000) } : { name: 'review', recordingId };
    }
    // M4
    case 'builder':
      return { name: 'builder', recordingId: decode(arg), templateId: query.get('template'), documentId: query.get('document') };
    case 'document': {
      const recordingId = decode(arg);
      const documentId = decode(parts[2]);
      if (recordingId === null || documentId === null) {
        return LIBRARY_ROUTE;
      }
      const versionId = query.get('version');
      return versionId === null || versionId === '' ? { name: 'document', recordingId, documentId } : { name: 'document', recordingId, documentId, versionId };
    }
    case 'style': {
      const styleId = decode(arg);
      return styleId === null ? LIBRARY_ROUTE : { name: 'style', styleId };
    }
    case 'settings': {
      const section = decode(arg) ?? 'general';
      return { name: 'settings', section: isSettingsSection(section) ? section : 'general' };
    }
    default:
      return LIBRARY_ROUTE;
  }
}

export function formatRoute(route: Route): string {
  switch (route.name) {
    case 'library':
      return '#/library';
    case 'record':
      return route.sessionId === null ? '#/record' : `#/record/${encodeURIComponent(route.sessionId)}`;
    case 'review':
      return `#/review/${encodeURIComponent(route.recordingId)}${route.atMs === undefined ? '' : `?t=${route.atMs / 1000}`}`;
    case 'settings':
      return `#/settings/${route.section}`;
    // M4
    case 'builder': {
      const query = new URLSearchParams();
      if (route.templateId !== null) {
        query.set('template', route.templateId);
      }
      if (route.documentId !== null) {
        query.set('document', route.documentId);
      }
      const search = query.toString();
      return `#/builder${route.recordingId === null ? '' : `/${encodeURIComponent(route.recordingId)}`}${search === '' ? '' : `?${search}`}`;
    }
    case 'document':
      return `#/document/${encodeURIComponent(route.recordingId)}/${encodeURIComponent(route.documentId)}${route.versionId === undefined ? '' : `?version=${encodeURIComponent(route.versionId)}`}`;
    case 'style':
      return `#/style/${encodeURIComponent(route.styleId)}`;
  }
}

/**
 * A key that changes when the screen changes (drives the spoke cross-fade). Settings sections and
 * the Record screen before and after Start (which adds the session id) stay one screen.
 */
export function screenKey(route: Route): string {
  return route.name === 'settings' || route.name === 'record' ? route.name : formatRoute(route);
}

/** The bits of `window` the router touches; tests pass a fake. */
export interface RouterWindow {
  location: { hash: string };
  scrollY: number;
  scrollTo(x: number, y: number): void;
  addEventListener(type: 'hashchange', listener: () => void): void;
  removeEventListener(type: 'hashchange', listener: () => void): void;
}

export interface RouterStore {
  route: Signal<Route>;
  libraryView: Signal<LibraryViewState>;
}

export interface Router {
  navigate(route: Route): void;
  /** Stops following hashchange. */
  dispose(): void;
}

/**
 * Keeps `store.route` and location.hash in step. Leaving the Library saves its scroll offset into
 * the store; LibraryScreen restores it (and focus) once it has painted again.
 */
export function createRouter(store: RouterStore, win: RouterWindow): Router {
  const apply = (next: Route): void => {
    const current = store.route.value;
    if (formatRoute(current) === formatRoute(next)) {
      return;
    }
    if (current.name === 'library' && next.name !== 'library') {
      store.libraryView.value = { ...store.libraryView.value, scrollY: win.scrollY };
    }
    store.route.value = next;
    if (next.name !== 'library') {
      win.scrollTo(0, 0);
    }
  };

  const onHashChange = (): void => {
    apply(parseRoute(win.location.hash));
  };
  win.addEventListener('hashchange', onHashChange);

  // A reload keeps the screen; an empty hash is the Library.
  if (win.location.hash !== '') {
    apply(parseRoute(win.location.hash));
  }

  return {
    navigate(route) {
      apply(route);
      const hash = formatRoute(route);
      if (win.location.hash !== hash) {
        win.location.hash = hash;
      }
    },
    dispose() {
      win.removeEventListener('hashchange', onHashChange);
    },
  };
}
