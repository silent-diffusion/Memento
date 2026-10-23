import { describe, expect, it, vi } from 'vitest';
import { createStore } from './store';
import { backTargetOf, navigationOf } from './navigation';
import { createRouter, formatRoute, parseRoute, screenKey, type Route, type RouterWindow } from './router';

class FakeWindow implements RouterWindow {
  location = { hash: '' };
  scrollY = 0;
  private readonly listeners = new Set<() => void>();
  readonly scrollTo = vi.fn((_x: number, y: number) => {
    this.scrollY = y;
  });

  addEventListener(_type: 'hashchange', listener: () => void): void {
    this.listeners.add(listener);
  }

  removeEventListener(_type: 'hashchange', listener: () => void): void {
    this.listeners.delete(listener);
  }

  /** The user pressed Back, or typed a URL. */
  changeHash(hash: string): void {
    this.location.hash = hash;
    for (const listener of this.listeners) {
      listener();
    }
  }
}

describe('route parsing', () => {
  it.each<[string, Route]>([
    ['', { name: 'library' }],
    ['#/library', { name: 'library' }],
    ['#/record', { name: 'record', sessionId: null }],
    ['#/record/session-1', { name: 'record', sessionId: 'session-1' }],
    ['#/review/20261006-100000-k3f9ab', { name: 'review', recordingId: '20261006-100000-k3f9ab' }],
    ['#/settings/recording', { name: 'settings', section: 'recording' }],
    ['#/settings', { name: 'settings', section: 'general' }],
    ['#/settings/nonsense', { name: 'settings', section: 'general' }],
    ['#/review', { name: 'library' }],
    ['#/unknown', { name: 'library' }],
  ])('%s', (hash, route) => {
    expect(parseRoute(hash)).toEqual(route);
  });

  it('round-trips every route, encoding ids', () => {
    const routes: Route[] = [
      { name: 'library' },
      { name: 'record', sessionId: null },
      { name: 'record', sessionId: 'a b' },
      { name: 'review', recordingId: 'x/y' },
      { name: 'settings', section: 'ai-privacy' },
    ];
    for (const route of routes) {
      expect(parseRoute(formatRoute(route))).toEqual(route);
    }
  });

  it('parses and formats the M4 routes: Builder, Document viewer, Style editor and Review at a moment', () => {
    const routes: Route[] = [
      { name: 'review', recordingId: 'r1', atMs: 1_122_000 },
      { name: 'builder', recordingId: 'r1', templateId: null, documentId: null },
      { name: 'builder', recordingId: 'r1', templateId: 'meeting-minutes', documentId: 'doc 1' },
      { name: 'builder', recordingId: null, templateId: 'interview-notes', documentId: null },
      { name: 'document', recordingId: 'r1', documentId: 'doc-1' },
      { name: 'style', styleId: 'corporate' },
    ];
    for (const route of routes) {
      expect(parseRoute(formatRoute(route))).toEqual(route);
    }
    expect(formatRoute({ name: 'review', recordingId: 'r1', atMs: 65_500 })).toBe('#/review/r1?t=65.5');
    expect(formatRoute({ name: 'builder', recordingId: null, templateId: 'x', documentId: null })).toBe('#/builder?template=x');
    expect(parseRoute('#/review/r1?t=soon')).toEqual({ name: 'review', recordingId: 'r1' });
    expect(parseRoute('#/document/r1')).toEqual({ name: 'library' });
    expect(parseRoute('#/style')).toEqual({ name: 'library' });
  });

  it('keeps one screen key for all of Settings so changing section does not cross-fade', () => {
    expect(screenKey({ name: 'settings', section: 'general' })).toBe(screenKey({ name: 'settings', section: 'storage' }));
    expect(screenKey({ name: 'review', recordingId: 'a' })).not.toBe(screenKey({ name: 'review', recordingId: 'b' }));
  });

  it('keeps the Record screen mounted when Start adds the session id', () => {
    expect(screenKey({ name: 'record', sessionId: null })).toBe(screenKey({ name: 'record', sessionId: 'session-1' }));
  });
});

describe('router state restore', () => {
  it('keeps search, filter, sort and layout in the store across a spoke and saves the scroll offset', () => {
    const store = createStore(false);
    const win = new FakeWindow();
    const router = createRouter(store, win);
    store.libraryView.value = { ...store.libraryView.value, query: 'priya', type: 'interview', sort: 'oldest', layout: 'grid' };
    win.scrollY = 640;

    router.navigate({ name: 'review', recordingId: 'r1' });
    expect(store.route.value).toEqual({ name: 'review', recordingId: 'r1' });
    expect(win.location.hash).toBe('#/review/r1');
    expect(store.libraryView.value.scrollY).toBe(640);
    expect(win.scrollTo).toHaveBeenLastCalledWith(0, 0);

    router.navigate({ name: 'library' });
    expect(store.libraryView.value).toMatchObject({ query: 'priya', type: 'interview', sort: 'oldest', layout: 'grid', scrollY: 640 });
  });

  it('follows Back and Forward through hashchange', () => {
    const store = createStore(false);
    const win = new FakeWindow();
    createRouter(store, win);

    win.changeHash('#/settings/storage');
    expect(store.route.value).toEqual({ name: 'settings', section: 'storage' });
    win.changeHash('#/library');
    expect(store.route.value).toEqual({ name: 'library' });
  });

  it('does not overwrite the saved offset when moving between spokes', () => {
    const store = createStore(false);
    const win = new FakeWindow();
    const router = createRouter(store, win);
    win.scrollY = 300;
    router.navigate({ name: 'settings', section: 'general' });
    win.scrollY = 900;
    router.navigate({ name: 'settings', section: 'recording' });
    router.navigate({ name: 'review', recordingId: 'x' });
    expect(store.libraryView.value.scrollY).toBe(300);
  });

  it('records every move in the navigation trail: Back from Settings returns to the spoke that opened it', () => {
    const store = createStore(false);
    const win = new FakeWindow();
    const router = createRouter(store, win);
    const nav = navigationOf(store);
    router.navigate({ name: 'review', recordingId: 'r1' });
    nav.label = 'Design review';
    router.navigate({ name: 'settings', section: 'transcription' });
    const back = backTargetOf(nav, store.route.value, { route: { name: 'library' }, label: 'Library' });
    expect(back).toEqual({ route: { name: 'review', recordingId: 'r1' }, label: 'Design review' });
    router.navigate(back.route, { back: true });
    expect(win.location.hash).toBe('#/review/r1');
    expect(nav.trail).toEqual([{ route: { name: 'library' }, label: null }]);
    // The browser's own Back (a hashchange to the screen on top of the trail) steps back too.
    router.navigate({ name: 'settings', section: 'general' });
    win.changeHash('#/review/r1');
    expect(nav.trail).toEqual([{ route: { name: 'library' }, label: null }]);
  });

  it('opens on the screen in the hash after a reload', () => {
    const store = createStore(false);
    const win = new FakeWindow();
    win.location.hash = '#/settings/recording';
    createRouter(store, win);
    expect(store.route.value).toEqual({ name: 'settings', section: 'recording' });
  });

  it('stops listening once disposed', () => {
    const store = createStore(false);
    const win = new FakeWindow();
    createRouter(store, win).dispose();
    win.changeHash('#/settings/general');
    expect(store.route.value).toEqual({ name: 'library' });
  });
});

describe('the leave guard (a screen with unsaved changes asks before the page history leaves it, 2.4.0)', () => {
  const builder: Route = { name: 'builder', recordingId: 'r1', templateId: null, documentId: null };

  it('holds a history move the guard takes, shows the screen again, and makes the move once the person agrees', () => {
    const store = createStore(false);
    const win = new FakeWindow();
    const router = createRouter(store, win);
    router.navigate({ name: 'review', recordingId: 'r1' });
    router.navigate(builder);
    const nav = navigationOf(store);
    const held: (() => void)[] = [];
    nav.leaveGuard = (proceed) => {
      held.push(proceed);
      return true;
    };
    win.changeHash('#/review/r1');
    expect(store.route.value).toEqual(builder);
    expect(win.location.hash).toBe(formatRoute(builder));
    expect(held).toHaveLength(1);
    nav.leaveGuard = null;
    held[0]?.();
    expect(store.route.value).toEqual({ name: 'review', recordingId: 'r1' });
    expect(win.location.hash).toBe('#/review/r1');
  });

  it('lets the move through when the guard has nothing to lose, or when there is no guard', () => {
    const store = createStore(false);
    const win = new FakeWindow();
    const router = createRouter(store, win);
    router.navigate(builder);
    const nav = navigationOf(store);
    const asked = vi.fn(() => false);
    nav.leaveGuard = asked;
    win.changeHash('#/review/r1');
    expect(asked).toHaveBeenCalledTimes(1);
    expect(store.route.value).toEqual({ name: 'review', recordingId: 'r1' });
    nav.leaveGuard = null;
    win.changeHash('#/library');
    expect(store.route.value).toEqual({ name: 'library' });
  });

  it('is not asked for the app’s own moves (Back asks through the screen itself) or a hash that keeps the screen', () => {
    const store = createStore(false);
    const win = new FakeWindow();
    const router = createRouter(store, win);
    router.navigate(builder);
    const nav = navigationOf(store);
    const asked = vi.fn(() => true);
    nav.leaveGuard = asked;
    win.changeHash(formatRoute(builder));
    router.navigate({ name: 'settings', section: 'ai-privacy' });
    expect(asked).not.toHaveBeenCalled();
    expect(store.route.value).toEqual({ name: 'settings', section: 'ai-privacy' });
  });
});
