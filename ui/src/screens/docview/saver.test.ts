import { afterEach, describe, expect, it, vi } from 'vitest';
import type { BridgeClient } from '../../bridge/client';
import { createEditSaver, SAVE_DEBOUNCE_MS, type SaveState } from './saver';

function fakeBridge(fail: string | null = null): { bridge: BridgeClient; calls: unknown[] } {
  const calls: unknown[] = [];
  let version = 2;
  const bridge = {
    isHosted: false,
    on: () => () => undefined,
    callWithFiles: () => Promise.reject(new Error('unused')),
    call: (method: string, params: unknown) => {
      calls.push([method, params]);
      return fail === null ? Promise.resolve({ document: {}, version: ++version }) : Promise.reject(new Error(fail));
    },
  } as unknown as BridgeClient;
  return { bridge, calls };
}

describe('the viewer saves edits as you type (documents.saveEdit, debounced)', () => {
  afterEach(() => {
    vi.useRealTimers();
  });

  it('saves once after typing pauses, with the paper as it is then', async () => {
    vi.useFakeTimers();
    const { bridge, calls } = fakeBridge();
    const states: SaveState['kind'][] = [];
    let html = '<article class="paper">a</article>';
    const saver = createEditSaver({ bridge, recordingId: 'r', documentId: 'd', read: () => html, onState: (s) => states.push(s.kind) });
    saver.changed();
    html = '<article class="paper">ab</article>';
    saver.changed();
    await vi.advanceTimersByTimeAsync(SAVE_DEBOUNCE_MS - 100);
    html = '<article class="paper">abc</article>';
    saver.changed();
    await vi.advanceTimersByTimeAsync(SAVE_DEBOUNCE_MS - 1);
    expect(calls).toHaveLength(0);
    await vi.advanceTimersByTimeAsync(2);
    expect(calls).toEqual([['documents.saveEdit', { recordingId: 'r', documentId: 'd', html: '<article class="paper">abc</article>' }]]);
    expect(states.at(-1)).toBe('saved');
    expect(states).toContain('saving');
    saver.dispose();
  });

  it('flushes what is pending at once (leaving the viewer) and reports a refusal in the host’s words', async () => {
    vi.useFakeTimers();
    const { bridge, calls } = fakeBridge('The last edit was not saved: a document cannot hold <video>.');
    const states: SaveState[] = [];
    const saver = createEditSaver({ bridge, recordingId: 'r', documentId: 'd', read: () => '<article class="paper"><video></video></article>', onState: (s) => states.push(s) });
    saver.changed();
    await saver.flush();
    expect(calls).toHaveLength(1);
    expect(states.at(-1)).toEqual({ kind: 'failed', message: 'The last edit was not saved: a document cannot hold <video>.' });
    // Nothing pending: flush sends nothing.
    await saver.flush();
    expect(calls).toHaveLength(1);
    saver.dispose();
  });
});
