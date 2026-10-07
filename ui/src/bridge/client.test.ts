import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { BridgeCallError, createBridgeClient, webViewTransport, type BridgeLogger, type BridgeTransport, type WebViewMessaging } from './client';
import type { BridgeRequest } from './types';

class FakeTransport implements BridgeTransport {
  readonly sent: BridgeRequest[] = [];
  private readonly listeners = new Set<(message: unknown) => void>();

  send(request: BridgeRequest): void {
    this.sent.push(request);
  }

  subscribe(listener: (message: unknown) => void): () => void {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  }

  deliver(message: unknown): void {
    for (const listener of this.listeners) {
      listener(message);
    }
  }

  last(): BridgeRequest {
    const request = this.sent.at(-1);
    if (request === undefined) {
      throw new Error('nothing was sent');
    }
    return request;
  }
}

const quietLogger = (): BridgeLogger => ({ info: vi.fn(), warn: vi.fn() });

describe('bridge client calls', () => {
  let transport: FakeTransport;

  beforeEach(() => {
    transport = new FakeTransport();
  });

  it('sends the request envelope with increasing ids and empty params by default', () => {
    const client = createBridgeClient({ transport, logger: quietLogger() });

    void client.call('app.version');
    void client.call('settings.set', { theme: 'dark' });

    expect(transport.sent).toEqual([
      { id: 1, method: 'app.version', params: {} },
      { id: 2, method: 'settings.set', params: { theme: 'dark' } },
    ]);
  });

  it('resolves each call with the result carrying its id, in any order', async () => {
    const client = createBridgeClient({ transport, logger: quietLogger() });

    const version = client.call('app.version');
    const library = client.call('library.list');
    transport.deliver({ id: 2, result: { recordings: [], totalDurationMs: 0 } });
    transport.deliver({ id: 1, result: { version: '0.1.0', osVersion: 'Windows 10.0.26200', isDarkTheme: true } });

    await expect(library).resolves.toEqual({ recordings: [], totalDurationMs: 0 });
    await expect(version).resolves.toEqual({ version: '0.1.0', osVersion: 'Windows 10.0.26200', isDarkTheme: true });
  });

  it('rejects with the structured host error', async () => {
    const client = createBridgeClient({ transport, logger: quietLogger() });

    const call = client.call('settings.set', { theme: 'dark' });
    transport.deliver({
      id: transport.last().id,
      error: { code: 'settings.invalidValue', message: 'Theme is not available.', detail: null },
    });

    const error: unknown = await call.catch((e: unknown) => e);
    expect(error).toBeInstanceOf(BridgeCallError);
    expect(error).toMatchObject({ code: 'settings.invalidValue', message: 'Theme is not available.', method: 'settings.set' });
  });

  it('ignores answers for unknown ids and warns', () => {
    const logger = quietLogger();
    createBridgeClient({ transport, logger });

    transport.deliver({ id: 99, result: {} });

    expect(logger.warn).toHaveBeenCalledWith(expect.stringContaining('#99'));
  });

  it('ignores non-object messages', () => {
    const logger = quietLogger();
    createBridgeClient({ transport, logger });

    transport.deliver('hello');
    transport.deliver(null);

    expect(logger.warn).toHaveBeenCalledTimes(2);
  });

  it('rejects when sending throws', async () => {
    const client = createBridgeClient({
      transport: {
        send: () => {
          throw new Error('gone');
        },
        subscribe: () => () => undefined,
      },
      logger: quietLogger(),
    });

    await expect(client.call('app.version')).rejects.toMatchObject({ code: 'bridge.sendFailed', detail: 'gone' });
  });
});

describe('bridge client timeouts', () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('rejects a call that gets no answer in time and drops a late answer', async () => {
    const transport = new FakeTransport();
    const logger = quietLogger();
    const client = createBridgeClient({ transport, timeoutMs: 500, logger });

    const call = client.call('library.list');
    const outcome = call.catch((e: unknown) => e);
    vi.advanceTimersByTime(499);
    let settled = false;
    void outcome.then(() => {
      settled = true;
    });
    await Promise.resolve();
    expect(settled).toBe(false);

    vi.advanceTimersByTime(1);
    const error = await outcome;
    expect(error).toBeInstanceOf(BridgeCallError);
    expect(error).toMatchObject({ code: 'bridge.timeout', method: 'library.list' });

    transport.deliver({ id: 1, result: { recordings: [], totalDurationMs: 0 } });
    expect(logger.warn).toHaveBeenCalledWith(expect.stringContaining('#1'));
  });

  it('clears the timer when the answer arrives', async () => {
    const transport = new FakeTransport();
    const client = createBridgeClient({ transport, timeoutMs: 500, logger: quietLogger() });

    const call = client.call('ui.ready');
    transport.deliver({ id: 1, result: {} });
    await expect(call).resolves.toEqual({});
    expect(vi.getTimerCount()).toBe(0);
  });
});

describe('bridge client events', () => {
  it('dispatches events to every handler for that event only', () => {
    const transport = new FakeTransport();
    const client = createBridgeClient({ transport, logger: quietLogger() });
    const theme = vi.fn();
    const themeToo = vi.fn();
    const footer = vi.fn();
    client.on('theme.changed', theme);
    client.on('theme.changed', themeToo);
    client.on('status.footer', footer);

    transport.deliver({ event: 'theme.changed', payload: { isDark: true } });

    expect(theme).toHaveBeenCalledWith({ isDark: true });
    expect(themeToo).toHaveBeenCalledWith({ isDark: true });
    expect(footer).not.toHaveBeenCalled();
  });

  it('stops delivering after unsubscribe', () => {
    const transport = new FakeTransport();
    const client = createBridgeClient({ transport, logger: quietLogger() });
    const handler = vi.fn();
    const off = client.on('status.footer', handler);

    off();
    transport.deliver({
      event: 'status.footer',
      payload: { engine: { ready: false, device: null }, storage: { freeBytes: 1, lowSpace: true } },
    });

    expect(handler).not.toHaveBeenCalled();
  });

  it('keeps delivering when one handler throws', () => {
    const transport = new FakeTransport();
    const logger = quietLogger();
    const client = createBridgeClient({ transport, logger });
    const healthy = vi.fn();
    client.on('theme.changed', () => {
      throw new Error('broken handler');
    });
    client.on('theme.changed', healthy);

    transport.deliver({ event: 'theme.changed', payload: { isDark: false } });

    expect(healthy).toHaveBeenCalledWith({ isDark: false });
    expect(logger.warn).toHaveBeenCalled();
  });
});

describe('transports', () => {
  it('uses window.chrome.webview when the host provides it', () => {
    const listeners: ((event: { data: unknown }) => void)[] = [];
    const posted: unknown[] = [];
    const webview: WebViewMessaging = {
      postMessage: (message) => posted.push(message),
      addEventListener: (_type, listener) => listeners.push(listener),
      removeEventListener: vi.fn(),
    };
    window.chrome = { webview };
    try {
      const client = createBridgeClient({ logger: quietLogger() });
      const call = client.call('ui.ready');
      for (const listener of listeners) {
        listener({ data: { id: 1, result: {} } });
      }

      expect(client.isHosted).toBe(true);
      expect(posted).toEqual([{ id: 1, method: 'ui.ready', params: {} }]);
      return expect(call).resolves.toEqual({});
    } finally {
      delete window.chrome;
    }
  });

  it('callWithFiles posts the request with the files through postMessageWithAdditionalObjects', async () => {
    const listeners: ((event: { data: unknown }) => void)[] = [];
    const plain: unknown[] = [];
    const withObjects: [unknown, unknown[]][] = [];
    const webview: WebViewMessaging = {
      postMessage: (message) => plain.push(message),
      postMessageWithAdditionalObjects: (message, objects) => withObjects.push([message, Array.from(objects)]),
      addEventListener: (_type, listener) => listeners.push(listener),
      removeEventListener: vi.fn(),
    };
    const client = createBridgeClient({ transport: webViewTransport(webview), logger: quietLogger() });
    const file = new File(['1. Welcome'], 'agenda.docx');

    const call = client.callWithFiles('agenda.importDropped', { recordingId: null, paths: ['agenda.docx'] }, [file]);
    for (const listener of listeners) {
      listener({ data: { id: 1, result: { preview: null, cancelled: true } } });
    }

    expect(plain).toEqual([]);
    expect(withObjects).toEqual([[{ id: 1, method: 'agenda.importDropped', params: { recordingId: null, paths: ['agenda.docx'] } }, [file]]]);
    await expect(call).resolves.toEqual({ preview: null, cancelled: true });
  });

  it('callWithFiles falls back to postMessage without postMessageWithAdditionalObjects or files', () => {
    const plain: unknown[] = [];
    const webview: WebViewMessaging = { postMessage: (message) => plain.push(message), addEventListener: vi.fn(), removeEventListener: vi.fn() };
    const client = createBridgeClient({ transport: webViewTransport(webview), logger: quietLogger() });

    void client.callWithFiles('agenda.importDropped', { recordingId: null, paths: ['a.docx'] }, [new File([''], 'a.docx')]);

    expect(plain).toEqual([{ id: 1, method: 'agenda.importDropped', params: { recordingId: null, paths: ['a.docx'] } }]);
  });

  it('callWithFiles on a transport without sendWithFiles sends the request alone', () => {
    const transport = new FakeTransport();
    const client = createBridgeClient({ transport, logger: quietLogger() });

    void client.callWithFiles('agenda.importDropped', { recordingId: null, paths: ['a.docx'] }, []);

    expect(transport.last()).toEqual({ id: 1, method: 'agenda.importDropped', params: { recordingId: null, paths: ['a.docx'] } });
  });

  it('the browser-preview host reads the names of the dropped files as their paths', async () => {
    const client = createBridgeClient({ logger: quietLogger(), mock: { live: false, recovery: false } });

    const result = await client.callWithFiles('agenda.importDropped', { recordingId: null, paths: [] }, [new File([''], 'agenda.docx')]);

    expect(result.preview?.source).toBe('agenda.docx');
  });

  it('webViewTransport unsubscribes from the host', () => {
    const removeEventListener = vi.fn();
    const webview: WebViewMessaging = {
      postMessage: vi.fn(),
      addEventListener: vi.fn(),
      removeEventListener,
    };

    webViewTransport(webview).subscribe(vi.fn())();

    expect(removeEventListener).toHaveBeenCalledWith('message', expect.any(Function));
  });

  it('falls back to logged mock data in a plain browser', async () => {
    const logger = quietLogger();
    const client = createBridgeClient({ logger, mock: { live: false } });
    const footer = vi.fn();
    client.on('status.footer', footer);

    const library = await client.call('library.list');

    expect(client.isHosted).toBe(false);
    expect(library.totalCount).toBe(library.recordings.length);
    expect(library.totalCount).toBeGreaterThan(10);
    expect(logger.info).toHaveBeenCalledWith(expect.stringContaining('window.chrome.webview is absent'));
    expect(footer).toHaveBeenCalledWith(
      expect.objectContaining({
        recording: { active: false, lastCheckpointAt: null, lostSource: null },
        processingPaused: null,
      }),
    );
  });

  it('answers the empty library when asked to', async () => {
    const client = createBridgeClient({ logger: quietLogger(), mock: { library: 'empty', live: false } });

    await expect(client.call('library.list')).resolves.toEqual({ recordings: [], totalDurationMs: 0, totalCount: 0 });
  });
});
