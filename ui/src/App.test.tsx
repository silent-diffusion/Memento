import { render } from 'preact';
import { act } from 'preact/test-utils';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { App } from './App';
import type { BridgeClient } from './bridge/client';
import type { FooterStatusPayload } from './bridge/types';
import { createStore, type AppStore } from './state/store';

const bridge: BridgeClient = {
  isHosted: false,
  call: vi.fn(() => new Promise<never>(() => undefined)),
  on: vi.fn(() => () => undefined),
};

const EMPTY = { recordings: [], totalDurationMs: 0, totalCount: 0 };

const footerPayload = (overrides: Partial<FooterStatusPayload> = {}): FooterStatusPayload => ({
  engine: { ready: false, device: null, detail: { ready: false, device: null, gpuName: null, freeVramBytes: null, model: null, paused: null } },
  storage: { freeBytes: 212 * 1024 ** 3, lowSpace: false },
  recording: { active: false, lastCheckpointAt: null, lostSource: null },
  processingPaused: null,
  ...overrides,
});

describe('Library shell, first run', () => {
  let container: HTMLDivElement;
  let store: AppStore;

  beforeEach(() => {
    container = document.createElement('div');
    document.body.append(container);
    store = createStore(false);
  });

  afterEach(() => {
    render(null, container);
    container.remove();
  });

  const mount = (): void => {
    void act(() => {
      render(<App bridge={bridge} store={store} />, container);
    });
  };

  const button = (name: string): HTMLButtonElement => {
    const match = [...container.querySelectorAll('button')].find(
      (b) => (b.getAttribute('aria-label') ?? b.textContent.trim()) === name,
    );
    if (match === undefined) {
      throw new Error(`no button named ${name}`);
    }
    return match;
  };

  it('shows the empty state copy and real, named buttons once the library is known to be empty', () => {
    store.library.value = EMPTY;
    mount();

    expect(container.querySelector('h1')?.textContent).toBe('Your library is empty');
    expect(container.textContent).toContain(
      'Recordings, transcripts and documents live here, on this PC. Nothing leaves it unless you choose to export.',
    );
    for (const name of ['New recording', 'Settings', 'Start your first recording', 'Import audio or video']) {
      expect(button(name).type).toBe('button');
    }
    expect([...container.querySelectorAll('h2')].map((h) => h.textContent)).toEqual([
      'Record what you choose',
      'Transcribe on this PC',
      'Turn it into documents',
    ]);
    expect(container.textContent).toContain('AI is optional and off by default.');
  });

  it('disables search with the explanatory placeholder and keeps its label', () => {
    store.library.value = EMPTY;
    mount();

    const search = container.querySelector<HTMLInputElement>('input[type="search"]');
    expect(search?.disabled).toBe(true);
    expect(search?.placeholder).toBe('Search will work once you have a recording');
    expect(container.querySelector(`label[for="${search?.id ?? ''}"]`)?.textContent.trim()).toBe('Search recordings');
  });

  it('shows neutral footer placeholders until the host reports, then live values', () => {
    mount();
    const footer = (): string => container.querySelector('footer')?.textContent ?? '';
    expect(footer()).toContain('Checking transcription engine');
    expect(footer()).toContain('Everything is stored on this PC');
    expect(footer()).not.toMatch(/\d/);

    void act(() => {
      store.footer.value = footerPayload();
    });

    expect(footer()).toContain('No transcription model installed');
    expect(footer()).toContain('Everything is stored on this PC · 212 GB free');
  });

  it('shows the footer warning variants', () => {
    mount();
    const footer = (): HTMLElement | null => container.querySelector('footer');

    void act(() => {
      store.footer.value = footerPayload({ processingPaused: 'Low disk space', storage: { freeBytes: 4 * 1024 ** 3, lowSpace: true } });
    });
    expect(footer()?.querySelector('.status-dot--accent')).not.toBeNull();
    expect(footer()?.textContent).toContain('Transcription paused · Low disk space');
    expect(footer()?.querySelector('.footer-storage--low')?.textContent).toBe('Low disk space · 4 GB free');

    void act(() => {
      store.footer.value = footerPayload({ recording: { active: true, lastCheckpointAt: null, lostSource: 'Zoom' } });
    });
    expect(footer()?.querySelector('.status-dot--danger')).not.toBeNull();
    expect(footer()?.textContent).toContain('Zoom lost · other tracks recording');
    expect(footer()?.textContent).toContain('Saving continuously');
  });

  it('opens the Record and Settings spokes from the header and comes back', () => {
    store.library.value = EMPTY;
    mount();

    void act(() => {
      button('New recording').click();
    });
    expect(store.route.value).toEqual({ name: 'record', sessionId: null });
    expect(container.textContent).toContain('Ready to record');

    void act(() => {
      button('Library').click();
    });
    expect(store.route.value).toEqual({ name: 'library' });

    void act(() => {
      button('Settings').click();
    });
    expect(store.route.value).toEqual({ name: 'settings', section: 'general' });
    expect([...container.querySelectorAll('nav .nav')].map((n) => n.textContent)).toEqual([
      'General',
      'Recording',
      'Transcription',
      'Speakers',
      'AI and privacy',
      'Documents',
      'Export',
      'Storage and history',
    ]);
  });

  it('names the failure and offers a retry when the host cannot answer', () => {
    store.loadError.value = "Memento did not answer 'library.list' within 10 s.";
    mount();

    expect(container.querySelector('[role="alert"]')?.textContent).toContain("Memento did not answer 'library.list' within 10 s.");
    expect(button('Try again')).toBeDefined();
  });
});
