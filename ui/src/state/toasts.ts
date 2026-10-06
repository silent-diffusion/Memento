// Toasts (DESIGN.md §5.19): bottom-right, 6 s, pause while hovered or focused, newest on top of the stack.
import { signal, type Signal } from '@preact/signals';

export const TOAST_TIMEOUT_MS = 6000;

export interface ToastAction {
  label: string;
  /** Runs, then the toast closes. */
  run: () => void;
  /** The quiet text-only action (Dismiss) rather than a ghost button. */
  quiet?: boolean;
}

export interface ToastInput {
  /** `accent` dot for warnings, `danger` for failures. */
  tone: 'warning' | 'danger';
  title: string;
  body: string;
  actions?: ToastAction[];
  /** Replaces an open toast with the same key instead of stacking a duplicate. */
  key?: string;
}

export interface Toast extends ToastInput {
  id: number;
  actions: ToastAction[];
}

export interface ToastTimers {
  setTimeout(handler: () => void, ms: number): unknown;
  clearTimeout(handle: unknown): void;
  now(): number;
}

export interface ToastQueue {
  /** Oldest first; the stack draws them bottom-up so the newest sits on top. */
  readonly items: Signal<Toast[]>;
  show(input: ToastInput): number;
  dismiss(id: number): void;
  /** Hover or focus inside the toast: the countdown stops. */
  pause(id: number): void;
  /** Pointer or focus left: the countdown continues with the time that was left. */
  resume(id: number): void;
  clear(): void;
}

const defaultTimers: ToastTimers = {
  setTimeout: (handler, ms) => globalThis.setTimeout(handler, ms),
  clearTimeout: (handle) => {
    globalThis.clearTimeout(handle as ReturnType<typeof globalThis.setTimeout>);
  },
  now: () => Date.now(),
};

interface Countdown {
  handle: unknown;
  remainingMs: number;
  startedAt: number;
  paused: boolean;
}

export function createToastQueue(timeoutMs = TOAST_TIMEOUT_MS, timers: ToastTimers = defaultTimers): ToastQueue {
  const items = signal<Toast[]>([]);
  const countdowns = new Map<number, Countdown>();
  let nextId = 1;

  const stop = (id: number): void => {
    const countdown = countdowns.get(id);
    if (countdown !== undefined && countdown.handle !== null) {
      timers.clearTimeout(countdown.handle);
      countdown.handle = null;
    }
  };

  const dismiss = (id: number): void => {
    stop(id);
    countdowns.delete(id);
    items.value = items.value.filter((t) => t.id !== id);
  };

  const start = (id: number, ms: number): void => {
    countdowns.set(id, {
      handle: timers.setTimeout(() => {
        dismiss(id);
      }, ms),
      remainingMs: ms,
      startedAt: timers.now(),
      paused: false,
    });
  };

  return {
    items,
    show(input) {
      if (input.key !== undefined) {
        const existing = items.value.find((t) => t.key === input.key);
        if (existing !== undefined) {
          dismiss(existing.id);
        }
      }
      const id = nextId++;
      items.value = [...items.value, { ...input, id, actions: input.actions ?? [] }];
      start(id, timeoutMs);
      return id;
    },
    dismiss,
    pause(id) {
      const countdown = countdowns.get(id);
      if (countdown === undefined || countdown.paused) {
        return;
      }
      stop(id);
      countdown.remainingMs = Math.max(0, countdown.remainingMs - (timers.now() - countdown.startedAt));
      countdown.paused = true;
    },
    resume(id) {
      const countdown = countdowns.get(id);
      if (!countdown?.paused) {
        return;
      }
      start(id, countdown.remainingMs);
    },
    clear() {
      for (const id of [...countdowns.keys()]) {
        stop(id);
      }
      countdowns.clear();
      items.value = [];
    },
  };
}
