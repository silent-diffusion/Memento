import { createMockTransport, mockOptionsFromQuery, type MockOptions } from './mock';
import type {
  BridgeError,
  BridgeRequest,
  EmptyParams,
  EventName,
  EventPayload,
  MethodName,
  MethodParams,
  MethodResult,
} from './types';

/** Moves bridge messages between the page and the host. */
export interface BridgeTransport {
  send(request: BridgeRequest): void;
  /**
   * Sends a request together with dropped `File` objects, so the host learns their real paths
   * (WebView2: `postMessageWithAdditionalObjects`). Transports without it send the request alone.
   */
  sendWithFiles?(request: BridgeRequest, files: readonly File[]): void;
  /** Subscribes to every message from the host; returns the unsubscribe function. */
  subscribe(listener: (message: unknown) => void): () => void;
}

export interface BridgeLogger {
  info: (message: string, ...details: unknown[]) => void;
  warn: (message: string, ...details: unknown[]) => void;
}

/** A host method answered with an error, or did not answer in time. */
export class BridgeCallError extends Error {
  readonly code: string;
  readonly detail: string | null;
  readonly method: MethodName;

  constructor(method: MethodName, error: BridgeError) {
    super(error.message);
    this.name = 'BridgeCallError';
    this.method = method;
    this.code = error.code;
    this.detail = error.detail;
  }
}

type ParamsArgument<M extends MethodName> = EmptyParams extends MethodParams<M>
  ? [params?: MethodParams<M>]
  : [params: MethodParams<M>];

export interface BridgeClient {
  /** Calls a host method; resolves with its result or rejects with a {@link BridgeCallError}. */
  call<M extends MethodName>(method: M, ...params: ParamsArgument<M>): Promise<MethodResult<M>>;
  /**
   * Like {@link call}, with dropped files attached to the message (BRIDGE.md M3, drag and drop):
   * the host matches the names in `params` against the paths WebView2 hands it with the files.
   */
  callWithFiles<M extends MethodName>(method: M, params: MethodParams<M>, files: readonly File[]): Promise<MethodResult<M>>;
  /** Subscribes to a host event; returns the unsubscribe function. */
  on<E extends EventName>(event: E, handler: (payload: EventPayload<E>) => void): () => void;
  /** True inside the Memento window; false in a plain browser (npm run dev), where mock data answers. */
  readonly isHosted: boolean;
}

export interface BridgeClientOptions {
  transport?: BridgeTransport;
  /** How long a call may wait for its answer. */
  timeoutMs?: number;
  logger?: BridgeLogger;
  /** Browser-preview host behaviour; defaults to the flags in the page URL (see mock.ts). */
  mock?: MockOptions;
}

export const DEFAULT_TIMEOUT_MS = 10_000;

/**
 * Calls that can open a Windows picker and so wait for the person, not for Memento: a folder or file chosen after ten
 * seconds of browsing is not a host that stopped answering. They get this much longer limit instead.
 */
export const PICKER_METHODS: ReadonlySet<MethodName> = new Set<MethodName>(['dialog.pickFolder', 'agenda.importFile', 'attachments.add', 'library.importMedia']);
export const PICKER_TIMEOUT_MS = 30 * 60_000;

/** The messaging surface WebView2 exposes as window.chrome.webview. */
export interface WebViewMessaging {
  postMessage(message: unknown): void;
  /** Posts the message with DOM objects; the host receives `File`s as CoreWebView2File with their paths. */
  postMessageWithAdditionalObjects?(message: unknown, additionalObjects: ArrayLike<unknown>): void;
  addEventListener(type: 'message', listener: (event: { data: unknown }) => void): void;
  removeEventListener(type: 'message', listener: (event: { data: unknown }) => void): void;
}

declare global {
  interface Window {
    chrome?: { webview?: WebViewMessaging };
  }
}

export function webViewTransport(webview: WebViewMessaging): BridgeTransport {
  return {
    send: (request) => {
      webview.postMessage(request);
    },
    sendWithFiles: (request, files) => {
      if (files.length > 0 && typeof webview.postMessageWithAdditionalObjects === 'function') {
        webview.postMessageWithAdditionalObjects(request, [...files]);
      } else {
        webview.postMessage(request);
      }
    },
    subscribe: (listener) => {
      const handler = (event: { data: unknown }): void => {
        listener(event.data);
      };
      webview.addEventListener('message', handler);
      return () => {
        webview.removeEventListener('message', handler);
      };
    },
  };
}

interface Pending {
  method: MethodName;
  resolve: (result: unknown) => void;
  reject: (error: BridgeCallError) => void;
  timer: ReturnType<typeof setTimeout>;
}

type AnyHandler = (payload: never) => void;

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function isBridgeError(value: unknown): value is BridgeError {
  return isRecord(value) && typeof value.code === 'string' && typeof value.message === 'string';
}

export function createBridgeClient(options: BridgeClientOptions = {}): BridgeClient {
  const logger = options.logger ?? console;
  const timeoutMs = options.timeoutMs ?? DEFAULT_TIMEOUT_MS;
  const webview = typeof window === 'undefined' ? undefined : window.chrome?.webview;
  const isHosted = options.transport === undefined ? webview !== undefined : true;
  const mockOptions =
    options.mock ?? (typeof window === 'undefined' ? {} : mockOptionsFromQuery(window.location.search));
  const transport =
    options.transport ?? (webview !== undefined ? webViewTransport(webview) : createMockTransport(logger, mockOptions));

  const pending = new Map<number, Pending>();
  const handlers = new Map<EventName, Set<AnyHandler>>();
  let nextId = 1;

  const dispatchEvent = (name: string, payload: unknown): void => {
    const subscribers = handlers.get(name as EventName);
    if (subscribers === undefined || subscribers.size === 0) {
      return;
    }
    for (const handler of subscribers) {
      try {
        (handler as (payload: unknown) => void)(payload);
      } catch (error) {
        logger.warn(`[bridge] a handler for '${name}' failed`, error);
      }
    }
  };

  const settle = (message: Record<string, unknown>): void => {
    const id = message.id;
    if (typeof id !== 'number') {
      if (isBridgeError(message.error)) {
        logger.warn(`[bridge] the host rejected a malformed message: ${message.error.message}`);
      }
      return;
    }
    const call = pending.get(id);
    if (call === undefined) {
      logger.warn(`[bridge] ignored an answer for unknown or expired call #${id}`);
      return;
    }
    pending.delete(id);
    clearTimeout(call.timer);
    if (isBridgeError(message.error)) {
      call.reject(new BridgeCallError(call.method, message.error));
    } else {
      call.resolve(message.result);
    }
  };

  transport.subscribe((message) => {
    if (!isRecord(message)) {
      logger.warn('[bridge] ignored a message that is not an object');
      return;
    }
    if (typeof message.event === 'string') {
      dispatchEvent(message.event, message.payload);
      return;
    }
    settle(message);
  });

  const invoke = <M extends MethodName>(method: M, params: MethodParams<M>, files: readonly File[] | null): Promise<MethodResult<M>> => {
    const id = nextId++;
    const limitMs = PICKER_METHODS.has(method) ? Math.max(timeoutMs, PICKER_TIMEOUT_MS) : timeoutMs;
    return new Promise<MethodResult<M>>((resolve, reject) => {
      const timer = setTimeout(() => {
        pending.delete(id);
        reject(
          new BridgeCallError(method, {
            code: 'bridge.timeout',
            message: `Memento did not answer '${method}' within ${Math.round(limitMs / 1000)} s.`,
            detail: null,
          }),
        );
      }, limitMs);
      pending.set(id, {
        method,
        resolve: resolve as (result: unknown) => void,
        reject,
        timer,
      });
      try {
        const request: BridgeRequest = { id, method, params };
        if (files !== null && transport.sendWithFiles !== undefined) {
          transport.sendWithFiles(request, files);
        } else {
          transport.send(request);
        }
      } catch (error) {
        pending.delete(id);
        clearTimeout(timer);
        reject(
          new BridgeCallError(method, {
            code: 'bridge.sendFailed',
            message: `The request '${method}' could not be sent to Memento.`,
            detail: error instanceof Error ? error.message : null,
          }),
        );
      }
    });
  };

  return {
    isHosted,
    call<M extends MethodName>(method: M, ...args: ParamsArgument<M>): Promise<MethodResult<M>> {
      return invoke(method, (args[0] ?? {}) as MethodParams<M>, null);
    },
    callWithFiles<M extends MethodName>(method: M, params: MethodParams<M>, files: readonly File[]): Promise<MethodResult<M>> {
      return invoke(method, params, files);
    },
    on<E extends EventName>(event: E, handler: (payload: EventPayload<E>) => void): () => void {
      let subscribers = handlers.get(event);
      if (subscribers === undefined) {
        subscribers = new Set();
        handlers.set(event, subscribers);
      }
      subscribers.add(handler);
      return () => {
        subscribers.delete(handler);
      };
    },
  };
}
