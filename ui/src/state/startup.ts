import { effect } from '@preact/signals';
import type { BridgeClient } from '../bridge/client';
import { loadInitialData, type AppStore } from './store';

/** How long ui.ready waits for the first footer status before going ahead without it. */
export const FOOTER_WAIT_MS = 2000;

function nextFrame(): Promise<void> {
  return new Promise((resolve) => {
    requestAnimationFrame(() => {
      resolve();
    });
  });
}

function firstFooter(store: AppStore, timeoutMs: number): Promise<void> {
  return new Promise((resolve) => {
    let done = false;
    const stop: { dispose?: () => void } = {};
    const finish = (): void => {
      if (done) {
        return;
      }
      done = true;
      clearTimeout(timer);
      stop.dispose?.();
      resolve();
    };
    const timer = setTimeout(finish, timeoutMs);
    stop.dispose = effect(() => {
      if (store.footer.value !== null) {
        queueMicrotask(finish);
      }
    });
  });
}

/**
 * Loads the first data, then tells the host the page is ready once fonts are in, the footer has
 * its first live values and two frames have painted. The host's --screenshot mode captures then.
 */
export async function startUp(bridge: BridgeClient, store: AppStore): Promise<void> {
  await loadInitialData(bridge, store);
  await document.fonts.ready;
  await firstFooter(store, FOOTER_WAIT_MS);
  await nextFrame();
  await nextFrame();
  await bridge.call('ui.ready');
}
