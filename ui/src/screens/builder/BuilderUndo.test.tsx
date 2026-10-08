import { act } from 'preact/test-utils';
import { afterEach, describe, expect, it } from 'vitest';
import { undoOf } from '../../state/undo';
import { button, click, mountApp, settle, type, until, type Harness } from '../../testing/appHarness';
import { MERGE_WINDOW_MS } from '../../state/undo';
import { structureStep, templateStep } from './builderUndo';

const DESIGN = '20261005-160000-dsrev';

describe('Undo in the document builder (one stack with the rest of the app)', () => {
  let h: Harness;

  afterEach(() => {
    h.unmount();
  });

  const open = async (): Promise<void> => {
    h = await mountApp({ name: 'builder', recordingId: DESIGN, templateId: null, documentId: null }, { m4: { ai: 'ready' } });
    await until(() => document.querySelector('.builder-paper article.paper') !== null && document.querySelector('.mod') !== null);
  };
  const order = (): string[] => [...document.querySelectorAll('.structure-row')].map((row) => [...row.querySelectorAll('.mod-name')].map((n) => n.textContent).join(' | '));
  const undoButton = (): HTMLButtonElement | null => document.querySelector<HTMLButtonElement>('.undo-btn');
  const key = async (init: KeyboardEventInit, target: Element = document.body): Promise<void> => {
    await act(async () => {
      target.dispatchEvent(new KeyboardEvent('keydown', { bubbles: true, cancelable: true, ...init }));
      await Promise.resolve();
    });
    await settle(10);
  };
  const card = (name: string): HTMLElement => {
    const found = [...document.querySelectorAll<HTMLElement>('.mod')].find((m) => m.querySelector('.mod-name')?.textContent === name);
    if (found === undefined) {
      throw new Error(`no card ${name}`);
    }
    return found;
  };

  it('undoes and redoes adding, moving and removing modules with the button and the keys', async () => {
    await open();
    const start = order();
    expect(undoButton()).toBeNull();

    await click(button('Remove Agenda'));
    expect(order()).not.toContain('Agenda');
    expect(undoButton()?.getAttribute('aria-label')).toBe('Undo remove Agenda');
    await click(undoButton());
    await until(() => order().includes('Agenda'));
    expect(order()).toEqual(start);
    expect(document.querySelector('.undo-status')?.textContent).toBe('Undone: remove Agenda');
    await key({ key: 'y', ctrlKey: true });
    await until(() => !order().includes('Agenda'));
    await key({ key: 'z', ctrlKey: true });
    await until(() => order().includes('Agenda'));

    await click(button('Move Agenda up'));
    expect(order().indexOf('Agenda')).toBe(start.indexOf('Agenda') - 1);
    expect(undoButton()?.getAttribute('aria-label')).toBe('Undo move Agenda');
    await click(document.querySelector('.pal[data-module="quote"]'));
    expect(order().at(-1)).toBe('Quote');
    expect(undoButton()?.getAttribute('aria-label')).toBe('Undo add Quote');
    await key({ key: 'z', ctrlKey: true });
    await until(() => order().at(-1) !== 'Quote');
    await key({ key: 'z', ctrlKey: true });
    await until(() => order().join() === start.join());
    await key({ key: 'z', ctrlKey: true, shiftKey: true });
    await until(() => order().indexOf('Agenda') === start.indexOf('Agenda') - 1);
  });

  it('undoes settings, typing as one step, and the template name', async () => {
    await open();
    await click(card('Executive summary').querySelector('.modhead'));
    const instructions = card('Executive summary').querySelector<HTMLTextAreaElement>('textarea');
    if (instructions === null) {
      throw new Error('no instructions');
    }
    const original = instructions.value;
    await type(instructions, `${original} Keep`);
    await type(instructions, `${original} Keep it short.`);
    expect(undoButton()?.getAttribute('aria-label')).toBe('Undo change Executive summary instructions');
    expect(undoOf(h.store).depth.value).toBe(1);
    // In the field with changes, Ctrl+Z is the field's own; on the page it is the app's.
    const inField = new KeyboardEvent('keydown', { key: 'z', ctrlKey: true, bubbles: true, cancelable: true });
    await act(async () => {
      instructions.focus();
      instructions.dispatchEvent(new Event('focusin', { bubbles: true }));
      instructions.value = `${original} Keep it short!`;
      instructions.dispatchEvent(new Event('input', { bubbles: true }));
      instructions.dispatchEvent(inField);
      await Promise.resolve();
    });
    expect(inField.defaultPrevented).toBe(false);
    await act(async () => {
      instructions.blur();
      await Promise.resolve();
    });
    await key({ key: 'z', ctrlKey: true });
    await until(() => card('Executive summary').querySelector<HTMLTextAreaElement>('textarea')?.value === original);

    await click(button('Long'));
    expect(undoButton()?.getAttribute('aria-label')).toBe('Undo change Executive summary length');
    await settle(MERGE_WINDOW_MS / 10);
    await click(undoButton());
    await until(() => button('Short').getAttribute('aria-pressed') === 'true');

    const name = document.querySelector<HTMLInputElement>('#tpl-name');
    await type(name, 'Minutes for the team');
    expect(undoButton()?.getAttribute('aria-label')).toBe('Undo rename template');
    await click(undoButton());
    await until(() => document.querySelector<HTMLInputElement>('#tpl-name')?.value === 'Meeting minutes');
    await key({ key: 'y', ctrlKey: true });
    await until(() => document.querySelector<HTMLInputElement>('#tpl-name')?.value === 'Minutes for the team');
  });

  it('keeps its stack across a trip to the Style editor and starts afresh after Back', async () => {
    await open();
    await click(button('Remove Agenda'));
    expect(undoButton()?.getAttribute('aria-label')).toBe('Undo remove Agenda');
    await click(document.querySelector('.preview-caption .link-btn, .preview-caption button'));
    await until(() => h.store.route.value.name === 'style');
    // Away from the Builder, Ctrl+Z does nothing.
    expect(undoOf(h.store).undoLabel.value).toBeNull();
    await act(async () => {
      h.store.route.value = { name: 'builder', recordingId: DESIGN, templateId: null, documentId: null };
      await Promise.resolve();
    });
    await until(() => document.querySelector('.mod') !== null);
    expect(undoButton()?.getAttribute('aria-label')).toBe('Undo remove Agenda');
    await click(undoButton());
    await until(() => order().includes('Agenda'));
  });

  it('names each step the way the Undo button reads', () => {
    const rows = [[{ id: 'm01', module: 'agenda' as const, instructions: '', length: 'medium' as const, textSize: 'normal' as const, linkToTranscript: false, customTitle: null, customText: null }]];
    const nameOf = (): string => 'Agenda';
    expect(structureStep(rows, { type: 'remove', id: 'm01' }, nameOf)).toEqual({ label: 'remove Agenda' });
    expect(structureStep(rows, { type: 'update', id: 'm01', patch: { textSize: 'larger' } }, nameOf)).toEqual({ label: 'change Agenda text size' });
    expect(structureStep(rows, { type: 'update', id: 'm01', patch: { instructions: 'x' } }, nameOf)).toEqual({ label: 'change Agenda instructions', mergeKey: 'instructions:m01' });
    expect(structureStep(rows, { type: 'select', id: 'm01' }, nameOf)).toBeNull();
    expect(templateStep({ inputs: { transcript: true, details: true, participants: true, agenda: true, highlights: true, attachments: false, previousDocuments: false } })).toEqual({ label: 'change what the AI receives' });
    expect(templateStep({ styleId: 'minimal' })).toEqual({ label: 'change style' });
  });
});
