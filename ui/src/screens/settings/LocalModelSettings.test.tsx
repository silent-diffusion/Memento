import { afterEach, describe, expect, it } from 'vitest';
import type { MockOptions } from '../../bridge/mock';
import type { SettingsSetParams } from '../../bridge/types';
import { button, click, mountApp, settle, until, type Harness } from '../../testing/appHarness';

const DESIGN = '20261005-160000-dsrev';

interface Card {
  id: string;
  name: string;
  checked: boolean;
  disabled: boolean;
  text: string;
}

/**
 * The local model in Settings › AI and privacy and in the Builder (1.1.0): every local model with its install state and
 * a "Use this model" radio, the model in effect always an installed one when any is, and both screens naming the model
 * that will write and where it runs.
 */
describe('Local model readiness in Settings and the Builder (against the browser-preview host)', () => {
  let h: Harness;

  afterEach(() => {
    h.unmount();
  });

  const openSettings = async (m4: NonNullable<MockOptions['m4']>): Promise<void> => {
    h = await mountApp({ name: 'settings', section: 'ai-privacy' }, { m4 });
    await until(() => document.querySelectorAll('[data-model-id^="qwen"], [data-model-id^="ministral"]').length === 2 && document.querySelector('.model-in-use') !== null);
  };
  const cards = (): Card[] =>
    [...document.querySelectorAll<HTMLElement>('.model-card')]
      .filter((c) => /^(qwen|ministral)/.test(c.dataset.modelId ?? ''))
      .map((c) => {
        const radio = c.querySelector<HTMLInputElement>('input[type="radio"]');
        return {
          id: c.dataset.modelId ?? '',
          name: c.querySelector('.model-name')?.firstChild?.textContent ?? '',
          checked: radio?.checked === true,
          disabled: radio?.disabled === true,
          text: c.textContent.replace(/\s+/g, ' '),
        };
      });
  const inUse = (): string => (document.querySelector('.model-in-use')?.textContent ?? '').replace(/\s+/g, ' ');
  const gpuLine = (): string => (document.querySelector('[data-testid="gpu-memory"] .gpu-memory-text')?.textContent ?? '').replace(/\s+/g, ' ');

  it('with only Qwen installed and the graphics card busy, Qwen is selected and runs on the processor', async () => {
    await openSettings({ llm: 'qwen', vram: 'low' });
    const [qwen, ministral] = cards();
    expect(qwen).toMatchObject({ id: 'qwen3.5-4b-q4', checked: true, disabled: false });
    expect(qwen?.text).toContain('Installed · selected');
    expect(qwen?.text).toContain('Use this model');
    expect(qwen?.text).not.toContain('Not installed');
    expect(ministral).toMatchObject({ id: 'ministral-3-3b-q4', checked: false, disabled: true });
    expect(ministral?.text).toContain('Not installed');
    expect(ministral?.text).not.toContain('Use this model');
    expect(inUse()).toContain('Documents are written with Qwen3.5 4B · processor.');
    expect(inUse()).not.toContain('graphics card has');
    expect(gpuLine()).toContain('Ollama (llama-server.exe) is using 9.4 GB');
    expect(gpuLine()).toContain('so it runs on the processor (several times slower) until that memory is free.');
    expect(document.querySelector('.model-in-use')?.getAttribute('data-local-ready')).toBe('true');
  });

  it('with only Ministral installed, Ministral is selected even when the card has room for Qwen', async () => {
    await openSettings({ llm: 'ministral' });
    const [qwen, ministral] = cards();
    expect(qwen).toMatchObject({ checked: false, disabled: true });
    expect(qwen?.text).toContain('Not installed');
    expect(ministral).toMatchObject({ checked: true, disabled: false });
    expect(inUse()).toContain('Documents are written with Ministral 3 3B · graphics card.');
  });

  it('with both installed, the radio chooses and the line follows; a busy card hands the writing to Ministral', async () => {
    await openSettings({ llm: 'both', vram: 'low' });
    expect(cards().map((c) => c.checked)).toEqual([false, true]);
    expect(inUse()).toContain('Ministral 3 3B · processor');

    const qwenRadio = document.querySelector<HTMLInputElement>('[data-model-id="qwen3.5-4b-q4"] input[type="radio"]');
    await click(qwenRadio);
    await until(() => cards()[0]?.checked === true && gpuLine().includes('so Ministral 3 3B writes instead until that memory is free.'));
    expect((h.callsOf('settings.set') as SettingsSetParams[]).at(-1)?.ai?.localModelId).toBe('qwen3.5-4b-q4');
    expect(h.store.settings.value?.ai.localModelChosen).toBe(true);
    expect(inUse()).toContain('Documents are written with Ministral 3 3B · processor.');
  });

  it('with no local model installed, the line says which one to download, and an install makes it the one in use', async () => {
    await openSettings({ llm: 'none', vram: 'low' });
    expect(cards().every((c) => !c.text.includes('Installed'))).toBe(true);
    expect(document.querySelector('.model-in-use')?.getAttribute('data-local-ready')).toBe('false');
    expect(inUse()).toContain('Model not installed.');
    expect(inUse()).toContain('The local model Ministral 3 3B is not installed.');

    await click(document.querySelector('[aria-label^="Install Qwen3.5 4B"]'));
    await until(() => cards()[0]?.text.includes('Installed') === true, 15_000);
    await until(() => cards()[0]?.checked === true && inUse().includes('Qwen3.5 4B · processor'), 15_000);
    expect(h.store.settings.value?.ai.localModelId).toBe('qwen3.5-4b-q4');
  });

  it('the Builder names the installed model and where it runs on the Local provider card', async () => {
    h = await mountApp({ name: 'builder', recordingId: DESIGN, templateId: null, documentId: null }, { m4: { llm: 'qwen', vram: 'low' } });
    await until(() => document.querySelector('.mod') !== null);
    await click(button('Inputs and output'));
    await until(() => document.querySelector('.providers .provider') !== null);
    const local = [...document.querySelectorAll<HTMLElement>('.providers label.provider')].find((l) => l.querySelector('.provider-name')?.textContent === 'Local model');
    expect(local?.querySelector('.provider-note')?.textContent).toBe('Qwen3.5 4B · processor · on this PC');
    expect(local?.querySelector('input')?.disabled).toBe(false);
    expect(local?.querySelector('.provider-detail')?.textContent).toBe(
      'The graphics card has 2.0 GB of 12 GB free. Ollama (llama-server.exe) is using 9.4 GB and Windows desktop (dwm.exe) 0.4 GB. Qwen3.5 4B needs 3.4 GB on the card, '
        + 'so it runs on the processor (several times slower) until that memory is free. To use the card, close Ollama (llama-server.exe) or wait until it lets go of the memory, then check again.',
    );
    await settle(10);
    expect(button('Generate minutes').disabled).toBe(false);
  });

  it('the Builder adds no card sentence when the local model fits on the card', async () => {
    h = await mountApp({ name: 'builder', recordingId: DESIGN, templateId: null, documentId: null }, { m4: { llm: 'qwen' } });
    await until(() => document.querySelector('.mod') !== null);
    await click(button('Inputs and output'));
    await until(() => document.querySelector('.providers .provider') !== null);
    expect(document.querySelector('.provider-detail')).toBeNull();
    expect(document.querySelector('.provider--detail')).toBeNull();
  });

  it('Settings shows the card with nothing to fix when the model fits, and Check again reads the card and the models again', async () => {
    await openSettings({ llm: 'qwen' });
    expect(inUse()).toContain('Documents are written with Qwen3.5 4B · graphics card.');
    expect(gpuLine()).toBe('The graphics card has 9.2 GB of 12 GB free. Windows desktop (dwm.exe) is using 0.4 GB.');
    expect(document.querySelector('.gpu-memory--short')).toBeNull();

    const before = { refresh: h.callsOf('engine.refresh').length, providers: h.callsOf('providers.list').length, settings: h.callsOf('settings.get').length, models: h.callsOf('models.list').length };
    await click(button('Check again'));
    await until(() => h.callsOf('providers.list').length > before.providers && document.querySelector<HTMLButtonElement>('.gpu-memory-check')?.disabled === false);
    expect(h.callsOf('engine.refresh').length).toBe(before.refresh + 1);
    expect(h.callsOf('settings.get').length).toBeGreaterThan(before.settings);
    expect(h.callsOf('models.list').length).toBeGreaterThan(before.models);
  });

  it('a busy card is shown as a warning with the fix', async () => {
    await openSettings({ llm: 'qwen', vram: 'low' });
    expect(document.querySelector('.gpu-memory--short')).not.toBeNull();
    expect(gpuLine()).toMatch(/^The graphics card has 2\.0 GB of 12 GB free\. Ollama \(llama-server\.exe\) is using 9\.4 GB .* then check again\.$/);
  });
});
