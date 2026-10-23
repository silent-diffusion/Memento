// The turn boundary (DESIGN.md §9, 2.6.0): between two consecutive lines of different speakers a pill offers to give
// the line above to the next speaker or the line below to the previous one; never inside a turn, never while
// selecting, never without speakers. Each press is the single-line move, one Undo step. Against the mock host.
import { render } from 'preact';
import { act } from 'preact/test-utils';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { App } from '../../App';
import { createBridgeClient, type BridgeClient } from '../../bridge/client';
import type { TranscriptSegment } from '../../bridge/types';
import { loadInitialData, refreshLibrary } from '../../state/data';
import { connectEvents, createStore, type AppStore } from '../../state/store';
import { undoOf } from '../../state/undo';

const quiet = { info: () => undefined, warn: () => undefined };
const NOW = new Date(2026, 9, 6, 15, 0);
const DESIGN_REVIEW = '20261005-160000-dsrev';
const README = '20261005-091200-readme';

describe('Review turn boundary (2.6.0, against the browser-preview host)', () => {
  let container: HTMLDivElement;
  let store: AppStore;
  let bridge: BridgeClient;
  let disconnect: () => void;
  let calls: { method: string; params: unknown }[];

  const settle = async (ms = 0): Promise<void> => {
    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, ms));
    });
  };

  const until = async (check: () => boolean, timeoutMs = 8000): Promise<void> => {
    const started = Date.now();
    while (!check()) {
      if (Date.now() - started > timeoutMs) {
        throw new Error('timed out waiting for the screen');
      }
      await settle(20);
    }
  };

  const click = async (el: Element | null | undefined, init: MouseEventInit = {}): Promise<void> => {
    if (el === null || el === undefined) {
      throw new Error('nothing to click');
    }
    await act(async () => {
      el.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true, ...init }));
      await Promise.resolve();
    });
  };

  const lines = async (recordingId = DESIGN_REVIEW): Promise<TranscriptSegment[]> => {
    const { transcript } = await bridge.call('transcript.get', { recordingId });
    return transcript?.segments ?? [];
  };

  const made = (method: string): { method: string; params: unknown }[] => calls.filter((c) => c.method === method);

  const undo = async (): Promise<void> => {
    await act(async () => {
      await undoOf(store).undo();
    });
    await settle(20);
  };

  const boundary = (above: TranscriptSegment, below: TranscriptSegment): HTMLElement | null =>
    container.querySelector<HTMLElement>(`[data-boundary="${above.id}|${below.id}"]`);

  /** The first two consecutive rendered lines whose speakers differ, and the first two that share one. */
  const pairs = async (): Promise<{ change: [TranscriptSegment, TranscriptSegment]; turn: [TranscriptSegment, TranscriptSegment] }> => {
    const all = await lines();
    const rendered = new Set([...container.querySelectorAll<HTMLElement>('[data-segment-id]')].map((el) => el.dataset.segmentId));
    let change: [TranscriptSegment, TranscriptSegment] | null = null;
    let turn: [TranscriptSegment, TranscriptSegment] | null = null;
    for (let i = 0; i < all.length - 1; i++) {
      const a = all[i];
      const b = all[i + 1];
      if (a === undefined || b === undefined || !rendered.has(a.id) || !rendered.has(b.id) || a.speaker === null || b.speaker === null) {
        continue;
      }
      if (a.speaker !== b.speaker) {
        change ??= [a, b];
      } else {
        turn ??= [a, b];
      }
    }
    if (change === null || turn === null) {
      throw new Error('the sample has no rendered speaker change or turn');
    }
    return { change, turn };
  };

  const open = async (recordingId = DESIGN_REVIEW): Promise<void> => {
    bridge = createBridgeClient({ logger: quiet, mock: { live: false, recovery: false, now: () => NOW.getTime(), stepMs: 30 } });
    calls = [];
    const original = bridge.call.bind(bridge);
    vi.spyOn(bridge, 'call').mockImplementation(((method: string, params: unknown) => {
      calls.push({ method, params });
      return (original as (m: string, p: unknown) => Promise<unknown>)(method, params);
    }) as typeof bridge.call);
    store = createStore(false);
    disconnect = connectEvents(bridge, store, {
      onLibraryChanged: () => {
        void refreshLibrary(bridge, store);
      },
    });
    await act(async () => {
      await loadInitialData(bridge, store);
    });
    store.route.value = { name: 'review', recordingId };
    await act(async () => {
      render(<App bridge={bridge} store={store} now={() => NOW} />, container);
      await Promise.resolve();
    });
    await until(() => container.querySelector('.segm') !== null);
  };

  beforeEach(() => {
    container = document.createElement('div');
    document.body.append(container);
  });

  afterEach(() => {
    render(null, container);
    disconnect();
    container.remove();
    vi.restoreAllMocks();
  });

  it('shows between two speakers’ lines and not inside a turn; ▲ and ▼ move the boundary a line, each undoable', async () => {
    await open();
    const { change, turn } = await pairs();
    const [above, below] = change;
    const speakerName = (id: string | null): string => container.querySelector(`.person[data-speaker-id="${id ?? ''}"] .person-name, .person[data-speaker-id="${id ?? ''}"] .person-filter-name`)?.textContent ?? '';
    const control = boundary(above, below);
    expect(control).not.toBeNull();
    expect(control?.getAttribute('role')).toBe('group');
    expect(boundary(turn[0], turn[1])).toBeNull();
    const up = control?.querySelector<HTMLButtonElement>('button[aria-label^="Give the line above to "]');
    const down = control?.querySelector<HTMLButtonElement>('button[aria-label^="Give the line below to "]');
    expect(up?.getAttribute('aria-label')).toBe(`Give the line above to ${speakerName(below.speaker)}`);
    expect(down?.getAttribute('aria-label')).toBe(`Give the line below to ${speakerName(above.speaker)}`);

    // ▲: the line above takes the speaker of the line below; the boundary is now one line up.
    await click(up);
    await until(() => made('transcript.setSegmentSpeaker').length === 1);
    await settle(30);
    expect(made('transcript.setSegmentSpeaker')[0]?.params).toEqual({ recordingId: DESIGN_REVIEW, segmentId: above.id, speakerId: below.speaker });
    expect((await lines()).find((s) => s.id === above.id)?.speaker).toBe(below.speaker);
    expect(boundary(above, below)).toBeNull();

    await undo();
    await until(() => boundary(above, below) !== null);
    expect((await lines()).find((s) => s.id === above.id)?.speaker).toBe(above.speaker);

    // ▼: the line below takes the speaker of the line above.
    await click(boundary(above, below)?.querySelector('button[aria-label^="Give the line below to "]'));
    await until(() => made('transcript.setSegmentSpeaker').length === 3);
    await settle(30);
    expect(made('transcript.setSegmentSpeaker')[2]?.params).toEqual({ recordingId: DESIGN_REVIEW, segmentId: below.id, speakerId: above.speaker });
    expect((await lines()).find((s) => s.id === below.id)?.speaker).toBe(above.speaker);
    expect(boundary(above, below)).toBeNull();

    await undo();
    await until(() => boundary(above, below) !== null);
  });

  it('hides while lines are being selected', async () => {
    await open();
    const { change } = await pairs();
    const [above, below] = change;
    expect(boundary(above, below)).not.toBeNull();

    await click(container.querySelector(`[data-segment-id="${above.id}"] .segm-text`), { ctrlKey: true });
    await until(() => container.querySelector('.sel-bar') !== null);
    expect(container.querySelector('.turn-boundary')).toBeNull();

    await act(async () => {
      document.body.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }));
      await Promise.resolve();
    });
    await until(() => container.querySelector('.sel-bar') === null);
    expect(boundary(above, below)).not.toBeNull();
  });

  it('is absent when the lines have no speakers', async () => {
    await open(README);
    const all = await lines(README);
    expect(all.length).toBeGreaterThan(1);
    expect(all.every((s) => s.speaker === null)).toBe(true);
    expect(container.querySelector('.turn-boundary')).toBeNull();
  });
});
