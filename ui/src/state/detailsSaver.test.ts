import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { BridgeClient } from '../bridge/client';
import { createDetailsSaver, DETAILS_SAVE_DEBOUNCE_MS, emptyDetails, saveStatusText } from './detailsSaver';

function fakeBridge(fail = false): { bridge: BridgeClient; calls: unknown[] } {
  const calls: unknown[] = [];
  const bridge: BridgeClient = {
    isHosted: false,
    on: () => () => undefined,
    call: vi.fn((method: string, params: unknown) => {
      calls.push({ method, params });
      return fail ? Promise.reject(new Error('The project file is locked.')) : Promise.resolve({});
    }) as unknown as BridgeClient['call'],
    callWithFiles: vi.fn(() => Promise.resolve({})) as unknown as BridgeClient['callWithFiles'],
  };
  return { bridge, calls };
}

describe('details save debounce', () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });
  afterEach(() => {
    vi.useRealTimers();
  });

  it('applies edits at once and sends one merged update after the pause in typing', async () => {
    const { bridge, calls } = fakeBridge();
    const saver = createDetailsSaver(bridge, emptyDetails('Untitled meeting', 'meeting'));
    saver.adopt('rec-1', emptyDetails('Untitled meeting', 'meeting'));

    saver.update({ platform: 'Z' });
    saver.update({ platform: 'Zoom' });
    await vi.advanceTimersByTimeAsync(DETAILS_SAVE_DEBOUNCE_MS - 100);
    saver.update({ purpose: 'Lock the launch date' });
    expect(saver.details.value.platform).toBe('Zoom');
    expect(saver.status.value).toBe('pending');
    expect(calls).toHaveLength(0);

    await vi.advanceTimersByTimeAsync(DETAILS_SAVE_DEBOUNCE_MS);
    expect(calls).toEqual([
      {
        method: 'project.updateDetails',
        params: { recordingId: 'rec-1', details: { platform: 'Zoom', purpose: 'Lock the launch date' } },
      },
    ]);
    expect(saver.status.value).toBe('saved');
    expect(saveStatusText(saver.status.value, null)).toBe('Saved');
  });

  it('keeps edits local until the recording exists, then sends them without title and type', async () => {
    const { bridge, calls } = fakeBridge();
    const saver = createDetailsSaver(bridge, emptyDetails('Untitled meeting', 'meeting'));
    saver.update({ title: 'Q3 planning sync', participants: ['Sam Okafor'] });
    await vi.advanceTimersByTimeAsync(DETAILS_SAVE_DEBOUNCE_MS * 3);
    expect(calls).toHaveLength(0);
    expect(saver.status.value).toBe('local');

    await saver.attach('rec-2');
    expect(calls).toEqual([{ method: 'project.updateDetails', params: { recordingId: 'rec-2', details: { participants: ['Sam Okafor'] } } }]);
  });

  it('flushes a pending change immediately', async () => {
    const { bridge, calls } = fakeBridge();
    const saver = createDetailsSaver(bridge, emptyDetails('A', 'meeting'));
    saver.adopt('rec-3', emptyDetails('A', 'meeting'));
    saver.update({ tags: ['q3'] });
    await saver.flush();
    expect(calls).toHaveLength(1);
    await vi.advanceTimersByTimeAsync(DETAILS_SAVE_DEBOUNCE_MS * 2);
    expect(calls).toHaveLength(1);
  });

  it('reports a failed save and sends the edit again with the next change', async () => {
    const { bridge, calls } = fakeBridge(true);
    const saver = createDetailsSaver(bridge, emptyDetails('A', 'meeting'));
    saver.adopt('rec-4', emptyDetails('A', 'meeting'));
    saver.update({ platform: 'Teams' });
    await vi.advanceTimersByTimeAsync(DETAILS_SAVE_DEBOUNCE_MS);
    expect(saver.status.value).toBe('error');
    expect(saveStatusText('error', saver.error.value)).toBe('The project file is locked. Your edit is kept and will be sent again.');

    saver.update({ purpose: 'x' });
    await vi.advanceTimersByTimeAsync(DETAILS_SAVE_DEBOUNCE_MS);
    expect(calls.at(-1)).toEqual({ method: 'project.updateDetails', params: { recordingId: 'rec-4', details: { platform: 'Teams', purpose: 'x' } } });
  });
});
