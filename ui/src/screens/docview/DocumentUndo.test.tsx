import { act } from 'preact/test-utils';
import { afterEach, describe, expect, it } from 'vitest';
import type { DocumentSaveEditParams } from '../../bridge/types';
import { click, mountApp, press, settle, type, until, type Harness } from '../../testing/appHarness';
import { MERGE_WINDOW_MS } from '../../state/undo';
import { SAVE_DEBOUNCE_MS } from './saver';

const DESIGN = '20261005-160000-dsrev';
const MINUTES = 'doc-20261005-minutes';

describe('Undo in the document viewer', () => {
  let h: Harness;

  afterEach(() => {
    h.unmount();
  });

  const open = async (): Promise<void> => {
    h = await mountApp({ name: 'document', recordingId: DESIGN, documentId: MINUTES }, { m4: { ai: 'ready' } });
    await until(() => document.querySelector('.doc-paper article.paper') !== null && document.querySelector('.doc-ver') !== null);
  };
  const paragraph = (): HTMLElement => {
    const el = document.querySelector<HTMLElement>('.doc-paper article.paper section.paper-module p');
    if (el === null) {
      throw new Error('no paragraph');
    }
    return el;
  };
  const undoButton = (): HTMLButtonElement | null => document.querySelector<HTMLButtonElement>('.undo-btn');

  /** Typing as the browser reports it: beforeinput, the change, input. */
  const typeInPaper = async (text: string): Promise<void> => {
    await act(async () => {
      const p = paragraph();
      p.dispatchEvent(new InputEvent('beforeinput', { bubbles: true, cancelable: true, inputType: 'insertText', data: text }));
      p.textContent = text;
      p.dispatchEvent(new InputEvent('input', { bubbles: true, inputType: 'insertText', data: text }));
      await Promise.resolve();
    });
  };

  const savedParagraph = (): string | null =>
    new DOMParser().parseFromString(lastSaved(), 'text/html').querySelector('article.paper section.paper-module p')?.textContent ?? null;
  const lastSaved = (): string => ((h.callsOf('documents.saveEdit') as DocumentSaveEditParams[]).at(-1)?.html ?? '');

  it('takes back a run of typing as one step, saves the paper as it was, and redoes it', async () => {
    await open();
    const original = paragraph().textContent;
    await typeInPaper('First draft');
    await typeInPaper('First draft of the summary.');
    expect(undoButton()?.getAttribute('aria-label')).toBe('Undo edit document');
    await settle(SAVE_DEBOUNCE_MS + 50);
    await until(() => lastSaved().includes('First draft of the summary.'));

    await click(undoButton());
    await until(() => paragraph().textContent === original);
    await settle(SAVE_DEBOUNCE_MS + 50);
    // The saved markup is the paper as it was: its first paragraph reads as before.
    await until(() => savedParagraph() === original);
    expect(document.querySelector('.undo-status')?.textContent).toBe('Undone: edit document');

    await act(async () => {
      document.body.dispatchEvent(new KeyboardEvent('keydown', { key: 'y', ctrlKey: true, bubbles: true, cancelable: true }));
      await Promise.resolve();
    });
    await until(() => paragraph().textContent === 'First draft of the summary.');

    // A pause makes the next typing its own step.
    await settle(MERGE_WINDOW_MS + 50);
    await typeInPaper('Second pass.');
    await act(async () => {
      document.body.dispatchEvent(new KeyboardEvent('keydown', { key: 'z', ctrlKey: true, bubbles: true, cancelable: true }));
      await Promise.resolve();
    });
    await until(() => paragraph().textContent === 'First draft of the summary.');
  });

  it('undoes a rename of the document', async () => {
    await open();
    const name = document.querySelector<HTMLInputElement>('#doc-name');
    await type(name, 'Minutes, library review');
    await press(name, 'Enter');
    await until(() => undoButton()?.getAttribute('aria-label') === 'Undo rename document');
    await click(undoButton());
    await until(() => document.querySelector<HTMLInputElement>('#doc-name')?.value === 'Meeting minutes');
    const { summary } = await h.bridge.call('documents.get', { recordingId: DESIGN, documentId: MINUTES });
    expect(summary.name).toBe('Meeting minutes');
  });
});
