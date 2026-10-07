// Test helpers for screen tests against the browser-preview host (M3 tests use them; nothing in the
// app imports this file).
import { render } from 'preact';
import { act } from 'preact/test-utils';
import { App } from '../App';
import { createBridgeClient, type BridgeClient } from '../bridge/client';
import type { MockOptions } from '../bridge/mock';
import type { MethodName } from '../bridge/types';
import { loadInitialData, refreshLibrary } from '../state/data';
import type { Route } from '../state/router';
import { connectEvents, createStore, type AppStore } from '../state/store';

const quiet = { info: () => undefined, warn: () => undefined };

export interface Harness {
  container: HTMLDivElement;
  store: AppStore;
  bridge: BridgeClient;
  /** Every bridge call made since mounting: [method, params]. */
  calls: [string, unknown][];
  callsOf: (method: MethodName) => unknown[];
  unmount: () => void;
}

export const settle = async (ms = 0): Promise<void> => {
  await act(async () => {
    await new Promise((resolve) => setTimeout(resolve, ms));
  });
};

export const until = async (check: () => boolean, timeoutMs = 8000): Promise<void> => {
  const started = Date.now();
  while (!check()) {
    if (Date.now() - started > timeoutMs) {
      throw new Error(`timed out waiting for the screen: ${document.body.textContent.slice(0, 300)}`);
    }
    await settle(20);
  }
};

/** A button (or menu item, option) by its accessible name or text. */
export const button = (name: string): HTMLButtonElement => {
  const match = [...document.querySelectorAll<HTMLButtonElement>('button, [role="menuitem"], [role="option"]')].find(
    (b) => (b.getAttribute('aria-label') ?? b.textContent.trim()) === name || b.textContent.trim() === name,
  );
  if (match === undefined) {
    throw new Error(`no button named ${name}`);
  }
  return match;
};

export const click = async (el: Element | null | undefined): Promise<void> => {
  if (el === null || el === undefined) {
    throw new Error('nothing to click');
  }
  await act(async () => {
    (el as HTMLElement).click();
    await Promise.resolve();
  });
};

export const type = async (el: Element | null, value: string): Promise<void> => {
  if (!(el instanceof HTMLInputElement || el instanceof HTMLTextAreaElement)) {
    throw new Error('not a text field');
  }
  await act(async () => {
    el.value = value;
    el.dispatchEvent(new Event('input', { bubbles: true }));
    await Promise.resolve();
  });
};

export const press = async (el: Element | null, key: string): Promise<void> => {
  await act(async () => {
    (el as HTMLElement | null)?.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true }));
    await Promise.resolve();
  });
};

/** jsdom has no DataTransfer: a drop event carrying files by name (and optional text). */
export const dropFiles = async (target: Element | null, names: string[], text = ''): Promise<void> => {
  const event = new Event('drop', { bubbles: true, cancelable: true });
  Object.defineProperty(event, 'dataTransfer', {
    value: { files: names.map((name) => ({ name })), getData: () => text, dropEffect: 'none' },
  });
  await act(async () => {
    target?.dispatchEvent(event);
    await Promise.resolve();
  });
};

export async function mountApp(route: Route, mock: MockOptions = {}, now?: Date): Promise<Harness> {
  const container = document.createElement('div');
  document.body.append(container);
  const bridge = createBridgeClient({
    logger: quiet,
    mock: { live: false, recovery: false, stepMs: 10, ...(now === undefined ? {} : { now: () => now.getTime() }), ...mock },
  });
  const store = createStore(false);
  const disconnect = connectEvents(bridge, store, {
    onLibraryChanged: () => {
      void refreshLibrary(bridge, store);
    },
  });
  await act(async () => {
    await loadInitialData(bridge, store);
  });
  store.route.value = route;
  const calls: [string, unknown][] = [];
  const original = bridge.call.bind(bridge);
  (bridge as { call: unknown }).call = (method: string, params?: unknown) => {
    calls.push([method, params]);
    return (original as (m: string, p?: unknown) => Promise<unknown>)(method, params);
  };
  await act(async () => {
    render(<App bridge={bridge} store={store} {...(now === undefined ? {} : { now: () => now })} />, container);
    await Promise.resolve();
  });
  return {
    container,
    store,
    bridge,
    calls,
    callsOf: (method) => calls.filter(([m]) => m === method).map(([, p]) => p),
    unmount: () => {
      render(null, container);
      disconnect();
      container.remove();
      document.body.innerHTML = '';
    },
  };
}
