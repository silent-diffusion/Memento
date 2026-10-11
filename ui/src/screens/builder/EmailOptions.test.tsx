import { afterEach, describe, expect, it } from 'vitest';
import type { DocumentModuleParams, GenerationPreviewParams, GenerationStartParams, ModuleSettings, TemplateSaveParams } from '../../bridge/types';
import { button, click, mountApp, settle, type, until, type Harness } from '../../testing/appHarness';
import { DEFAULT_EMAIL, EMAIL_TONES, emailOf, TONE_NOTES } from './EmailOptions';

const DESIGN = '20261005-160000-dsrev';

/**
 * The Follow-up email module's settings (2.0): tone (neutral, warm, brief) and sign-off name on its expanded card, each
 * an Undo step, carried in the template to the preview, the generation and Save template; the email it writes follows
 * them, and the viewer copies it on its own.
 */
describe('Follow-up email settings in the builder (against the browser-preview host)', () => {
  let h: Harness;

  afterEach(() => {
    h.unmount();
  });

  const card = (): HTMLElement | null => [...document.querySelectorAll<HTMLElement>('.mod')].find((m) => m.querySelector('.mod-name')?.textContent === 'Follow-up email') ?? null;
  const tone = (label: string): HTMLButtonElement | null =>
    [...(card()?.querySelectorAll<HTMLButtonElement>('.mod-email .seg') ?? [])].find((b) => b.textContent === label) ?? null;
  const signOff = (): HTMLInputElement | null => card()?.querySelector<HTMLInputElement>('.mod-email input[type="text"]') ?? null;
  const emailIn = (modules: readonly ModuleSettings[] | undefined): ModuleSettings | undefined => modules?.find((m) => m.module === 'followUpEmail');
  const undoLabel = (): string | null => document.querySelector('.undo-btn')?.getAttribute('aria-label') ?? null;

  const open = async (): Promise<void> => {
    h = await mountApp({ name: 'builder', recordingId: DESIGN, templateId: null, documentId: null }, { m4: { ai: 'local' } });
    await until(() => document.querySelector('.builder-paper article.paper') !== null && document.querySelector('.mod') !== null);
    await click(document.querySelector('.pal[data-module="followUpEmail"]'));
    await until(() => card()?.querySelector('.mod-email') !== null);
  };

  it('offers a tone and a sign-off on the expanded card, neutral and unsigned at first', async () => {
    await open();
    const group = card()?.querySelector('.mod-email');
    expect(group?.getAttribute('role')).toBe('group');
    expect(group?.getAttribute('aria-label')).toBe('Follow-up email settings');
    expect([...(group?.querySelectorAll('.seg') ?? [])].map((b) => b.textContent)).toEqual(['Neutral', 'Warm', 'Brief']);
    expect(tone('Neutral')?.getAttribute('aria-pressed')).toBe('true');
    expect(group?.querySelector('.mod-note')?.textContent).toBe(TONE_NOTES.neutral);
    expect(signOff()?.value).toBe('');
    expect(signOff()?.maxLength).toBe(80);
    expect(card()?.querySelector('label.mod-field .lbl')?.textContent).toBe('Heading');
  });

  it('changes the tone and the sign-off as Undo steps, and the template carries them', async () => {
    await open();
    await click(tone('Warm'));
    expect(tone('Warm')?.getAttribute('aria-pressed')).toBe('true');
    expect(card()?.querySelector('.mod-email .mod-note')?.textContent).toBe(TONE_NOTES.warm);
    expect(undoLabel()).toBe('Undo change Follow-up email tone');

    await type(signOff(), 'Da');
    await type(signOff(), 'Dana');
    expect(undoLabel()).toBe('Undo change Follow-up email sign-off');
    await click(document.querySelector('.undo-btn'));
    await until(() => signOff()?.value === '');
    expect(tone('Warm')?.getAttribute('aria-pressed')).toBe('true');
    await type(signOff(), 'Dana Okafor');
    expect(signOff()?.value).toBe('Dana Okafor');

    await click(button('Preview exactly what will be sent'));
    await until(() => h.callsOf('generation.preview').length > 0);
    const previewed = (h.callsOf('generation.preview') as GenerationPreviewParams[]).at(-1);
    expect(emailIn(previewed?.template.rows.flatMap((r) => r.modules))?.email).toEqual({ tone: 'warm', signOff: 'Dana Okafor' });
    await click(button('Done'));

    await click(button('Save as new template'));
    await until(() => h.callsOf('templates.save').length > 0);
    const saved = (h.callsOf('templates.save') as TemplateSaveParams[]).at(-1);
    expect(emailIn(saved?.template.rows.flatMap((r) => r.modules))?.email).toEqual({ tone: 'warm', signOff: 'Dana Okafor' });
  });

  it('writes the email in its tone, signed, and the viewer copies it on its own', async () => {
    await open();
    await click(tone('Brief'));
    await type(signOff(), 'Dana');
    await settle(20);
    await click(button('Generate minutes'));
    await until(() => h.store.route.value.name === 'document', 10_000);
    const started = (h.callsOf('generation.start') as GenerationStartParams[]).at(-1);
    expect(emailIn(started?.template.rows.flatMap((r) => r.modules))?.email).toEqual({ tone: 'brief', signOff: 'Dana' });

    await until(() => document.querySelector('.doc-paper article.paper, article.paper section[data-module="followUpEmail"]') !== null, 10_000);
    const email = document.querySelector('article.paper section[data-module="followUpEmail"]');
    expect(email?.textContent).toContain('Subject');
    expect(email?.textContent).toContain('Recap:');
    expect(email?.textContent).toContain('Thanks,');
    expect(email?.textContent).toContain('Dana');
    expect(email?.textContent).not.toContain('Best regards');

    await click(document.querySelector('[aria-label="More actions"]'));
    await until(() => [...document.querySelectorAll('[role="menuitem"]')].some((i) => i.textContent.includes('Copy the follow-up email')));
    const item = [...document.querySelectorAll<HTMLElement>('[role="menuitem"]')].find((i) => i.textContent.includes('Copy the follow-up email'));
    await click(item);
    await until(() => h.callsOf('documents.copyModule').length > 0);
    const copied = (h.callsOf('documents.copyModule') as DocumentModuleParams[]).at(-1);
    expect(copied?.moduleId).toBe(email?.getAttribute('data-id'));
    await until(() => document.body.textContent.includes('Copied the follow-up email'));
  });

  it('defaults and words', () => {
    expect(EMAIL_TONES.map((t) => t.value)).toEqual(['neutral', 'warm', 'brief']);
    expect(emailOf({ id: 'e', module: 'followUpEmail', instructions: '', length: 'medium', textSize: 'normal', linkToTranscript: false, customTitle: null, customText: null })).toEqual(DEFAULT_EMAIL);
    expect(Object.values(TONE_NOTES).join(' ')).not.toContain('!');
  });
});
