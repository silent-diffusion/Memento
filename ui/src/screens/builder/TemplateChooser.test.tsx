import { afterEach, describe, expect, it } from 'vitest';
import type { GenerationStartParams, Template, TemplateSaveParams } from '../../bridge/types';
import { button, click, mountApp, settle, type, until, type Harness } from '../../testing/appHarness';
import { BUILT_IN_GROUP, SAVED_GROUP, templateOptions } from './TemplateChooser';

const DESIGN = '20261005-160000-dsrev';

/**
 * The Builder's template list (1.1.0): built-ins first, then saved templates; choosing one opens it; Save template and
 * Save as new template put the arrangement in the list, where it can be chosen again after the Builder is reopened.
 */
describe('Builder template list (against the browser-preview host)', () => {
  let h: Harness;

  afterEach(() => {
    h.unmount();
  });

  const name = (): string => document.querySelector<HTMLInputElement>('#tpl-name')?.value ?? '';
  const chooser = (): HTMLButtonElement => {
    const el = document.querySelector<HTMLButtonElement>('.tpl-chooser button[aria-haspopup="listbox"]');
    if (el === null) {
      throw new Error('no template list');
    }
    return el;
  };
  const listed = async (): Promise<string[]> => {
    await click(chooser());
    const items = [...document.querySelectorAll('.tpl-chooser [role="listbox"] li')].map((li) => (li.getAttribute('role') === 'presentation' ? `# ${li.textContent}` : li.textContent));
    await click(chooser());
    return items;
  };
  const choose = async (label: string): Promise<void> => {
    await click(chooser());
    const option = [...document.querySelectorAll<HTMLElement>('.tpl-chooser [role="option"]')].find((o) => o.textContent === label);
    await click(option);
  };
  const open = async (): Promise<void> => {
    h = await mountApp({ name: 'builder', recordingId: DESIGN, templateId: null, documentId: null }, { m4: { ai: 'local' } });
    await until(() => name() === 'Meeting minutes' && document.querySelector('.mod') !== null);
    await until(() => chooser().getAttribute('aria-label') === 'Template: Meeting minutes');
  };
  const reopen = async (): Promise<void> => {
    // Back to Review (which drops the Builder's draft), then Create document again.
    await click(document.querySelector('[data-spoke-back]'));
    await until(() => h.store.route.value.name === 'review');
    await settle(30);
    h.store.route.value = { name: 'builder', recordingId: DESIGN, templateId: null, documentId: null };
    await until(() => name() === 'Meeting minutes' && document.querySelector('.tpl-chooser') !== null);
  };

  it('lists the built-in templates first, and choosing one opens it', async () => {
    await open();
    expect(await listed()).toEqual([`# ${BUILT_IN_GROUP}`, 'Meeting minutes', 'Interview notes', 'Lecture summary', 'Dictation clean-up']);

    await choose('Interview notes');
    await until(() => name() === 'Interview notes');
    expect(chooser().getAttribute('aria-label')).toBe('Template: Interview notes');
    expect(document.querySelector('.spoke-title-edit .pill')?.textContent).not.toBe('Template · 9 modules');
  });

  it('a saved template is listed under Your templates, survives reopening the Builder, and generates with its id', async () => {
    await open();
    await type(document.querySelector('#tpl-name'), 'Board minutes');
    await click(button('Save template'));
    await until(() => chooser().getAttribute('aria-label') === 'Template: Board minutes');
    const saved = h.callsOf('templates.save') as TemplateSaveParams[];
    expect(saved).toHaveLength(1);
    expect(saved[0]?.template.id).toBe('meeting-minutes');
    expect(await listed()).toEqual([`# ${BUILT_IN_GROUP}`, 'Meeting minutes', 'Interview notes', 'Lecture summary', 'Dictation clean-up', `# ${SAVED_GROUP}`, 'Board minutes']);

    await reopen();
    expect((await listed()).at(-1)).toBe('Board minutes');
    await choose('Board minutes');
    await until(() => name() === 'Board minutes');
    await click(button('Generate minutes'));
    await until(() => h.callsOf('generation.start').length === 1);
    const start = h.callsOf('generation.start')[0] as GenerationStartParams;
    expect(start.template.name).toBe('Board minutes');
    expect(start.template.id).not.toBe('meeting-minutes');
    expect(start.template.builtIn).toBe(false);
  });

  it('Save as new template always makes another template, with a name of its own', async () => {
    await open();
    await click(button('Save template'));
    await until(() => chooser().getAttribute('aria-label') === 'Template: Meeting minutes (copy)');
    await click(button('Save as new template'));
    await until(() => chooser().getAttribute('aria-label') === 'Template: Meeting minutes (copy) (copy)');
    const saves = h.callsOf('templates.save') as TemplateSaveParams[];
    expect(saves.map((s) => s.template.id === '')).toEqual([false, true]);
    expect((await listed()).slice(-2)).toEqual(['Meeting minutes (copy)', 'Meeting minutes (copy) (copy)']);
  });

  it('asks before switching away from changes that are not saved', async () => {
    await open();
    await type(document.querySelector('#tpl-name'), 'Minutes, changed');
    await choose('Lecture summary');
    await until(() => document.querySelector('.tpl-confirm[role="alertdialog"]') !== null);
    expect(document.querySelector('.tpl-confirm')?.textContent).toContain('The changes to “Minutes, changed” are not saved');
    await click(button('Keep editing'));
    expect(document.querySelector('.tpl-confirm')).toBeNull();
    expect(name()).toBe('Minutes, changed');

    await choose('Lecture summary');
    await click(button('Open Lecture summary'));
    await until(() => name() === 'Lecture summary');
    expect(h.callsOf('templates.get')).toContainEqual({ templateId: 'lecture-summary' });
  });
});

describe('templateOptions', () => {
  const t = (id: string, builtIn: boolean): Template => ({ id, name: id.toUpperCase(), builtIn }) as unknown as Template;

  it('puts the built-ins first and keeps an open template that is no longer listed', () => {
    expect(templateOptions([t('mine', false), t('minutes', true)], 'gone', 'Gone')).toEqual([
      { value: 'minutes', label: 'MINUTES', group: BUILT_IN_GROUP },
      { value: 'mine', label: 'MINE', group: SAVED_GROUP },
      { value: 'gone', label: 'Gone', group: SAVED_GROUP },
    ]);
  });
});
