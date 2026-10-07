import { render } from 'preact';
import { act } from 'preact/test-utils';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { App } from '../../App';
import { createBridgeClient, type BridgeClient } from '../../bridge/client';
import type { MockOptions } from '../../bridge/mock';
import { loadInitialData } from '../../state/data';
import type { SettingsSection } from '../../state/router';
import { connectEvents, createStore, type AppStore } from '../../state/store';
import { engineWording } from './TranscriptionSections';

const quiet = { info: () => undefined, warn: () => undefined };
const NOW = new Date(2026, 9, 6, 15, 0);

describe('Settings › Transcription, Speakers and Documents (M2)', () => {
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
      await settle(20);
    }
  };

  const button = (name: string): HTMLButtonElement => {
    const match = [...document.querySelectorAll('button')].find((b) => (b.getAttribute('aria-label') ?? b.textContent.trim()) === name);
    if (match === undefined) {
      throw new Error(`no button named ${name}`);
    }
    return match;
  };

  const click = async (el: Element | null | undefined): Promise<void> => {
    await act(async () => {
      (el as HTMLElement | null)?.click();
      await Promise.resolve();
    });
  };

  const open = async (section: SettingsSection, mock: MockOptions = {}): Promise<void> => {
    bridge = createBridgeClient({ logger: quiet, mock: { live: false, recovery: false, now: () => NOW.getTime(), stepMs: 10, ...mock } });
    store = createStore(false);
    disconnect = connectEvents(bridge, store);
    await act(async () => {
      await loadInitialData(bridge, store);
    });
    store.route.value = { name: 'settings', section };
    await act(async () => {
      render(<App bridge={bridge} store={store} now={() => NOW} />, container);
      await Promise.resolve();
    });
  };

  const choose = async (label: string, option: string): Promise<void> => {
    await click(document.querySelector(`[aria-label^="${label}:"]`));
    await click([...document.querySelectorAll('[role="option"]')].find((o) => o.textContent.trim() === option));
  };

  beforeEach(() => {
    container = document.createElement('div');
    document.body.append(container);
  });

  afterEach(() => {
    render(null, container);
    disconnect();
    container.remove();
    document.body.innerHTML = '';
  });

  it('saves the When and Engine rows as whole transcription blocks', async () => {
    await open('transcription');
    await until(() => container.querySelector('.model-card') !== null);
    expect(container.querySelector('.settings-blurb')?.textContent).toBe('Runs on this PC. Nothing is uploaded.');
    await until(() => container.querySelector('[data-testid="engine-value"]')?.textContent === 'Local · GPU (NVIDIA GeForce RTX 4070)');
    const call = vi.spyOn(bridge, 'call');

    await click(button('Transcribe automatically'));
    await click(button('During recording'));
    await choose('Mark words as uncertain below', '60% confidence');
    await choose('Language', 'German');
    await until(() => store.settings.value?.transcription.language === 'de');
    const sets = call.mock.calls.filter(([method]) => method === 'settings.set').map(([, params]) => params as { transcription?: Record<string, unknown> });
    expect(sets).toHaveLength(4);
    // Each change sends the whole block.
    expect(Object.keys(sets[0]?.transcription ?? {}).sort()).toEqual(
      ['auto', 'cpuFallbackModelId', 'keepWordTimestamps', 'language', 'lowConfidenceThreshold', 'modelId', 'pauseWhenBusy', 'timing'].sort(),
    );
    expect(await bridge.call('settings.get')).toMatchObject({
      transcription: { auto: false, timing: 'during', lowConfidenceThreshold: 0.6, language: 'de' },
    });
  });

  it('lists the transcription models, installs one with progress and makes it the default', async () => {
    await open('transcription');
    await until(() => container.querySelectorAll('.model-card').length === 5);
    const names = [...container.querySelectorAll('.model-card .model-name')].map((n) => n.firstChild?.textContent);
    expect(names).toEqual(['Large v3', 'Large v3 Turbo', 'Medium', 'Small', 'Base']);
    const largeV3 = container.querySelector('[data-model-id="large-v3"]');
    expect(largeV3?.querySelector('.model-tag')?.textContent).toBe('Recommended');
    expect(largeV3?.querySelector<HTMLInputElement>('input[type="radio"]')?.checked).toBe(true);
    expect(button('Remove Large v3').disabled).toBe(true);
    // The CPU fallback cannot be removed either.
    expect(button('Remove Small').disabled).toBe(true);
    const medium = container.querySelector('[data-model-id="medium"]');
    expect(medium?.querySelector<HTMLInputElement>('input[type="radio"]')?.disabled).toBe(true);

    await click(button('Install Medium, 788 MB'));
    await until(() => medium?.querySelector('[role="progressbar"]') !== null);
    // One download at a time.
    expect(button('Install Base, 142 MB').disabled).toBe(true);
    await until(() => document.querySelector<HTMLButtonElement>('[aria-label="Remove Medium"]')?.disabled === false);
    expect(medium?.querySelector('[role="progressbar"]')).toBeNull();
    await click(medium?.querySelector('input[type="radio"]'));
    await until(() => store.settings.value?.transcription.modelId === 'medium');
    expect((await bridge.call('engine.status')).transcription.model).toBe('medium');
  });

  it('cancels a download and shows a refused download inline', async () => {
    await open('transcription', { models: 'noSpace' });
    await until(() => container.querySelectorAll('.model-card').length === 5);
    await click(button('Install Medium, 788 MB'));
    await until(() => container.querySelector('[data-model-id="medium"] .model-error') !== null);
    const error = container.querySelector('[data-model-id="medium"] .model-error');
    expect(error?.getAttribute('role')).toBe('alert');
    expect(error?.textContent).toMatch(/^Not enough space for Medium\. Medium needs 788 MB and the library drive has 300 MB free\. Nothing was downloaded\./);
    await click(button('Dismiss'));
    expect(container.querySelector('.model-error')).toBeNull();

    await click(button('Install Base, 142 MB'));
    await until(() => container.querySelector('[data-model-id="base"] [role="progressbar"]') !== null);
    await click(button('Cancel the download of Base'));
    await until(() => container.querySelector('[data-model-id="base"] [role="progressbar"]') === null);
    expect(button('Install Base, 142 MB').disabled).toBe(false);
  });

  it('saves Speakers and lists the speaker models', async () => {
    await open('speakers');
    await until(() => container.querySelectorAll('.model-card').length === 2);
    expect(container.textContent).toContain('Stored for a later version');
    await choose('Expected speakers', '4 people');
    await click(button('Identify speakers'));
    await until(() => store.settings.value?.speakers.identify === false);
    expect((await bridge.call('settings.get')).speakers).toEqual({ identify: false, expectedSpeakers: 4, rememberRenamed: true, embeddingModelId: 'voice-resnet34' });
  });

  it('saves version history in Documents', async () => {
    await open('documents');
    await until(() => container.textContent.includes('Keep version history'));
    await choose('Keep versions for', '365 days');
    await until(() => store.settings.value?.history.keepDays === 365);
    await click(button('Keep version history'));
    await until(() => store.settings.value?.history.keepVersions === false);
    expect(document.querySelector<HTMLButtonElement>('[aria-label^="Keep versions for:"]')?.disabled).toBe(true);
    expect((await bridge.call('settings.get')).history).toEqual({ keepVersions: false, keepDays: 365 });
  });

  it('words the engine row from engine.status', () => {
    const detail = { ready: true, device: 'GPU', gpuName: 'RTX 3060', freeVramBytes: 6 * 1024 ** 3, model: 'large-v3', paused: null };
    expect(engineWording(null)).toEqual({ value: 'Checking…', note: null });
    expect(engineWording({ transcription: detail, speakers: detail })).toEqual({ value: 'Local · GPU (RTX 3060)', note: '6 GB video memory free' });
    expect(engineWording({ transcription: { ...detail, device: 'CPU', gpuName: null, freeVramBytes: null }, speakers: detail })).toEqual({
      value: 'Local · CPU',
      note: null,
    });
    expect(engineWording({ transcription: { ...detail, paused: 'PC is busy' }, speakers: detail }).value).toBe('Paused · PC is busy');
    expect(engineWording({ transcription: { ...detail, ready: false, device: null, model: null }, speakers: detail })).toEqual({
      value: 'Not ready',
      note: 'Install a model below',
    });
  });
});
