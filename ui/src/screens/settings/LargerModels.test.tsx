import { afterEach, describe, expect, it } from 'vitest';
import type { MockOptions } from '../../bridge/mock';
import type { SettingsSetParams } from '../../bridge/types';
import { button, click, mountApp, settle, until, type Harness } from '../../testing/appHarness';

const DESIGN = '20261005-160000-dsrev';
const MOE = 'qwen3.5-35b-a3b-q4';
const DENSE = 'qwen3.5-27b-q4';

/**
 * 2.2, the larger models (DESIGN.md §11 "Larger models", BRIDGE.md "Larger local models"): Settings › AI and privacy lists
 * them under their own label below the small models, each with its size, licence, memory and the plan line from
 * providers.list; "Use this model" makes one the model in effect; the Builder's Local card names it with its placement
 * and the plan line.
 */
describe('Larger models in Settings and the Builder (against the browser-preview host)', () => {
  let h: Harness;

  afterEach(() => {
    h.unmount();
  });

  const openSettings = async (m4: NonNullable<MockOptions['m4']>): Promise<void> => {
    h = await mountApp({ name: 'settings', section: 'ai-privacy' }, { m4 });
    await until(() => document.querySelectorAll('.model-group .model-card').length === 2 && document.querySelectorAll('.model-plan').length === 2);
  };
  const card = (id: string): HTMLElement | null => document.querySelector<HTMLElement>(`.model-group [data-model-id="${id}"]`);
  const text = (el: Element | null | undefined): string => (el?.textContent ?? '').replace(/\s+/g, ' ').trim();

  it('lists the larger models under their own label below the small ones, with the plan line and the download size', async () => {
    await openSettings({ llm: 'qwen' });
    const group = document.querySelector('.model-group');
    expect(text(group?.querySelector('.lbl'))).toBe('Larger models');
    expect(group?.getAttribute('role')).toBe('group');
    expect(text(group?.querySelector('.model-group-desc'))).toContain('They use the computer’s RAM beside the graphics card'.replace('’', "'"));
    expect(text(group?.querySelector('.model-group-desc'))).toContain('a 16–21 GB download that continues where it stopped, checked against the free space first');

    // The small models stay where they were, without a plan line.
    const small = [...document.querySelectorAll<HTMLElement>('.model-card')].filter((c) => c.closest('.model-group') === null && /^(qwen3\.5-4b|ministral)/.test(c.dataset.modelId ?? ''));
    expect(small.map((c) => c.dataset.modelId)).toEqual(['qwen3.5-4b-q4', 'ministral-3-3b-q4']);
    expect(small.every((c) => c.querySelector('.model-plan') === null)).toBe(true);

    expect(text(card(DENSE)?.querySelector('.model-facts'))).toMatch(/^15\.6 GB · Apache-2\.0 · Graphics card and RAM, 17\.\d GB of memory · Not installed$/);
    expect(text(card(DENSE)?.querySelector('.model-plan'))).toBe('Runs 15 of 64 layers on the graphics card, the rest in RAM · about 3 tokens/s · 14 GB of RAM while writing');
    expect(text(card(MOE)?.querySelector('.model-plan'))).toBe('Runs on the graphics card with the experts of 37 of 40 layers in RAM · about 12 tokens/s · 18 GB of RAM while writing');
    expect(text(card(MOE)?.querySelector('.model-note'))).toBe('Mixture of experts: fast for its size');
    expect(card(MOE)?.querySelector('[aria-label="Install Qwen3.5 35B-A3B, 20.5 GB"]')).not.toBeNull();
    expect(text(card(MOE)?.querySelector('.model-actions'))).toBe('Install · 20.5 GB');
    expect(card(MOE)?.querySelector('.model-plan--short')).toBeNull();
  });

  it('with the card held by another program the plan moves to RAM', async () => {
    await openSettings({ llm: 'qwen', vram: 'low' });
    expect(text(card(MOE)?.querySelector('.model-plan'))).toBe('Runs in RAM on the processor · about 6 tokens/s · 22 GB of RAM while writing');
  });

  it('Use this model makes an installed larger model the one in effect, in one radio group with the small ones', async () => {
    await openSettings({ llm: 'moe' });
    const radios = [...document.querySelectorAll<HTMLInputElement>('.model-card input[type="radio"]')].filter((r) => /^(qwen|ministral)/.test(r.closest<HTMLElement>('.model-card')?.dataset.modelId ?? ''));
    expect(new Set(radios.map((r) => r.name))).toEqual(new Set(['default-llm']));
    const moeRadio = card(MOE)?.querySelector<HTMLInputElement>('input[type="radio"]') ?? null;
    expect(moeRadio?.disabled).toBe(false);
    expect(text(card(MOE))).toContain('Use this model');
    expect(card(DENSE)?.querySelector<HTMLInputElement>('input[type="radio"]')?.disabled).toBe(true);

    await click(moeRadio);
    await until(() => (h.callsOf('settings.set') as SettingsSetParams[]).some((c) => c.ai?.localModelId === MOE));
    await until(() => text(document.querySelector('.model-in-use')).includes('Qwen3.5 35B-A3B · graphics card and RAM'));
    expect(text(document.querySelector('.model-in-use'))).toContain('Runs on the graphics card with the experts of 37 of 40 layers in RAM');
    expect(moeRadio?.checked).toBe(true);
    expect(card(MOE)?.querySelector<HTMLInputElement>('input[type="radio"]')?.checked).toBe(true);
    expect(document.querySelector<HTMLInputElement>('[data-model-id="qwen3.5-4b-q4"] input[type="radio"]')?.checked).toBe(false);
  });

  it('the Builder names the larger model, where it runs and the plan on the Local card', async () => {
    h = await mountApp({ name: 'builder', recordingId: DESIGN, templateId: null, documentId: null }, { m4: { llm: 'moeOnly' } });
    await until(() => document.querySelector('.mod') !== null);
    await click(button('Inputs and output'));
    await until(() => document.querySelector('.providers .provider') !== null);
    const local = [...document.querySelectorAll<HTMLElement>('.providers label.provider')].find((l) => l.querySelector('.provider-name')?.textContent === 'Local model');
    expect(local?.querySelector('.provider-note')?.textContent).toBe('Qwen3.5 35B-A3B · graphics card and RAM · on this PC');
    const plan = local?.querySelector('.provider-detail--plan');
    expect(plan?.textContent).toBe('Runs on the graphics card with the experts of 37 of 40 layers in RAM · about 12 tokens/s · 18 GB of RAM while writing');
    expect(local?.classList.contains('provider--detail')).toBe(true);
    await settle(10);
    expect(button('Generate minutes').disabled).toBe(false);
  });
});
