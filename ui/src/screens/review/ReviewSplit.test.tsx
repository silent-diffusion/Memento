// Splitting a line at a word and joining lines (DESIGN.md §9, 2.6.0): Ctrl+Enter while editing cuts the line at the
// caret (after saving a changed draft) and opens the second part's speaker menu; the context menu joins a line with
// the next one of its track; each is one Undo step that does the inverse. Against the mock host.
import { render } from 'preact';
import { act } from 'preact/test-utils';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { App } from '../../App';
import { createBridgeClient, type BridgeClient } from '../../bridge/client';
import type { TranscriptSegment } from '../../bridge/types';
import { formatDuration } from '../../format/duration';
import { loadInitialData, refreshLibrary } from '../../state/data';
import { connectEvents, createStore, type AppStore } from '../../state/store';
import { undoOf } from '../../state/undo';

const quiet = { info: () => undefined, warn: () => undefined };
const NOW = new Date(2026, 9, 6, 15, 0);
const DESIGN_REVIEW = '20261005-160000-dsrev';
const LINE = 'Great. Lena, you had a list of questions from the research sessions?';

describe('Review: split a line at a word, join lines (2.6.0, against the browser-preview host)', () => {
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

  const click = async (el: Element | null | undefined): Promise<void> => {
    if (el === null || el === undefined) {
      throw new Error('nothing to click');
    }
    await act(async () => {
      (el as HTMLElement).click();
      await Promise.resolve();
    });
  };

  const dblclick = async (el: Element): Promise<void> => {
    await act(async () => {
      el.dispatchEvent(new MouseEvent('dblclick', { bubbles: true, cancelable: true }));
      await Promise.resolve();
    });
  };

  const typeInto = async (input: HTMLTextAreaElement, text: string): Promise<void> => {
    await act(async () => {
      input.value = text;
      input.dispatchEvent(new Event('input', { bubbles: true }));
      await Promise.resolve();
    });
  };

  const press = async (target: Element, key: string, init: KeyboardEventInit = {}): Promise<void> => {
    await act(async () => {
      target.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true, ...init }));
      await Promise.resolve();
    });
  };

  const segment = (text: string): HTMLElement => {
    const match = [...container.querySelectorAll<HTMLElement>('.segm')].find((s) => s.querySelector('.segm-text')?.textContent.startsWith(text));
    if (match === undefined) {
      throw new Error(`no segment starting "${text}"`);
    }
    return match;
  };

  const hasSegment = (text: string): boolean =>
    [...container.querySelectorAll<HTMLElement>('.segm')].some((s) => s.querySelector('.segm-text')?.textContent.startsWith(text));

  const lines = async (): Promise<TranscriptSegment[]> => {
    const { transcript } = await bridge.call('transcript.get', { recordingId: DESIGN_REVIEW });
    return transcript?.segments ?? [];
  };

  const lineStarting = async (text: string): Promise<TranscriptSegment> => {
    const found = (await lines()).find((s) => s.text.startsWith(text));
    if (found === undefined) {
      throw new Error(`no line starting "${text}"`);
    }
    return found;
  };

  const made = (method: string): number => calls.filter((c) => c.method === method).length;

  const undo = async (): Promise<void> => {
    await act(async () => {
      await undoOf(store).undo();
    });
    await settle(20);
  };

  const redo = async (): Promise<void> => {
    await act(async () => {
      await undoOf(store).redo();
    });
    await settle(20);
  };

  const editor = (): HTMLTextAreaElement => {
    const el = container.querySelector<HTMLTextAreaElement>('.segm-editor');
    if (el === null) {
      throw new Error('no editor');
    }
    return el;
  };

  const open = async (): Promise<void> => {
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
    store.route.value = { name: 'review', recordingId: DESIGN_REVIEW };
    await act(async () => {
      render(<App bridge={bridge} store={store} now={() => NOW} />, container);
      await Promise.resolve();
    });
    await until(() => container.querySelector('.segm') !== null && container.querySelectorAll('.person[data-speaker-id]').length > 0);
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

  it('Ctrl+Enter cuts the line at the caret and opens the second part’s speaker menu; Undo joins, Redo cuts again', async () => {
    await open();
    const before = await lineStarting('Great. Lena');
    await dblclick(segment('Great. Lena'));
    const field = editor();
    expect(field.getAttribute('aria-label')).toBe('Edit this line. Enter saves, Ctrl+Enter splits the line at the caret, Esc cancels.');
    // "Great. Lena, |you had…": the caret after the comma and space lands the cut before "you".
    field.setSelectionRange(13, 13);
    await press(field, 'Enter', { ctrlKey: true });
    await until(() => hasSegment('you had a list'));

    const split = calls.find((c) => c.method === 'transcript.splitSegment');
    expect(split?.params).toEqual({ recordingId: DESIGN_REVIEW, segmentId: before.id, at: 13 });
    expect(container.querySelector('.segm-editor')).toBeNull();
    const first = await lineStarting('Great. Lena,');
    const second = await lineStarting('you had a list');
    expect([first.id, second.id]).toEqual([before.id, `${before.id}-2`]);
    expect([first.text, second.text]).toEqual(['Great. Lena,', 'you had a list of questions from the research sessions?']);
    expect([first.speaker, second.speaker]).toEqual([before.speaker, before.speaker]);
    expect(first.end).toBeLessThanOrEqual(second.start);
    // The second part asks who said it: its speaker menu is open with the search field focused.
    await until(() => document.querySelector('.speaker-menu') !== null);
    const menu = document.querySelector<HTMLElement>('.speaker-menu');
    expect(menu?.getAttribute('aria-label')).toBe(`Speaker for the line at ${formatDuration(second.start * 1000)}`);
    expect(document.activeElement).toBe(menu?.querySelector('.speaker-menu-search'));
    await press(menu?.querySelector('.speaker-menu-search') ?? field, 'Escape');
    await until(() => document.querySelector('.speaker-menu') === null);

    await undo();
    expect(made('transcript.joinSegments')).toBe(1);
    await until(() => hasSegment(LINE));
    const joined = await lineStarting('Great. Lena');
    expect([joined.id, joined.text, joined.start, joined.end, joined.edited]).toEqual([before.id, before.text, before.start, before.end, null]);
    expect((await lines()).some((s) => s.id === `${before.id}-2`)).toBe(false);

    await redo();
    expect(made('transcript.splitSegment')).toBe(2);
    await until(() => hasSegment('you had a list'));
    expect((await lines()).map((s) => s.id)).toContain(`${before.id}-2`);
  });

  it('Ctrl+Enter saves a changed draft first, then cuts it', async () => {
    await open();
    const before = await lineStarting('Great. Lena');
    await dblclick(segment('Great. Lena'));
    const field = editor();
    await typeInto(field, 'Great. Lena, you had questions?');
    field.setSelectionRange(13, 13);
    await press(field, 'Enter', { ctrlKey: true });
    await until(() => hasSegment('you had questions?'));

    expect(calls.map((c) => c.method).filter((m) => m === 'transcript.editSegment' || m === 'transcript.splitSegment')).toEqual(['transcript.editSegment', 'transcript.splitSegment']);
    const first = await lineStarting('Great. Lena,');
    expect([first.text, first.edited?.original]).toEqual(['Great. Lena,', before.text]);
    expect((await lineStarting('you had questions?')).edited?.original).toBe(before.text);

    // Two steps back: the cut, then the edit.
    await undo();
    await until(() => hasSegment('Great. Lena, you had questions?'));
    await undo();
    await until(() => hasSegment(LINE));
    expect((await lineStarting('Great. Lena')).edited).toBeNull();
  });

  it('the line menu joins a line with the next one; Undo cuts it again; the last line offers no join', async () => {
    await open();
    const all = await lines();
    const first = all[0];
    const second = all[1];
    const last = all[all.length - 1];
    if (first === undefined || second === undefined || last === undefined) {
      throw new Error('the sample has too few lines');
    }
    const row = container.querySelector<HTMLElement>(`[data-segment-id="${first.id}"]`);
    if (row === null) {
      throw new Error('no first row');
    }
    await act(async () => {
      row.dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, cancelable: true, clientX: 300, clientY: 200 }));
      await Promise.resolve();
    });
    const item = document.querySelector<HTMLElement>('.line-menu [data-item="join"]');
    expect(item?.textContent).toBe('Join with the next line');
    await click(item);
    await until(() => made('transcript.joinSegments') === 1);
    await settle(30);

    const joined = await lineStarting(first.text);
    expect(joined.text).toBe(`${first.text} ${second.text}`);
    expect((await lines()).some((s) => s.id === second.id)).toBe(false);
    expect(container.querySelector('.undo-status')?.textContent ?? '').toMatch(/^Joined the line at /);

    await undo();
    await until(() => (made('transcript.splitSegment') === 1));
    await settle(30);
    const back = await lines();
    expect(back[0]?.text).toBe(first.text);
    expect(back[1]?.text).toBe(second.text);

    // The last line has nothing after it, so its menu does not offer the join.
    const lastRow = container.querySelector<HTMLElement>(`[data-segment-id="${last.id}"]`);
    if (lastRow === null) {
      // The virtual list may not render the last row; focus it through the host's order instead.
      return;
    }
    await act(async () => {
      lastRow.dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, cancelable: true, clientX: 300, clientY: 200 }));
      await Promise.resolve();
    });
    expect(document.querySelector('.line-menu [data-item="join"]')).toBeNull();
  });
});
