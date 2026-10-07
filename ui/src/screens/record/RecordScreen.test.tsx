import { render } from 'preact';
import { act } from 'preact/test-utils';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { App } from '../../App';
import { createBridgeClient, type BridgeClient } from '../../bridge/client';
import type { MockOptions } from '../../bridge/mock';
import { LIVE_DRAFT_GRACE_MS, liveTranscriptCopy } from './RecordParts';
import { loadInitialData, refreshLibrary } from '../../state/data';
import { connectEvents, createStore, type AppStore } from '../../state/store';

const quiet = { info: () => undefined, warn: () => undefined };

describe('Recording session (against the browser-preview host)', () => {
  let container: HTMLDivElement;
  let store: AppStore;
  let bridge: BridgeClient;
  let disconnect: () => void;

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
      await settle(25);
    }
  };

  const button = (name: string): HTMLButtonElement => {
    const match = [...document.querySelectorAll('button')].find(
      (b) => (b.getAttribute('aria-label') ?? b.textContent.trim()) === name,
    );
    if (match === undefined) {
      throw new Error(`no button named ${name}`);
    }
    return match;
  };

  const setUp = async (mock: MockOptions = {}): Promise<void> => {
    bridge = createBridgeClient({ logger: quiet, mock: { live: false, recovery: false, ...mock } });
    store = createStore(false);
    disconnect = connectEvents(bridge, store, {
      onLibraryChanged: () => {
        void refreshLibrary(bridge, store);
      },
    });
    await act(async () => {
      await loadInitialData(bridge, store);
    });
  };

  const mount = async (): Promise<void> => {
    await act(async () => {
      render(<App bridge={bridge} store={store} />, container);
      await Promise.resolve();
    });
  };

  const stopSession = async (): Promise<void> => {
    const current = (await bridge.call('recording.current')).session;
    if (current !== null && (current.state === 'recording' || current.state === 'paused')) {
      await bridge.call('recording.stop', { sessionId: current.sessionId });
    }
  };

  beforeEach(() => {
    container = document.createElement('div');
    document.body.append(container);
  });

  afterEach(async () => {
    await stopSession();
    render(null, container);
    disconnect();
    container.remove();
    document.body.innerHTML = '';
  });

  it('opens ready to record with the remembered sources and the video toggles for later', async () => {
    await setUp();
    store.route.value = { name: 'record', sessionId: null };
    await mount();
    await until(() => container.querySelectorAll('.src').length === 5);

    expect(container.querySelector('.lbl')?.textContent).toBe('Audio sources');
    expect(container.textContent).toContain('Ready to record');
    expect(container.querySelector('.rec-timer')?.textContent).toBe('00:00:00');
    expect(container.querySelector<HTMLInputElement>('#rec-title')?.value).toBe('Untitled meeting');
    expect(container.querySelector('.rec-subline')?.textContent).toBe('3 audio sources selected · audio only');
    const switches = [...container.querySelectorAll<HTMLButtonElement>('[role="switch"]')];
    expect(switches.map((s) => [s.getAttribute('aria-label'), s.getAttribute('aria-checked'), s.disabled])).toEqual([
      ['Microphone', 'true', false],
      ['System audio', 'true', false],
      ['Zoom', 'true', false],
      ['Screen (available in a later version)', 'false', true],
      ['Camera (available in a later version)', 'false', true],
    ]);
    expect(container.textContent).toContain('Available in a later version');
    expect(container.querySelector('footer')?.textContent).toContain('Ready · recordings save to this PC as they happen');
    expect(container.querySelector('footer')?.textContent).toContain('212 GB free · about 87 hours at this quality');
  });

  it('starts, stops, shows Finalizing until the host reports ready, then opens Review for that recording', async () => {
    await setUp();
    store.route.value = { name: 'record', sessionId: null };
    await mount();
    await until(() => container.querySelectorAll('.src').length === 5);

    await act(async () => {
      button('Start recording').click();
      await Promise.resolve();
    });
    await until(() => store.recording.value?.state === 'recording');
    const session = store.recording.value;
    expect(store.route.value).toEqual({ name: 'record', sessionId: session?.sessionId });
    expect(container.textContent).toContain('Recording');
    expect(container.querySelector('.rec-subline')?.textContent).toMatch(/^Started \d{1,2}:\d{2} [AP]M · 3 audio tracks · audio only$/);
    expect(container.querySelectorAll('.rec-lane-row[data-live="true"]')).toHaveLength(3);

    await act(async () => {
      button('Stop and open review').click();
      await Promise.resolve();
    });
    await until(() => container.textContent.includes('Finalizing…'));
    expect(store.route.value.name).toBe('record');

    await until(() => store.route.value.name === 'review', 4000);
    expect(store.route.value).toEqual({ name: 'review', recordingId: session?.recordingId });
    await until(() => container.querySelector('.spoke-title')?.textContent === 'Untitled meeting');
    // The transcript waits for the stored tracks, then runs on its own (Settings › Transcription).
    await until(() => container.textContent.includes('Waiting to transcribe'));
    // Still storing: no peaks yet, and the sunk empty track says why.
    expect(container.querySelector('.wave--empty')?.textContent).toBe('Waveform appears once the recording is finalized');
    expect(container.querySelector<HTMLButtonElement>('.player-play')?.disabled).toBe(true);
  });

  it('rejoins an active session through recording.current, highlights included', async () => {
    await setUp();
    const started = await bridge.call('recording.start', { title: 'Q3 planning sync', type: 'meeting', sourceIds: ['mic:usb-mv7'] });
    await bridge.call('recording.markHighlight', { sessionId: started.sessionId, note: 'Launch date agreed' });
    await settle(20);
    // As after a reload: the page knows nothing about the session.
    store.recording.value = null;
    const call = vi.spyOn(bridge, 'call');
    store.route.value = { name: 'record', sessionId: null };
    await mount();
    await until(() => store.recording.value?.sessionId === started.sessionId);
    await until(() => container.querySelector('.rec-highlights') !== null);

    expect(call.mock.calls.some(([method]) => method === 'recording.current')).toBe(true);
    expect(container.querySelector<HTMLInputElement>('#rec-title')?.value).toBe('Q3 planning sync');
    expect(container.querySelector<HTMLInputElement>('.rec-mark-note')?.value).toBe('Launch date agreed');
    expect(container.querySelectorAll('.rec-lane-row[data-live]')).toHaveLength(1);
    expect(button('Pause')).toBeDefined();
  });

  it('pauses with Space, marks with Ctrl+M and leaves Space to a note being typed', async () => {
    await setUp();
    store.route.value = { name: 'record', sessionId: null };
    await mount();
    await until(() => container.querySelectorAll('.src').length === 5);
    await act(async () => {
      button('Start recording').click();
      await Promise.resolve();
    });
    await until(() => store.recording.value?.state === 'recording');
    const key = (init: KeyboardEventInit, target: EventTarget = document.body): KeyboardEvent => {
      const event = new KeyboardEvent('keydown', { bubbles: true, cancelable: true, ...init });
      target.dispatchEvent(event);
      return event;
    };

    let space: KeyboardEvent | undefined;
    await act(async () => {
      space = key({ key: ' ' });
      await Promise.resolve();
    });
    expect(space?.defaultPrevented).toBe(true);
    await until(() => store.recording.value?.state === 'paused');
    expect(container.textContent).toContain('Paused');
    expect(container.querySelector('footer')?.textContent).toContain('Paused · everything so far is saved on this PC');

    await act(async () => {
      key({ key: ' ' });
      await Promise.resolve();
    });
    await until(() => store.recording.value?.state === 'recording');

    await act(async () => {
      key({ key: 'm', ctrlKey: true });
      await Promise.resolve();
    });
    await until(() => container.querySelector('.rec-mark-note') !== null);
    const note = container.querySelector<HTMLInputElement>('.rec-mark-note');
    await until(() => document.activeElement === note);

    await act(async () => {
      key({ key: ' ' }, note ?? document.body);
      await Promise.resolve();
    });
    await settle(50);
    expect(store.recording.value?.state).toBe('recording');

    const escape = key({ key: 'Escape' }, note ?? document.body);
    expect(escape.defaultPrevented).toBe(false); // typed into the note: Esc belongs to the field
    note?.blur();
    const escapeBody = key({ key: 'Escape' });
    expect(escapeBody.defaultPrevented).toBe(true);
    expect(store.recording.value?.state).toBe('recording');
  });

  it('takes a pasted agenda in the sheet, reorders it from the keyboard and ticks items off while recording', async () => {
    await setUp();
    store.route.value = { name: 'record', sessionId: null };
    await mount();
    await until(() => container.querySelectorAll('.src').length === 5);
    await act(async () => {
      button('Details and agenda').click();
      await Promise.resolve();
    });
    const sheet = (): HTMLElement => {
      const el = document.querySelector<HTMLElement>('.sheet');
      if (el === null) {
        throw new Error('no sheet');
      }
      return el;
    };
    expect(sheet().textContent).toContain('Drop an agenda here');
    expect(sheet().textContent).toContain('Word, PDF, Excel, CSV, Markdown, plain text, or a photo of a printed agenda');
    expect(sheet().textContent).toContain('Parsed on this PC. Nothing is uploaded.');
    // M3: file import is live (agenda.importFile); pasted text is parsed by the host (agenda.parseText).
    expect(button('Choose a file').disabled).toBe(false);

    await act(async () => {
      button('Paste text').click();
      await Promise.resolve();
    });
    const textarea = sheet().querySelector<HTMLTextAreaElement>('#agenda-paste');
    if (textarea === null) {
      throw new Error('no paste box');
    }
    await act(async () => {
      textarea.value = '1. Q2 recap\r\n2. Hiring plan\r\n\r\n- Launch date\r\n• Budget asks';
      textarea.dispatchEvent(new Event('input', { bubbles: true }));
      await Promise.resolve();
    });
    await act(async () => {
      button('Add items').click();
      await Promise.resolve();
    });
    const items = (): string[] => [...sheet().querySelectorAll<HTMLInputElement>('.agenda-item .ii')].map((i) => i.value);
    await until(() => items().length === 4);
    expect(items()).toEqual(['Q2 recap', 'Hiring plan', 'Launch date', 'Budget asks']);
    expect(sheet().querySelector('.sheet-caption')?.textContent).toContain('Pasted text · parsed on this PC');
    expect(sheet().querySelector('.agenda-item.low')).toBeNull();
    expect(button('Extract').disabled).toBe(true);
    expect(sheet().textContent).toContain('External AI is off. Turn it on in Settings › AI and privacy.');

    const handle = sheet().querySelector<HTMLButtonElement>('.drag-handle');
    await act(async () => {
      handle?.focus();
      handle?.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true, cancelable: true }));
      await Promise.resolve();
    });
    await settle(10);
    expect(items()).toEqual(['Hiring plan', 'Q2 recap', 'Launch date', 'Budget asks']);
    expect(document.activeElement?.getAttribute('aria-label')).toContain('Q2 recap');
    expect(sheet().querySelector('.sheet-save')?.textContent).toBe('Saved with the recording when it starts.');

    await act(async () => {
      button('Done').click();
      await Promise.resolve();
    });
    // Done applies the parsed agenda first (before the recording exists it goes with the details).
    await until(() => document.querySelector('.sheet') === null);
    const agenda = (): string[] => [...container.querySelectorAll('.rec-agenda-item')].map((b) => b.className.replace('rec-agenda-item rec-agenda-item--', ''));
    expect(agenda()).toEqual(['current', 'upcoming', 'upcoming', 'upcoming']);

    const call = vi.spyOn(bridge, 'call');
    await act(async () => {
      button('Start recording').click();
      await Promise.resolve();
    });
    await until(() => store.recording.value?.state === 'recording');
    // The agenda edited before Start reaches the new project.
    await until(() => call.mock.calls.some(([method]) => method === 'project.updateDetails'));
    const recordingId = store.recording.value?.recordingId ?? '';
    expect((await bridge.call('project.get', { recordingId })).details.agenda.items.map((i) => i.text)).toEqual([
      'Hiring plan',
      'Q2 recap',
      'Launch date',
      'Budget asks',
    ]);

    const first = container.querySelector<HTMLButtonElement>('.rec-agenda-item');
    await act(async () => {
      first?.click();
      await Promise.resolve();
    });
    expect(agenda()).toEqual(['covered', 'current', 'upcoming', 'upcoming']);
    expect(first?.getAttribute('aria-pressed')).toBe('true');
    await settle(700);
    expect((await bridge.call('project.get', { recordingId })).details.agenda.items.map((i) => i.covered)).toEqual([true, false, false, false]);
  });

  it('saves a highlight note with annotations.updateHighlight when the field is left', async () => {
    await setUp();
    store.route.value = { name: 'record', sessionId: null };
    await mount();
    await until(() => container.querySelectorAll('.src').length === 5);
    await act(async () => {
      button('Start recording').click();
      await Promise.resolve();
    });
    await until(() => store.recording.value?.state === 'recording');
    await act(async () => {
      button('Mark highlight').click();
      await Promise.resolve();
    });
    await until(() => container.querySelector('.rec-mark-note') !== null);
    const note = container.querySelector<HTMLInputElement>('.rec-mark-note');
    if (note === null) {
      throw new Error('no note field');
    }
    const call = vi.spyOn(bridge, 'call');
    await act(async () => {
      note.value = 'Decision: launch date';
      note.dispatchEvent(new Event('input', { bubbles: true }));
      await Promise.resolve();
    });
    await act(async () => {
      note.dispatchEvent(new FocusEvent('blur'));
      note.dispatchEvent(new FocusEvent('focusout', { bubbles: true }));
      await Promise.resolve();
    });
    const update = call.mock.calls.find(([method]) => method === 'annotations.updateHighlight');
    expect(update?.[1]).toMatchObject({ recordingId: store.recording.value?.recordingId, highlight: { note: 'Decision: launch date' } });
    const project = await bridge.call('project.get', { recordingId: store.recording.value?.recordingId ?? '' });
    expect(project.highlights.map((h) => h.note)).toEqual(['Decision: launch date']);
  });

  it('shows the live draft when the host sends one (?live=1)', async () => {
    await setUp({ liveTranscript: true });
    store.route.value = { name: 'record', sessionId: null };
    await mount();
    await until(() => container.querySelectorAll('.src').length === 5);
    const card = (): Element | null => container.querySelector('[aria-label="Live transcript"]');
    expect(card()?.textContent).toContain('Words appear here a few seconds behind the recording.');
    await act(async () => {
      button('Start recording').click();
      await Promise.resolve();
    });
    await until(() => (card()?.querySelectorAll('.rec-live-line').length ?? 0) >= 2, 10_000);
    const lines = [...(card()?.querySelectorAll('.rec-live-line') ?? [])];
    expect(lines[0]?.querySelector('.rec-live-at')?.textContent).toBe('00:00:00');
    expect(lines.at(-1)?.querySelector('.rec-live-more')?.textContent).toBe(' …');
    expect(lines[0]?.querySelector('.rec-live-more')).toBeNull();
    expect(card()?.querySelector('.pill')?.textContent).toBe('Local · GPU');
    expect(card()?.textContent).toContain('Rough draft. Speaker names and corrections happen in Review.');
  });

  it('says live transcription is off when Timing is After recording', async () => {
    await setUp();
    store.route.value = { name: 'record', sessionId: null };
    await mount();
    await until(() => container.querySelectorAll('.src').length === 5);
    const card = container.querySelector('[aria-label="Live transcript"]');
    expect(card?.textContent).toContain('Live transcription is off.');
    expect(card?.querySelector('.pill')).toBeNull();
    expect(liveTranscriptCopy('during', 'recording', 2_000)).toBe('Listening. Words appear here a few seconds behind the recording.');
    expect(liveTranscriptCopy('during', 'recording', LIVE_DRAFT_GRACE_MS)).toMatch(/^Live transcription is not available in this version of Memento\./);
    expect(liveTranscriptCopy('after', 'recording', 60_000)).toMatch(/^Live transcription is off\./);
  });
});
