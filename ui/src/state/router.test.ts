import { describe, expect, it, vi } from 'vitest';
import { createStore } from './store';
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

  it('keeps one screen key for all of Settings so changing section does not cross-fade', () => {
    expect(screenKey({ name: 'settings', section: 'general' })).toBe(screenKey({ name: 'settings', section: 'storage' }));
    expect(screenKey({ name: 'review', recordingId: 'a' })).not.toBe(screenKey({ name: 'review', recordingId: 'b' }));
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
