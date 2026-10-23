// Keyboard correction of speakers (DESIGN.md §9, 2.6.0): with a transcript line focused, 1–9 move it to that
// person in the People order (the rows show their digits), U / Shift+U go to the next or previous line whose
// speaker is uncertain and play it, and the hint says when there is none that way. Against the mock host.
import { render } from 'preact';
import { act } from 'preact/test-utils';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { App } from '../../App';
import { createBridgeClient, type BridgeClient } from '../../bridge/client';
import { formatDuration } from '../../format/duration';
import { loadInitialData, refreshLibrary } from '../../state/data';
import { connectEvents, createStore, type AppStore } from '../../state/store';

const quiet = { info: () => undefined, warn: () => undefined };
const NOW = new Date(2026, 9, 6, 15, 0);
const DESIGN_REVIEW = '20261005-160000-dsrev';
const KEYS_HINT = 'Click the time to play a line · click the words to correct them · U for the next uncertain line · 1–9 move it to a speaker';

describe('Review keyboard correction (2.6.0, against the browser-preview host)', () => {
  let container: HTMLDivElement;
  let store: AppStore;
  let bridge: BridgeClient;
  let disconnect: () => void;
  /** How often playback was started (jsdom cannot play; the element's play is stubbed). */
  let played = 0;

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

  const click = async (el: Element): Promise<void> => {
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

  const focusedText = (): string => document.activeElement?.querySelector('.segm-text')?.textContent ?? '';

  /** The People rows' speaker ids, in the order shown (named people first, then "Speaker n"). */
  const people = (): string[] => [...container.querySelectorAll<HTMLElement>('.person[data-speaker-id]')].map((p) => p.dataset.speakerId ?? '');

  const nameOf = (speakerId: string): string =>
    container.querySelector(`.person[data-speaker-id="${speakerId}"] .person-filter-name, .person[data-speaker-id="${speakerId}"] .person-name`)?.textContent ?? '';

  const line = async (text: string): Promise<{ speaker: string | null; start: number }> => {
    const { transcript } = await bridge.call('transcript.get', { recordingId: DESIGN_REVIEW });
    const found = transcript?.segments.find((s) => s.text.startsWith(text));
    if (found === undefined) {
      throw new Error(`no line starting "${text}"`);
    }
    return { speaker: found.speaker, start: found.start };
  };

  const version = async (): Promise<number> => (await bridge.call('transcript.get', { recordingId: DESIGN_REVIEW })).transcript?.version ?? -1;

  const open = async (): Promise<void> => {
    bridge = createBridgeClient({ logger: quiet, mock: { live: false, recovery: false, now: () => NOW.getTime(), stepMs: 30 } });
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
    played = 0;
    vi.spyOn(HTMLMediaElement.prototype, 'play').mockImplementation(function play(this: HTMLMediaElement) {
      played += 1;
      return Promise.resolve();
    });
    vi.spyOn(HTMLMediaElement.prototype, 'pause').mockImplementation(() => undefined);
  });

  afterEach(() => {
    render(null, container);
    disconnect();
    container.remove();
    document.body.innerHTML = '';
    vi.restoreAllMocks();
  });

  it('shows each of the first nine People rows its digit, hidden from assistive technology', async () => {
    await open();
    const caps = [...container.querySelectorAll<HTMLElement>('.person .person-key')];
    const count = Math.min(9, people().length);
    expect(count).toBeGreaterThan(1);
    expect(caps.map((c) => c.textContent)).toEqual(Array.from({ length: count }, (_, i) => String(i + 1)));
    expect(caps.every((c) => c.getAttribute('aria-hidden') === 'true')).toBe(true);
    expect(caps[2]?.getAttribute('title')).toBe(`Press 3 on a transcript line to move it to ${nameOf(people()[2] ?? '')}`);
    expect(container.querySelector('.transcript-hint')?.textContent).toBe(KEYS_HINT);
  });

  it('a digit moves the focused line to that person in the People order, one Undo step, and keeps focus', async () => {
    await open();
    const ids = people();
    const before = (await line('Great. Lena')).speaker;
    const target = ids.findIndex((id) => id !== before);
    const row = segment('Great. Lena');
    row.focus();
    await press(row, String(target + 1));
    await until(() => segment('Great. Lena').querySelector('.segm-speaker-name')?.textContent === nameOf(ids[target] ?? ''));
    expect((await line('Great. Lena')).speaker).toBe(ids[target]);
    expect(document.activeElement).toBe(segment('Great. Lena'));

    const undo = [...document.querySelectorAll('button')].find((b) => b.getAttribute('aria-label') === `Undo move line to ${nameOf(ids[target] ?? '')}`);
    if (undo === undefined) {
      throw new Error('no Undo button for the move');
    }
    await click(undo);
    await until(() => segment('Great. Lena').querySelector('.segm-speaker-name')?.textContent === nameOf(before ?? ''));
    expect((await line('Great. Lena')).speaker).toBe(before);

    // The line's own speaker, and a digit past the list, change nothing.
    const unchanged = await version();
    await press(segment('Great. Lena'), String(ids.indexOf(before ?? '') + 1));
    expect(ids.length).toBeLessThan(9);
    await press(segment('Great. Lena'), '9');
    await settle();
    expect(await version()).toBe(unchanged);
  });

  it('digits typed in the line editor, the search field or a speaker menu change nothing', async () => {
    await open();
    const ids = people();
    const before = (await line('Great. Lena')).speaker;
    const other = String(ids.findIndex((id) => id !== before) + 1);
    const unchanged = await version();

    await dblclick(segment('Great. Lena'));
    const editor = container.querySelector<HTMLTextAreaElement>('.segm-editor');
    if (editor === null) {
      throw new Error('no editor');
    }
    await press(editor, other);
    expect(container.querySelector('.segm-editor')).not.toBeNull();
    await press(editor, 'Escape');

    const search = container.querySelector<HTMLInputElement>('#tx-search');
    if (search === null) {
      throw new Error('no search field');
    }
    search.focus();
    await press(search, other);

    const speakerButton = segment('Great. Lena').querySelector<HTMLButtonElement>('.segm-speaker');
    if (speakerButton === null) {
      throw new Error('no speaker button');
    }
    await click(speakerButton);
    const field = document.querySelector<HTMLInputElement>('.speaker-menu-search');
    if (field === null) {
      throw new Error('no speaker menu');
    }
    await press(field, other);
    await press(field, 'Escape');
    await settle();
    expect(await version()).toBe(unchanged);
    expect((await line('Great. Lena')).speaker).toBe(before);
  });

  it('U goes to the next uncertain line and plays it, Shift+U to the previous, and the hint says when there is none', async () => {
    await open();
    const first = segment('Okay, I think everyone');
    first.focus();
    await press(first, 'u');
    await settle();
    expect(focusedText()).toMatch(/^Mm-hm\./);
    const target = await line('Mm-hm.');
    expect(container.querySelector('.scrubber')?.getAttribute('aria-valuetext')).toBe(`${formatDuration(target.start * 1000)} of 1:10:02`);
    expect(played).toBe(1);

    // Nothing uncertain before the first one: the hint says so and focus stays.
    const mm = segment('Mm-hm.');
    await press(mm, 'U', { shiftKey: true });
    expect(container.querySelector('.transcript-hint')?.textContent).toBe('No uncertain lines before this one');
    expect(container.querySelector('.transcript-hint')?.getAttribute('role')).toBe('status');
    expect(document.activeElement).toBe(mm);

    // Forward through every uncertain line to the end, then the hint says so there too.
    let visited = 0;
    let last = focusedText();
    while (container.querySelector('.transcript-hint')?.textContent !== 'No uncertain lines after this one') {
      const focused = document.activeElement;
      if (!(focused instanceof HTMLElement) || !focused.classList.contains('segm')) {
        throw new Error('focus left the transcript');
      }
      await press(focused, 'u');
      await settle();
      visited += 1;
      if (visited > 40) {
        throw new Error('the end was never reached');
      }
      if (container.querySelector('.transcript-hint')?.textContent !== 'No uncertain lines after this one') {
        expect(focusedText()).not.toBe(last);
        last = focusedText();
      }
    }
    expect(visited).toBeGreaterThan(2);
    expect(focusedText()).toBe(last);

    // Shift+U from the last one goes back to the one before it.
    const end = document.activeElement;
    if (!(end instanceof HTMLElement)) {
      throw new Error('nothing focused');
    }
    await press(end, 'U', { shiftKey: true });
    await settle();
    expect(focusedText()).not.toBe(last);
    expect(segment(focusedText().slice(0, 12)).querySelector('.segm-dot--uncertain')).not.toBeNull();
  });

  it('with a speaker filter on, U only visits the lines shown', async () => {
    await open();
    const sam = [...container.querySelectorAll<HTMLButtonElement>('.person-filter')].find((b) => b.querySelector('.person-filter-name')?.textContent === 'Sam Okafor');
    if (sam === undefined) {
      throw new Error('no Sam Okafor in People');
    }
    await click(sam);
    await until(() => (container.querySelector('.segm-list')?.getAttribute('aria-label') ?? '').includes('of 130 lines shown'));
    const first = container.querySelector<HTMLElement>('.segm');
    if (first === null) {
      throw new Error('no line shown');
    }
    first.focus();
    await press(first, 'u');
    await settle();
    expect(focusedText()).toMatch(/^Okay\./);
    expect(document.activeElement?.querySelector('.segm-speaker-name')?.textContent).toBe('Sam Okafor');
  });
});
