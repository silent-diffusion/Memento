import { afterEach, describe, expect, it } from 'vitest';
import type { SettingsSetParams } from '../../bridge/types';
import { click, mountApp, settle, until, type Harness } from '../../testing/appHarness';

/**
 * Settings › Documents › Agenda photos and scans (2.0): Windows OCR is the only choice until Tesseract English is
 * installed from its card (download, check, Remove); then a segmented control chooses, and Remove is off while
 * Tesseract is in use.
 */
describe('Agenda text recognition in Settings › Documents (against the browser-preview host)', () => {
  let h: Harness;

  afterEach(() => {
    h.unmount();
  });

  const group = (): HTMLElement | null =>
    [...document.querySelectorAll<HTMLElement>('.settings-group')].find((g) => g.textContent.includes('Agenda photos and scans')) ?? null;
  const card = (): HTMLElement | null => document.querySelector<HTMLElement>('[data-model-id="tesseract-eng"]');
  const cardText = (): string => (card()?.textContent ?? '').replace(/\s+/g, ' ');
  const segment = (name: string): HTMLButtonElement | null =>
    [...(group()?.querySelectorAll<HTMLButtonElement>('.seg') ?? [])].find((b) => b.textContent === name) ?? null;
  const removeButton = (): HTMLButtonElement | null => card()?.querySelector<HTMLButtonElement>('[aria-label="Remove Tesseract English"]') ?? null;

  it('offers only Windows OCR until Tesseract is installed, then lets it be chosen and kept while in use', async () => {
    h = await mountApp({ name: 'settings', section: 'documents' });
    await until(() => card() !== null);

    expect(group()?.querySelector('.settings-value')?.textContent).toBe('Windows OCR');
    expect(group()?.querySelector('.seg-well')).toBeNull();
    expect(cardText()).toContain('Not installed');
    expect(cardText()).toContain('Apache-2.0');
    expect(h.store.settings.value?.documents.ocrEngine).toBe('windows');

    await click(card()?.querySelector('[aria-label^="Install Tesseract English"]'));
    await until(() => cardText().includes('Installed') && segment('Tesseract') !== null, 15_000);
    expect(segment('Windows OCR')?.getAttribute('aria-pressed')).toBe('true');
    expect(removeButton()?.disabled).toBe(false);

    await click(segment('Tesseract'));
    await until(() => segment('Tesseract')?.getAttribute('aria-pressed') === 'true');
    expect((h.callsOf('settings.set') as SettingsSetParams[]).at(-1)?.documents).toEqual({ ocrEngine: 'tesseract' });
    expect(h.store.settings.value?.documents.ocrEngine).toBe('tesseract');
    expect(cardText()).toContain('Installed · in use');
    expect(cardText()).toContain('Needed by the current settings');
    expect(removeButton()?.disabled).toBe(true);
    expect(removeButton()?.title).toBe('Agenda photos and scans are read with it. Choose Windows OCR first.');

    await click(segment('Windows OCR'));
    await until(() => removeButton()?.disabled === false);
    expect(h.store.settings.value?.documents.ocrEngine).toBe('windows');
  });

  it('removing Tesseract puts Windows OCR back as the only choice', async () => {
    h = await mountApp({ name: 'settings', section: 'documents' });
    await until(() => card() !== null);
    await click(card()?.querySelector('[aria-label^="Install Tesseract English"]'));
    await until(() => segment('Tesseract') !== null, 15_000);

    await click(removeButton());
    await until(() => cardText().includes('Not installed') && segment('Tesseract') === null);
    await settle(20);
    expect(group()?.querySelector('.settings-value')?.textContent).toBe('Windows OCR');
    expect(h.store.settings.value?.documents.ocrEngine).toBe('windows');
  });

  it('the host refuses Tesseract while it is not installed and the row says why', async () => {
    h = await mountApp({ name: 'settings', section: 'documents' });
    await until(() => card() !== null);

    await expect(h.bridge.call('settings.set', { documents: { ocrEngine: 'tesseract' } })).rejects.toMatchObject({
      code: 'settings.invalidValue',
      detail: 'documents.ocrEngine',
    });
    expect(h.store.settings.value?.documents.ocrEngine).toBe('windows');
  });
});
