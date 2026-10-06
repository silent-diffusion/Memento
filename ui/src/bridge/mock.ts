import type { BridgeTransport, BridgeLogger } from './client';
import type {
  BridgeEventEnvelope,
  BridgeRequest,
  BridgeResponse,
  MethodName,
  SettingsSetParams,
  SettingsSnapshot,
} from './types';

/**
 * Stand-in for the host when the page runs in a plain browser (npm run dev). It answers every
 * method with neutral sample data and says so in the console. It never invents numbers the real
 * host would measure: free space is "unknown" and the engine is "not ready".
 */
export function createMockTransport(logger: BridgeLogger): BridgeTransport {
  const listeners = new Set<(message: unknown) => void>();
  const prefersDark = (): boolean =>
    typeof window !== 'undefined' && typeof window.matchMedia === 'function'
      ? window.matchMedia('(prefers-color-scheme: dark)').matches
      : false;

  let settings: SettingsSnapshot = {
    theme: 'system',
    libraryPath: 'Library (browser preview)',
    listDensity: 'comfortable',
  };

  const emit = (message: BridgeResponse | BridgeEventEnvelope): void => {
    setTimeout(() => {
      for (const listener of listeners) {
        listener(message);
      }
    }, 0);
  };

  const answer = (request: BridgeRequest): unknown => {
    const method: MethodName = request.method;
    switch (method) {
      case 'app.version':
        return { version: '0.0.0-dev', osVersion: 'Browser preview', isDarkTheme: prefersDark() };
      case 'settings.get':
        return settings;
      case 'settings.set': {
        const update = request.params as SettingsSetParams;
        settings = {
          ...settings,
          theme: update.theme ?? settings.theme,
          listDensity: update.listDensity ?? settings.listDensity,
        };
        return settings;
      }
      case 'library.list':
        return { recordings: [], totalDurationMs: 0 };
      case 'app.openExternal':
        return { opened: false };
      case 'ui.ready':
        return {};
    }
  };

  logger.info('[bridge] window.chrome.webview is absent; answering with browser-preview mock data.');

  return {
    send: (request) => {
      logger.info(`[bridge:mock] ${request.method}`, request.params);
      emit({ id: request.id, result: answer(request) });
    },
    subscribe: (listener) => {
      listeners.add(listener);
      // The real host pushes the footer status as soon as the page has loaded.
      emit({
        event: 'status.footer',
        payload: { engine: { ready: false, device: null }, storage: { freeBytes: null, lowSpace: false } },
      });
      return () => {
        listeners.delete(listener);
      };
    },
  };
}
