import { render } from 'preact';
import { act } from 'preact/test-utils';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { App } from '../../App';
import { createBridgeClient, type BridgeClient } from '../../bridge/client';
import { loadInitialData, refreshLibrary } from '../../state/data';
import { connectEvents, createStore, type AppStore } from '../../state/store';
import { startedWord } from './RecordScreen';

const quiet = { info: () => undefined, warn: () => undefined };

// 2.1 "Resume recording" (DESIGN.md §8, §9): from the Recording session after Stop and from Review's header.
describe('Resume recording (against the browser-preview host)', () => {
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

  const findButton = (name: string): HTMLButtonElement | undefined =>
    [...document.querySelectorAll('button')].find((b) => (b.getAttribute('aria-label') ?? b.textContent.trim()) === name);

  const button = (name: string): HTMLButtonElement => {
    const match = findButton(name);
    if (match === undefined) {
      throw new Error(`no button named ${name}`);
    }
    return match;
  };

  const click = async (name: string): Promise<void> => {
    await act(async () => {
      button(name).click();
      await Promise.resolve();
    });
  };

  beforeEach(async () => {
    container = document.createElement('div');
    document.body.append(container);
    bridge = createBridgeClient({ logger: quiet, mock: { live: false, recovery: false } });
    store = createStore(false);
    disconnect = connectEvents(bridge, store, {
      onLibraryChanged: () => {
        void refreshLibrary(bridge, store);
      },
    });
    await act(async () => {
      await loadInitialData(bridge, store);
    });
  });

  afterEach(async () => {
    const current = (await bridge.call('recording.current')).session;
    if (current !== null && (current.state === 'recording' || current.state === 'paused')) {
      await bridge.call('recording.stop', { sessionId: current.sessionId });
    }
    render(null, container);
    disconnect();
    container.remove();
    document.body.innerHTML = '';
  });

  const mount = async (): Promise<void> => {
    await act(async () => {
      render(<App bridge={bridge} store={store} />, container);
      await Promise.resolve();
    });
  };

  it('resumes from the Recording session after Stop: the next segment records into the same recording', async () => {
    store.route.value = { name: 'record', sessionId: null };
    await mount();
    await until(() => container.querySelectorAll('.rec-video-src').length === 3);
    await click('Start recording');
    await until(() => store.recording.value?.state === 'recording');
    const first = store.recording.value;
    await settle(1100);

    await click('Stop and open review');
    await until(() => container.textContent.includes('Finalizing…'));
    expect(container.textContent).toContain('Stopped by mistake? Resume records more into this recording');
    await click('Resume recording');
    expect(button('Resumes once saved…').disabled).toBe(true);

    // Once saved, the same recording records again instead of opening Review.
    await until(() => store.recording.value?.state === 'recording' && store.recording.value.sessionId !== first?.sessionId, 6000);
    expect(store.route.value.name).toBe('record');
    const second = store.recording.value;
    expect(second?.recordingId).toBe(first?.recordingId);
    expect(second?.segment).toBe(2);
    expect(second?.segmentOffsetMs).toBeGreaterThan(0);
    await until(() => (container.querySelector('.rec-subline')?.textContent ?? '').startsWith('Resumed '));
    expect(container.textContent).toContain('Recording');

    // Stop again: Review opens on the recording, which says where it was resumed.
    await click('Stop and open review');
    await until(() => store.route.value.name === 'review', 6000);
    expect(store.route.value).toEqual({ name: 'review', recordingId: first?.recordingId });
    await until(() => container.querySelector('.review-meta-resumed') !== null);
    expect(container.querySelector('.review-meta-resumed')?.textContent).toMatch(/^Resumed at \d{1,2}:\d{2} [AP]M$/);
    const project = await bridge.call('project.get', { recordingId: first?.recordingId ?? '' });
    expect(project.segments?.map((s) => s.index)).toEqual([1, 2]);
    expect(project.tracks.length).toBe(6);
  });

  it('offers Resume recording in Review for a recording made today and opens the Recording session on it', async () => {
    const started = await bridge.call('recording.start', { title: 'Stand-up', type: 'meeting', sourceIds: ['mic:usb-mv7'] });
    await settle(600);
    await bridge.call('recording.stop', { sessionId: started.sessionId });
    await until(() => store.recording.value?.state === 'ready', 6000);

    store.route.value = { name: 'review', recordingId: started.recordingId };
    await mount();
    await until(() => findButton('Resume recording') !== undefined);
    await click('Resume recording');
    await until(() => store.route.value.name === 'record');
    await until(() => store.recording.value?.state === 'recording' && store.recording.value.segment === 2);
    expect(store.recording.value?.recordingId).toBe(started.recordingId);
  });

  it('does not offer Resume recording for a recording from an earlier day, and the host says why', async () => {
    const library = await bridge.call('library.list', {});
    const old = library.recordings.find((r) => new Date(r.createdAt).toDateString() !== new Date().toDateString());
    expect(old).toBeDefined();
    store.route.value = { name: 'review', recordingId: old?.id ?? '' };
    await mount();
    await until(() => container.querySelector('.spoke-title') !== null);
    await settle(50);
    expect(findButton('Resume recording')).toBeUndefined();
    const project = await bridge.call('project.get', { recordingId: old?.id ?? '' });
    expect(project.canResume).toBe(false);
    expect(project.resumeNote).toContain('only recordings started today can be resumed');
    await expect(bridge.call('recording.resumeStopped', { recordingId: old?.id ?? '' })).rejects.toMatchObject({ code: 'recording.cannotResume' });
  });

  it('words the session line for a resumed segment', () => {
    const base = { sessionId: 's', recordingId: 'r', state: 'recording' as const, startedAt: '', elapsedMs: 0, tracks: [], lastCheckpointAt: null, highlightsCount: 0 };
    expect(startedWord(base)).toBe('Started');
    expect(startedWord({ ...base, segment: null })).toBe('Started');
    expect(startedWord({ ...base, segment: 2, segmentOffsetMs: 1000 })).toBe('Resumed');
  });
});
