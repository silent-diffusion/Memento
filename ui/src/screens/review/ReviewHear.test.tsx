// Hear a speaker (DESIGN.md §9, 2.6.0): the play button on a People row plays a few seconds of that
// speaker's clearest line through Review's player, turns into Stop while it plays, stops by itself at
// the sample's end or at once on a second press, gives way to another speaker's sample, and is
// disabled for a speaker without lines. Against the mock host; jsdom cannot play, so the element's
// play and pause are stubbed and the sample's own timer ends it.
import { render } from 'preact';
import { act } from 'preact/test-utils';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { App } from '../../App';
import { createBridgeClient, type BridgeClient } from '../../bridge/client';
import type { TranscriptSegment } from '../../bridge/types';
import { formatDuration } from '../../format/duration';
import { clearestLine, sampleEndMs } from '../../format/voiceSample';
import { loadInitialData, refreshLibrary } from '../../state/data';
import { connectEvents, createStore, type AppStore } from '../../state/store';

const quiet = { info: () => undefined, warn: () => undefined };
const NOW = new Date(2026, 9, 6, 15, 0);
const DESIGN_REVIEW = '20261005-160000-dsrev';

describe('Review › People › Hear {name} (2.6.0, against the browser-preview host)', () => {
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

  /** The People rows' speaker ids, in the order shown. */
  const people = (): string[] => [...container.querySelectorAll<HTMLElement>('.person[data-speaker-id]')].map((p) => p.dataset.speakerId ?? '');

  const hearButton = (speakerId: string): HTMLButtonElement => {
    const button = container.querySelector<HTMLButtonElement>(`.person[data-speaker-id="${speakerId}"] .person-hear`);
    if (button === null) {
      throw new Error(`no hear button for ${speakerId}`);
    }
    return button;
  };

  const scrubberAt = (): string | null => container.querySelector('.scrubber')?.getAttribute('aria-valuetext') ?? null;

  const segments = async (): Promise<TranscriptSegment[]> => (await bridge.call('transcript.get', { recordingId: DESIGN_REVIEW })).transcript?.segments ?? [];

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
    vi.useRealTimers();
    render(null, container);
    disconnect();
    container.remove();
    document.body.innerHTML = '';
    vi.restoreAllMocks();
  });

  it('plays the speaker\'s clearest line from its start, shows Stop meanwhile, and stops by itself at the sample\'s end', async () => {
    await open();
    const id = people()[0] ?? '';
    const line = clearestLine(await segments(), id);
    if (line === null) {
      throw new Error('the first speaker has no lines');
    }
    const name = hearButton(id).getAttribute('aria-label')?.replace(/^Hear /, '') ?? '';
    expect(name).not.toBe('');

    // Timers are faked from here (real time still moves them), so the sample's length can be jumped over.
    vi.useFakeTimers({ shouldAdvanceTime: true, toFake: ['setTimeout', 'clearTimeout'] });
    await click(hearButton(id));
    expect(scrubberAt()).toBe(`${formatDuration(line.start * 1000)} of 1:10:02`);
    expect(played).toBe(1);
    expect(hearButton(id).getAttribute('aria-label')).toBe(`Stop hearing ${name}`);
    expect(hearButton(id).getAttribute('aria-pressed')).toBe('true');
    expect(hearButton(id).classList.contains('person-hear--on')).toBe(true);

    await act(async () => {
      await vi.advanceTimersByTimeAsync(sampleEndMs(line) - line.start * 1000 + 500);
    });
    expect(hearButton(id).getAttribute('aria-label')).toBe(`Hear ${name}`);
    expect(hearButton(id).getAttribute('aria-pressed')).toBe('false');
  });

  it('a second press stops at once, and another speaker\'s sample takes over', async () => {
    await open();
    const [first, second] = people();
    if (first === undefined || second === undefined) {
      throw new Error('fewer than two speakers');
    }
    const all = await segments();
    const firstLine = clearestLine(all, first);
    const secondLine = clearestLine(all, second);
    if (firstLine === null || secondLine === null) {
      throw new Error('a speaker has no lines');
    }

    await click(hearButton(first));
    expect(hearButton(first).getAttribute('aria-label')).toMatch(/^Stop hearing /);
    await click(hearButton(first));
    expect(hearButton(first).getAttribute('aria-label')).toMatch(/^Hear /);

    await click(hearButton(first));
    await click(hearButton(second));
    expect(hearButton(first).getAttribute('aria-label')).toMatch(/^Hear /);
    expect(hearButton(second).getAttribute('aria-label')).toMatch(/^Stop hearing /);
    expect(scrubberAt()).toBe(`${formatDuration(secondLine.start * 1000)} of 1:10:02`);
    expect(played).toBe(3);
  });

  it('a speaker without lines has nothing to hear', async () => {
    await open();
    // A speaker put back with no lines (what Undo does after an added speaker's line moved away).
    await act(async () => {
      await bridge.call('transcript.restoreSpeaker', {
        recordingId: DESIGN_REVIEW,
        speaker: { id: 'spk-silent', name: 'Quiet Person', color: 2, renamed: true },
        segmentIds: [],
      });
    });
    await until(() => container.querySelector('.person[data-speaker-id="spk-silent"]') !== null);
    const button = hearButton('spk-silent');
    expect(button.disabled).toBe(true);
    expect(button.getAttribute('title')).toBe('No lines to hear');
    expect(button.getAttribute('aria-label')).toBe('Hear Quiet Person');
    // Everyone with lines still can be heard.
    expect(hearButton(people()[0] ?? '').disabled).toBe(false);
  });
});
