import { createMockTransport } from './mock';
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
}

export const DEFAULT_TIMEOUT_MS = 10_000;

/** The messaging surface WebView2 exposes as window.chrome.webview. */
export interface WebViewMessaging {
  postMessage(message: unknown): void;
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
  const transport =
    options.transport ?? (webview !== undefined ? webViewTransport(webview) : createMockTransport(logger));

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

  return {
    isHosted,
    call<M extends MethodName>(method: M, ...args: ParamsArgument<M>): Promise<MethodResult<M>> {
      const id = nextId++;
      const params = (args[0] ?? {}) as MethodParams<M>;
      return new Promise<MethodResult<M>>((resolve, reject) => {
        const timer = setTimeout(() => {
          pending.delete(id);
          reject(
            new BridgeCallError(method, {
              code: 'bridge.timeout',
              message: `Memento did not answer '${method}' within ${Math.round(timeoutMs / 1000)} s.`,
              detail: null,
            }),
          );
        }, timeoutMs);
        pending.set(id, {
          method,
          resolve: resolve as (result: unknown) => void,
          reject,
          timer,
        });
        try {
          transport.send({ id, method, params });
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
