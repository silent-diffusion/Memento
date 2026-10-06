import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { createToastQueue, TOAST_TIMEOUT_MS, type ToastTimers } from './toasts';

const fakeTimers = (): ToastTimers => ({
  setTimeout: (handler, ms) => setTimeout(handler, ms),
  clearTimeout: (handle) => {
    clearTimeout(handle as ReturnType<typeof setTimeout>);
  },
  now: () => Date.now(),
});

describe('toast queue timing', () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  const toast = { tone: 'warning' as const, title: 'Zoom stopped at 00:41:12', body: 'Microphone is still recording.' };

  it('closes a toast after 6 s', () => {
    const queue = createToastQueue(TOAST_TIMEOUT_MS, fakeTimers());
    queue.show(toast);
    vi.advanceTimersByTime(5999);
    expect(queue.items.value).toHaveLength(1);
    vi.advanceTimersByTime(1);
    expect(queue.items.value).toHaveLength(0);
  });

  it('pauses while hovered and continues with the time that was left', () => {
    const queue = createToastQueue(TOAST_TIMEOUT_MS, fakeTimers());
    const id = queue.show(toast);
    vi.advanceTimersByTime(4000);
    queue.pause(id);
    vi.advanceTimersByTime(60_000);
    expect(queue.items.value).toHaveLength(1);
    queue.resume(id);
    vi.advanceTimersByTime(1999);
    expect(queue.items.value).toHaveLength(1);
    vi.advanceTimersByTime(1);
    expect(queue.items.value).toHaveLength(0);
  });

  it('ignores a second pause or a resume without a pause', () => {
    const queue = createToastQueue(TOAST_TIMEOUT_MS, fakeTimers());
    const id = queue.show(toast);
    queue.resume(id);
    vi.advanceTimersByTime(3000);
    queue.pause(id);
    vi.advanceTimersByTime(1000);
    queue.pause(id);
    queue.resume(id);
    vi.advanceTimersByTime(2999);
    expect(queue.items.value).toHaveLength(1);
    vi.advanceTimersByTime(1);
    expect(queue.items.value).toHaveLength(0);
  });

  it('stacks in order of arrival, each with its own timer', () => {
    const queue = createToastQueue(TOAST_TIMEOUT_MS, fakeTimers());
    queue.show({ ...toast, title: 'first' });
    vi.advanceTimersByTime(2000);
    queue.show({ ...toast, title: 'second' });
    expect(queue.items.value.map((t) => t.title)).toEqual(['first', 'second']);
    vi.advanceTimersByTime(4000);
    expect(queue.items.value.map((t) => t.title)).toEqual(['second']);
    vi.advanceTimersByTime(2000);
    expect(queue.items.value).toEqual([]);
  });

  it('replaces a toast with the same key instead of stacking a duplicate', () => {
    const queue = createToastQueue(TOAST_TIMEOUT_MS, fakeTimers());
    queue.show({ ...toast, key: 'lost:zoom', title: 'old' });
    queue.show({ ...toast, key: 'lost:zoom', title: 'new' });
    expect(queue.items.value.map((t) => t.title)).toEqual(['new']);
    vi.advanceTimersByTime(TOAST_TIMEOUT_MS);
    expect(queue.items.value).toEqual([]);
  });

  it('dismisses at once and clears its timer', () => {
    const queue = createToastQueue(TOAST_TIMEOUT_MS, fakeTimers());
    const id = queue.show(toast);
    queue.dismiss(id);
    expect(queue.items.value).toEqual([]);
    expect(vi.getTimerCount()).toBe(0);
  });
});
