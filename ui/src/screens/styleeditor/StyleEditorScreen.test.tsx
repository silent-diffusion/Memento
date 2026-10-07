import { afterEach, describe, expect, it } from 'vitest';
import type { SettingsSetParams, StyleSampleParams, StyleSaveParams, TemplateIdParams } from '../../bridge/types';
import { button, click, mountApp, settle, until, type Harness } from '../../testing/appHarness';
import { SAMPLE_DEBOUNCE_MS } from './StyleEditorScreen';

describe('Style editor (DESIGN.md §13, against the browser-preview host)', () => {
  let h: Harness;

  afterEach(() => {
    h.unmount();
  });

  const open = async (styleId = 'corporate'): Promise<void> => {
    h = await mountApp({ name: 'style', styleId });
    await until(() => document.querySelector('.style-paper article.paper') !== null);
  };
  const paper = (): Element | null => document.querySelector('.style-paper article.paper');

  it('shows the four cards with exactly the §13 rows and the sample page in the style', async () => {
    await open();
    expect(document.querySelector<HTMLInputElement>('#style-name')?.value).toBe('Corporate');
    expect(document.querySelector('.spoke-title-edit .pill')?.textContent).toBe('Style · used by 1 template');
    expect(document.querySelector('[data-spoke-back]')?.textContent).toBe('Settings');
    const cards = [...document.querySelectorAll('.style-card')].map((card) => [
      card.querySelector('.lbl')?.textContent,
      ...[...card.querySelectorAll('.style-row-label')].map((l) => l.textContent),
    ]);
    expect(cards).toEqual([
      ['Type', 'Headings', 'Body', 'Base size', 'Heading case', 'Numbered headings'],
      ['Colour', 'Headings and rules', 'Body text', 'Table header fill'],
      ['Structure', 'Rule under the title', 'Lines between sections', 'Spacing'],
      ['Page', 'Paper', 'Page numbers', 'Running header'],
    ]);
    expect([...document.querySelectorAll('.sw')].map((s) => [s.getAttribute('aria-label'), s.getAttribute('aria-pressed')])).toEqual([
      ['Navy', 'true'],
      ['Ink', 'false'],
      ['Forest', 'false'],
      ['Burgundy', 'false'],
    ]);
    expect(document.querySelector('.style-builtin')?.textContent).toContain('Corporate is built in.');
    expect(paper()?.className).toBe('paper paper-sample caps title-rule th-fill');
    expect(paper()?.querySelector('.paper-runhead')).not.toBeNull();
    expect(paper()?.querySelector('.paper-pagenum')?.textContent).toBe('1 of 2');
    expect(document.querySelector('.style-preview-caption')?.textContent).toContain('Preview · sample minutes · Letter');
  });

  it('asks styles.sampleHtml again after each pause in changes, with the settings as they stand', async () => {
    await open();
    const before = h.callsOf('styles.sampleHtml').length;
    await click(button('Forest'));
    await click(button('Numbered headings'));
    await click(document.querySelector('[aria-label="Paper"] .seg:not(.on)'));
    expect(h.callsOf('styles.sampleHtml')).toHaveLength(before);
    await settle(SAMPLE_DEBOUNCE_MS + 60);
    const calls = h.callsOf('styles.sampleHtml') as StyleSampleParams[];
    expect(calls).toHaveLength(before + 1);
    expect(calls.at(-1)?.settings).toMatchObject({ headingColor: 'forest', numberedHeadings: true, paper: 'a4', headingCase: 'smallCaps' });
    await until(() => paper()?.classList.contains('numbered') === true);
    expect(paper()?.getAttribute('data-paper')).toBe('a4');
    expect(document.querySelector('.style-preview-caption')?.textContent).toContain('A4');
    // Reset goes back to the saved settings.
    await click(button('Reset'));
    await settle(SAMPLE_DEBOUNCE_MS + 60);
    await until(() => paper()?.classList.contains('numbered') === false);
    expect(button('Reset').disabled).toBe(true);
  });

  it('saves a changed built-in style as a copy and opens the copy', async () => {
    await open();
    await click(button('Burgundy'));
    await click(button('Save style'));
    await until(() => h.store.route.value.name === 'style' && h.store.route.value.styleId !== 'corporate');
    const saved = (h.callsOf('styles.save') as StyleSaveParams[])[0];
    expect(saved?.style).toMatchObject({ id: 'corporate', builtIn: true });
    expect(saved?.style.settings.headingColor).toBe('burgundy');
    expect(h.store.toasts.items.value.at(-1)?.title).toBe('Saved as “Corporate (copy)”');
    await until(() => document.querySelector<HTMLInputElement>('#style-name')?.value === 'Corporate (copy)');
    expect(document.querySelector('.style-builtin')).toBeNull();
    // The built-in is unchanged.
    expect((await h.bridge.call('styles.get', { styleId: 'corporate' })).settings.headingColor).toBe('navy');
  });

  it('asks before leaving with unsaved changes', async () => {
    await open('minimal');
    await click(button('Serif'));
    await click(document.querySelector('[data-spoke-back]'));
    expect(document.querySelector('#leave-style-title')?.textContent).toBe('Leave Minimal without saving?');
    await click(button('Keep editing'));
    expect(h.store.route.value.name).toBe('style');
    await click(document.querySelector('[data-spoke-back]'));
    await click(button('Discard changes'));
    expect(h.store.route.value).toEqual({ name: 'settings', section: 'documents' });
  });
});

describe('Settings › Documents (M4)', () => {
  let h: Harness;

  afterEach(() => {
    h.unmount();
  });

  it('makes the defaults live and manages templates and styles, refusing to delete built-ins', async () => {
    h = await mountApp({ name: 'settings', section: 'documents' });
    await until(() => document.querySelector('[aria-label="Default template: Meeting minutes"]') !== null);
    await click(document.querySelector('[aria-label="Default style: Corporate"]'));
    await click([...document.querySelectorAll('[role="option"]')].find((o) => o.textContent.trim() === 'Academic'));
    expect((h.callsOf('settings.set') as SettingsSetParams[]).at(-1)?.documents).toEqual({ defaultStyleId: 'academic' });
    await click(button('Manage'));
    await until(() => document.querySelectorAll('.manager-row').length === 7);
    const rows = [...document.querySelectorAll('.manager-row')].map((r) => r.querySelector('.manager-meta')?.textContent);
    expect(rows[0]).toBe('9 modules · Corporate · Built in');
    expect(rows[4]).toBe('Used by 1 template · Built in');
    // Built-ins offer Reset instead of Delete.
    expect(document.querySelector('[aria-label="Delete Meeting minutes"]')).toBeNull();
    await click(button('Duplicate Meeting minutes'));
    await until(() => document.querySelectorAll('.manager-row').length === 8);
    expect((h.callsOf('templates.duplicate') as TemplateIdParams[])[0]?.templateId).toBe('meeting-minutes');
    await click(button('Delete Meeting minutes (copy)'));
    expect(document.querySelector('.manager-confirm')?.textContent).toBe('Delete Meeting minutes (copy)?');
    await click([...document.querySelectorAll<HTMLButtonElement>('.manager-row .btn.d')][0]);
    await until(() => document.querySelectorAll('.manager-row').length === 7);
    await click(button('Open Minimal in the style editor'));
    expect(h.store.route.value).toEqual({ name: 'style', styleId: 'minimal' });
  });

  it('opens a template in the Builder without a recording', async () => {
    h = await mountApp({ name: 'settings', section: 'documents' });
    await until(() => document.querySelector('[aria-label="Default template: Meeting minutes"]') !== null);
    await click(button('Manage'));
    await until(() => document.querySelectorAll('.manager-row').length === 7);
    await click(button('Open Interview notes in the builder'));
    expect(h.store.route.value).toEqual({ name: 'builder', recordingId: null, templateId: 'interview-notes', documentId: null });
    await until(() => document.querySelector('.builder-paper article.paper') !== null);
    expect(document.querySelector('[data-spoke-back]')?.textContent).toBe('Settings');
    expect(document.querySelector('.builder-paper .paper-title')?.textContent).toBe('Sample recording');
    expect([...document.querySelectorAll('.spoke-actions button')].map((b) => b.textContent)).toEqual(['Save template']);
  });
});
