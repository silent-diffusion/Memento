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

  it('saves the When and Engine rows, sending only the field that changed', async () => {
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
    // The host merges the M2 blocks field by field, so each change sends just its field.
    expect(sets.map((s) => s.transcription)).toEqual([{ auto: false }, { timing: 'during' }, { lowConfidenceThreshold: 0.6 }, { language: 'de' }]);
    expect(await bridge.call('settings.get')).toMatchObject({
      transcription: { auto: false, timing: 'during', lowConfidenceThreshold: 0.6, language: 'de' },
    });
  });

  it('lists the transcription models, installs one with progress and makes it the default', async () => {
    await open('transcription');
    await until(() => container.querySelectorAll('.model-card').length === 4);
    const names = [...container.querySelectorAll('.model-card .model-name')].map((n) => n.firstChild?.textContent);
    expect(names).toEqual(['Large v3 Turbo', 'Medium', 'Small', 'Base']);
    const turbo = container.querySelector('[data-model-id="whisper-large-v3-turbo"]');
    expect(turbo?.querySelector('.model-tag')?.textContent).toBe('Recommended');
    expect(turbo?.querySelector<HTMLInputElement>('input[type="radio"]')?.checked).toBe(true);
    expect(button('Remove Large v3 Turbo').disabled).toBe(true);
    // The CPU fallback cannot be removed either; both say why on the card.
    expect(button('Remove Small').disabled).toBe(true);
    const small = container.querySelector('[data-model-id="whisper-small"]');
    expect(turbo?.querySelector('.model-needed')?.textContent).toBe('Needed by the current settings');
    expect(small?.querySelector('.model-needed')?.textContent).toBe('Needed by the current settings');
    expect(button('Remove Small').getAttribute('aria-describedby')).toBe(small?.querySelector('.model-needed')?.id);
    expect(button('Remove Small').title).toBe('Used when there is no graphics card. Choose another model for that first.');
    const medium = container.querySelector('[data-model-id="whisper-medium"]');
    expect(medium?.querySelector<HTMLInputElement>('input[type="radio"]')?.disabled).toBe(true);

    await click(button('Install Medium, 1.4 GB'));
    await until(() => medium?.querySelector('[role="progressbar"]') !== null);
    // One download at a time.
    expect(button('Install Base, 141 MB').disabled).toBe(true);
    await until(() => document.querySelector<HTMLButtonElement>('[aria-label="Remove Medium"]')?.disabled === false);
    expect(medium?.querySelector('[role="progressbar"]')).toBeNull();
    expect(medium?.querySelector('.model-needed')).toBeNull();
    await click(medium?.querySelector('input[type="radio"]'));
    await until(() => store.settings.value?.transcription.modelId === 'whisper-medium');
    // The new default is kept; the old one can go now.
    await until(() => button('Remove Medium').disabled && !button('Remove Large v3 Turbo').disabled);
    expect(turbo?.querySelector('.model-needed')).toBeNull();
    expect((await bridge.call('engine.status')).transcription.model).toBe('whisper-medium');
  });

  it('shows the first run with no model installed, and installs one', async () => {
    await open('transcription', { modelsInstalled: 'none' });
    await until(() => container.querySelectorAll('.model-card').length === 4);
    await until(() => container.querySelector('[data-testid="engine-value"]')?.textContent === 'No model installed');
    expect(container.textContent).toContain('Install a model below');
    expect([...container.querySelectorAll('.model-card .model-facts')].every((f) => f.textContent.endsWith('Not installed'))).toBe(true);
    await click(button('Install Large v3 Turbo, 1.5 GB'));
    await until(() => document.querySelector('[aria-label="Remove Large v3 Turbo"]') !== null);
    await until(() => container.querySelector('[data-testid="engine-value"]')?.textContent === 'Local · GPU (NVIDIA GeForce RTX 4070)');
  });

  it('cancels a download and shows a refused download inline', async () => {
    await open('transcription', { models: 'noSpace' });
    await until(() => container.querySelectorAll('.model-card').length === 4);
    await click(button('Install Medium, 1.4 GB'));
    await until(() => container.querySelector('[data-model-id="whisper-medium"] .model-error') !== null);
    const error = container.querySelector('[data-model-id="whisper-medium"] .model-error');
    expect(error?.getAttribute('role')).toBe('alert');
    expect(error?.textContent).toMatch(/^Not enough space for Medium\. Medium needs 1\.4 GB and the library drive has 300 MB free\. Nothing was downloaded\./);
    await click(button('Dismiss'));
    expect(container.querySelector('.model-error')).toBeNull();

    await click(button('Install Base, 141 MB'));
    await until(() => container.querySelector('[data-model-id="whisper-base"] [role="progressbar"]') !== null);
    await click(button('Cancel the download of Base'));
    await until(() => container.querySelector('[data-model-id="whisper-base"] [role="progressbar"]') === null);
    // The host's "cancelled" failure is not shown as an error.
    expect(container.querySelector('.model-error')).toBeNull();
    expect(button('Install Base, 141 MB').disabled).toBe(false);
  });

  it('saves Speakers and lists the segmentation model apart from the voice models', async () => {
    await open('speakers');
    await until(() => container.querySelectorAll('.model-card').length === 3);
    expect(container.textContent).toContain('Stored for a later version');
    // Segmentation is needed, not a choice: no radio.
    const segmentation = container.querySelector('[data-model-id="pyannote-segmentation-3-0"]');
    expect(segmentation?.querySelector('input[type="radio"]')).toBeNull();
    expect(segmentation?.querySelector('.model-facts')?.textContent).toMatch(/Installed · needed$/);
    // Needed while Identify speakers is on, so it cannot be removed; the default voice model neither.
    expect(segmentation?.querySelector('.model-needed')?.textContent).toBe('Needed by the current settings');
    expect(button('Remove Speech segmentation (pyannote 3.0)').disabled).toBe(true);
    expect(container.querySelector('[data-model-id="nemo-titanet-small"] .model-needed')?.textContent).toBe('Needed by the current settings');
    const voices = container.querySelector('[role="radiogroup"][aria-label="Default voice model"]');
    expect([...(voices?.querySelectorAll('.model-card') ?? [])].map((c) => c.getAttribute('data-model-id'))).toEqual(['nemo-titanet-small', '3dspeaker-eres2net-base']);
    await choose('Expected speakers', '4 people');
    await click(button('Identify speakers'));
    await until(() => store.settings.value?.speakers.identify === false);
    // With speakers off the segmentation model is not needed any more.
    await until(() => segmentation?.querySelector('.model-needed') === null);
    expect(button('Remove Speech segmentation (pyannote 3.0)').disabled).toBe(false);
    expect((await bridge.call('settings.get')).speakers).toEqual({ identify: false, expectedSpeakers: 4, rememberRenamed: true, embeddingModelId: 'nemo-titanet-small' });
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
    const detail = { ready: true, device: 'GPU', gpuName: 'RTX 3060', freeVramBytes: 6 * 1024 ** 3, model: 'whisper-large-v3-turbo', paused: null };
    expect(engineWording(null)).toEqual({ value: 'Checking…', note: null });
    expect(engineWording({ transcription: detail, speakers: detail })).toEqual({ value: 'Local · GPU (RTX 3060)', note: '6 GB video memory free' });
    expect(engineWording({ transcription: { ...detail, device: 'CPU', gpuName: null, freeVramBytes: null }, speakers: detail })).toEqual({
      value: 'Local · CPU',
      note: null,
    });
    expect(engineWording({ transcription: { ...detail, paused: 'PC is busy' }, speakers: detail }).value).toBe('Paused · PC is busy');
    expect(engineWording({ transcription: { ...detail, ready: false, device: null }, speakers: detail })).toEqual({
      value: 'No model installed',
      note: 'Install a model below',
    });
  });
});
