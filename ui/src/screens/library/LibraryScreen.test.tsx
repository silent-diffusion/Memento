import { render } from 'preact';
import { act } from 'preact/test-utils';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { App } from '../../App';
import { createBridgeClient, type BridgeClient } from '../../bridge/client';
import type { MockOptions } from '../../bridge/mock';
import { loadInitialData, refreshLibrary } from '../../state/data';
import { connectEvents, createStore, type AppStore } from '../../state/store';

// Tuesday 6 October 2026, 3 PM: the sample library fills every bucket.
const NOW = new Date(2026, 9, 6, 15, 0);
const quiet = { info: () => undefined, warn: () => undefined };

describe('Library, populated (against the browser-preview host)', () => {
  let container: HTMLDivElement;
  let store: AppStore;
  let bridge: BridgeClient;

  const start = async (options: MockOptions = {}): Promise<void> => {
    bridge = createBridgeClient({ logger: quiet, mock: { live: false, recovery: false, now: () => NOW.getTime(), ...options } });
    store = createStore(false);
    connectEvents(bridge, store, {
      onLibraryChanged: () => {
        void refreshLibrary(bridge, store);
      },
    });
    await act(async () => {
      render(<App bridge={bridge} store={store} now={() => NOW} />, container);
      await loadInitialData(bridge, store);
    });
  };

  const text = (selector: string): string[] => [...container.querySelectorAll(selector)].map((el) => el.textContent.trim());
  const settle = async (ms = 0): Promise<void> => {
    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, ms));
    });
  };
  /** Waits for the screen instead of a fixed delay, so a busy test run does not fail on timing. */
  const until = async (check: () => boolean, timeoutMs = 8000): Promise<void> => {
    const started = Date.now();
    while (!check()) {
      if (Date.now() - started > timeoutMs) {
        throw new Error('timed out waiting for the screen');
      }
      await settle(20);
    }
  };

  beforeEach(() => {
    container = document.createElement('div');
    document.body.append(container);
  });

  afterEach(() => {
    render(null, container);
    container.remove();
    document.body.innerHTML = '';
  });

  it('shows the heading, summary, processing card, chips and date groups', async () => {
    await start();
    expect(container.querySelector('h1')?.textContent).toBe('Library');
    expect(text('.lib-summary')).toEqual(['14 recordings · 10 h 38 min · all on this PC']);
    expect(text('.lib-group-label')).toEqual(['Today', 'Yesterday', 'Earlier this week', 'September', 'August']);
    expect(text('.proc-title')).toEqual(['Q3 planning sync']);
    expect(text('.proc-stage-name')).toEqual(['Stored', 'Transcribing', 'Speakers']);
    expect(text('.proc-meta')).toEqual(['Meeting · 5 people · recorded today at 10:00 AM · 1:02:14']);
    expect(text('.chip')).toEqual(['All', 'Meetings', 'Interviews', 'Lectures', 'Presentations', 'Dictation', 'Research', 'Book notes']);
    expect(container.querySelector('.chip.on')?.getAttribute('aria-pressed')).toBe('true');
  });

  it('renders rows per DESIGN.md §4', async () => {
    await start();
    const row = [...container.querySelectorAll<HTMLElement>('.lib-row')].find((r) => r.textContent.includes('Design review'));
    expect(row?.querySelector('.row-meta')?.textContent).toContain('Meeting · 4 people · 4:00 PM');
    expect(row?.querySelector('[aria-label="Includes video"]')).not.toBeNull();
    expect(row?.querySelector('.row-duration')?.textContent).toBe('1:10:02');
    expect([...(row?.querySelectorAll('.pill.done') ?? [])].map((p) => p.textContent)).toEqual(['Transcript', 'Speakers', 'Minutes']);

    const audioOnly = [...container.querySelectorAll<HTMLElement>('.lib-row')].find((r) => r.textContent.includes('Weekly 1:1'));
    expect(audioOnly?.querySelector('.audio-only')?.textContent).toBe('Audio only');
    expect(audioOnly?.querySelector('.row-meta')?.textContent).toBe('Meeting · 2 people · Fri, 3:30 PM');

    const failed = [...container.querySelectorAll<HTMLElement>('.lib-row')].find((r) => r.textContent.includes('Customer call'));
    expect(failed?.querySelector('.pill.failed')?.textContent).toBe('Transcript failed · Retry');
  });

  it('filters with the chips (arrow keys move, Enter chooses) and hides the processing card', async () => {
    await start();
    const all = container.querySelector<HTMLButtonElement>('.chip.on');
    all?.focus();
    all?.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true }));
    all?.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true }));
    expect(document.activeElement?.textContent).toBe('Interviews');
    expect(store.libraryView.value.type).toBe('all');

    await act(() => {
      (document.activeElement as HTMLButtonElement).click();
    });
    await settle(10);
    expect(store.libraryView.value.type).toBe('interview');
    expect(container.querySelector('.proc-card')).toBeNull();
    expect(text('.lib-summary')[0]).toMatch(/^3 recordings · /);
    expect(text('.row-meta').every((m) => m.startsWith('Interview'))).toBe(true);
  });

  it('searches titles and people, then shows the no-match state', async () => {
    await start();
    const search = container.querySelector<HTMLInputElement>('#lib-search');
    if (search === null) {
      throw new Error('no search');
    }
    await act(() => {
      search.value = 'town';
      search.dispatchEvent(new Event('input', { bubbles: true }));
    });
    await until(() => text('.row-title').length === 1);
    expect(text('.row-title')).toEqual(['Town hall Q&A']);
    expect(text('.lib-summary')[0]).toBe('1 recording · 1 h 46 min · all on this PC');

    await act(() => {
      search.value = 'nothing like this';
      search.dispatchEvent(new Event('input', { bubbles: true }));
    });
    await until(() => text('.no-match').length === 1);
    expect(text('.no-match')).toEqual(['No recordings match. Try another type or clear the search.']);
  });

  it('switches to the grid with a waveform strip and no processing card', async () => {
    await start();
    await act(() => {
      container.querySelector<HTMLButtonElement>('[aria-label="Grid view"]')?.click();
    });
    expect(container.querySelector('[aria-label="Grid view"]')?.getAttribute('aria-pressed')).toBe('true');
    expect(container.querySelectorAll('.lib-card')).toHaveLength(14);
    expect(container.querySelector('.proc-card')).toBeNull();
    expect(container.querySelectorAll('.lib-card .wb').length).toBe(14 * 48);
  });

  it('sorts from the sort menu', async () => {
    await start();
    await act(() => {
      container.querySelector<HTMLButtonElement>('.sort-btn')?.click();
    });
    const option = [...document.querySelectorAll<HTMLElement>('[role="option"]')].find((o) => o.textContent.trim() === 'Longest');
    await act(() => {
      option?.click();
    });
    await settle(10);
    expect(container.querySelector('.sort-btn')?.textContent).toContain('Longest first');
    expect(text('.lib-group-label')).toEqual(['Longest first']);
    expect(text('.row-title')[0]).toBe('Town hall Q&A');
  });

  it('deletes through the confirmation with the host estimate', async () => {
    await start();
    const more = container.querySelector<HTMLButtonElement>('[aria-label="More actions for Weekly 1:1 with Sam"]');
    await act(() => {
      more?.click();
    });
    const del = [...document.querySelectorAll<HTMLButtonElement>('[role="menuitem"]')].find((b) => b.textContent === 'Delete');
    await act(() => {
      del?.click();
    });
    await until(() => document.querySelector('.dialog-title') !== null);
    expect(document.querySelector('.dialog-title')?.textContent).toBe('Delete "Weekly 1:1 with Sam"?');
    expect(document.querySelector('.dialog-body')?.textContent).toMatch(
      /^Removes the recording, its 2 tracks and its details and highlights from this PC \(\d+ MB\)\. Exported copies are not affected\. This cannot be undone\.$/,
    );
    expect(document.activeElement?.textContent).toBe('Cancel');

    const confirm = [...document.querySelectorAll<HTMLButtonElement>('.dialog button')].find((b) => b.textContent === 'Delete');
    await act(() => {
      confirm?.click();
    });
    await until(() => document.querySelector('[role="dialog"]') === null && !text('.row-title').includes('Weekly 1:1 with Sam'));
    expect(text('.lib-summary')[0]).toMatch(/^13 recordings/);
  });

  it('renames from the row menu', async () => {
    await start();
    await act(() => {
      container.querySelector<HTMLButtonElement>('[aria-label="More actions for Notes for the README"]')?.click();
    });
    await act(() => {
      [...document.querySelectorAll<HTMLButtonElement>('[role="menuitem"]')].find((b) => b.textContent === 'Rename')?.click();
    });
    const input = document.querySelector<HTMLInputElement>('#rename-input');
    expect(document.activeElement).toBe(input);
    await act(() => {
      if (input !== null) {
        input.value = 'README notes, take two';
        input.dispatchEvent(new Event('input', { bubbles: true }));
      }
    });
    await act(() => {
      document.querySelector<HTMLFormElement>('#rename-form')?.requestSubmit();
    });
    await settle(20);
    expect(text('.row-title')).toContain('README notes, take two');
  });

  it('opens Review on click and keeps the filter for the way back', async () => {
    await start();
    await act(() => {
      [...container.querySelectorAll<HTMLButtonElement>('.chip')].find((c) => c.textContent === 'Lectures')?.click();
    });
    await settle(10);
    await act(() => {
      container.querySelector<HTMLButtonElement>('.lib-row')?.click();
    });
    expect(store.route.value).toEqual({ name: 'review', recordingId: '20261003-110000-cs301' });
    await settle(10);
    expect(container.querySelector('.spoke-title')?.textContent).toBe('CS 301, lecture 12: consensus protocols');

    await act(() => {
      container.querySelector<HTMLButtonElement>('[data-spoke-back]')?.click();
    });
    expect(store.route.value).toEqual({ name: 'library' });
    expect(container.querySelector('.chip.on')?.textContent).toBe('Lectures');
    expect(container.querySelector('.lib-row.selected')?.textContent).toContain('CS 301');
  });

  it('shows the recovery dialog at launch; Later acknowledges it', async () => {
    await start({ recovery: true });
    expect(document.querySelector('.dialog-title')?.textContent).toBe('Recovered an interrupted recording');
    expect(document.querySelector('.dialog-body')?.textContent).toBe(
      'Weekly 1:1 with Sam was saved up to 29:08 before the recording was interrupted. All 2 tracks are intact. The last 20 seconds may be missing.',
    );
    await act(() => {
      [...document.querySelectorAll<HTMLButtonElement>('.dialog button')].find((b) => b.textContent === 'Later')?.click();
    });
    await settle(10);
    expect(document.querySelector('[role="dialog"]')).toBeNull();
    await expect(bridge.call('recovery.list')).resolves.toEqual({ items: [] });
  });

  it('shows the low-space banner and the footer warning', async () => {
    await start({ lowSpace: true });
    await settle(450);
    expect(container.querySelector('.banner')?.textContent).toContain(
      'Low disk space · 4 GB free. Recording continues. Transcription is paused until there is room.',
    );
    expect(container.querySelector('.footer-storage--low')?.textContent).toBe('Low disk space · 4 GB free');
  });

  it('shows a transcript match snippet under the meta line with the matched words bold', async () => {
    await start();
    const search = container.querySelector<HTMLInputElement>('#lib-search');
    if (search === null) {
      throw new Error('no search');
    }
    await act(() => {
      search.value = 'processing card';
      search.dispatchEvent(new Event('input', { bubbles: true }));
    });
    await until(() => container.querySelector('.row-snippet') !== null);
    const row = [...container.querySelectorAll('.lib-row')].find((r) => r.querySelector('.row-title')?.textContent === 'Design review: library screen');
    const snippet = row?.querySelector('.row-snippet');
    expect(snippet?.textContent).toMatch(/processing card/i);
    expect([...(snippet?.querySelectorAll('b') ?? [])].map((b) => b.textContent.toLocaleLowerCase())).toEqual(['processing', 'card']);
    // A title match carries no snippet.
    await act(() => {
      search.value = 'town';
      search.dispatchEvent(new Event('input', { bubbles: true }));
    });
    await until(() => text('.row-title')[0] === 'Town hall Q&A');
    expect(container.querySelector('.row-snippet')).toBeNull();
  });

  it('retries a failed stage from its pill and from the row menu', async () => {
    await start({ stepMs: 30 });
    const failed = container.querySelector<HTMLElement>('[data-recording-id="20260930-130500-onbrd"] .pill.failed');
    expect(failed?.textContent).toBe('Transcript failed · Retry');
    expect([...(failed?.parentElement?.querySelectorAll('.pill') ?? [])].map((p) => p.textContent)).toEqual(['Transcript failed · Retry', 'Stored']);
    // The keyboard gets the same Retry in the row menu.
    await act(() => {
      container.querySelector<HTMLButtonElement>('[aria-label="More actions for Customer call: onboarding feedback"]')?.click();
    });
    expect([...document.querySelectorAll('[role="menuitem"]')].map((m) => m.textContent)).toEqual(['Rename', 'Change type', 'Retry transcript', 'Delete']);
    await act(() => {
      container.querySelector<HTMLButtonElement>('[aria-label="More actions for Customer call: onboarding feedback"]')?.click();
    });
    await act(() => {
      failed?.click();
    });
    // The click retried; it did not open the recording.
    expect(store.route.value.name).toBe('library');
    await until(() => container.querySelector('[data-recording-id="20260930-130500-onbrd"] .pill.failed') === null);
    await until(() => (container.querySelector('[data-recording-id="20260930-130500-onbrd"] .row-pills')?.textContent ?? '').includes('Transcript'));
    const project = await bridge.call('transcript.get', { recordingId: '20260930-130500-onbrd' });
    expect(['queued', 'running', 'done']).toContain(project.status);
  });

  it('shows the empty state for an empty library', async () => {
    await start({ library: 'empty' });
    expect(container.querySelector('h1')?.textContent).toBe('Your library is empty');
  });
});
