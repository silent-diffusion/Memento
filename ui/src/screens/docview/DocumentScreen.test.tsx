import { act } from 'preact/test-utils';
import { afterEach, describe, expect, it } from 'vitest';
import type { DocumentRestoreVersionParams, DocumentSaveEditParams } from '../../bridge/types';
import { importPaper, markupProblems } from '../../components/paper/paperDom';
import { button, click, mountApp, press, settle, type, until, type Harness } from '../../testing/appHarness';
import { SAVE_DEBOUNCE_MS } from './saver';

const DESIGN = '20261005-160000-dsrev';
const MINUTES = 'doc-20261005-minutes';

describe('Document viewer (DESIGN.md §12, against the browser-preview host)', () => {
  let h: Harness;

  afterEach(() => {
    h.unmount();
  });

  const open = async (documentId = MINUTES): Promise<void> => {
    h = await mountApp({ name: 'document', recordingId: DESIGN, documentId }, { m4: { ai: 'ready' } });
    await until(() => document.querySelector('.doc-paper article.paper') !== null && document.querySelector('.doc-ver') !== null);
  };
  const paper = (): HTMLElement => {
    const el = document.querySelector<HTMLElement>('.doc-paper article.paper');
    if (el === null) {
      throw new Error('no paper');
    }
    return el;
  };
  const caret = (el: Element, offset: number): void => {
    const walker = document.createTreeWalker(el, NodeFilter.SHOW_TEXT);
    const text = walker.nextNode();
    if (text === null) {
      throw new Error('no text');
    }
    const range = document.createRange();
    range.setStart(text, offset);
    range.collapse(true);
    document.getSelection()?.removeAllRanges();
    document.getSelection()?.addRange(range);
    document.dispatchEvent(new Event('selectionchange'));
  };

  it('shows the header, the editable paper from documents.renderHtml and How this was made', async () => {
    await open();
    expect(document.querySelector<HTMLInputElement>('#doc-name')?.value).toBe('Meeting minutes');
    expect(document.querySelector('.spoke-title-edit .pill')?.textContent).toMatch(/^Corporate · Claude · Yesterday, \d+:\d\d (AM|PM)$/);
    expect(document.querySelector('.doc-saved')?.textContent).toBe('Saved');
    expect(document.querySelector('[data-spoke-back]')?.textContent).toBe('Design review: library screen');
    const article = paper();
    expect(article.getAttribute('contenteditable')).toBe('true');
    expect(article.getAttribute('role')).toBe('textbox');
    expect(article.querySelector('.paper-meta')?.getAttribute('contenteditable')).toBe('false');
    expect(markupProblems(article)).toEqual([]);
    const how = document.querySelector('.how-made');
    expect([...(how?.querySelectorAll('dt') ?? [])].map((d) => d.textContent)).toEqual(['Template', 'Style', 'Provider', 'Generated']);
    expect(how?.textContent).toContain('Claude (Anthropic)');
    expect(how?.textContent).toMatch(/Yesterday, \d+:\d\d (AM|PM) · 38 s/);
    expect([...(how?.querySelectorAll('.pill') ?? [])].map((p) => p.textContent)).toEqual(['Transcript', 'Details', 'Participants', 'Agenda', 'Highlights']);
    expect(how?.textContent).toContain('Audio and video were not sent.');
    expect(document.querySelector('.doc-footnote')?.textContent).toBe('Timestamps link back to the transcript. Edits save as you type and stay inside this recording.');
  });

  it('formats from the toolbar and saves the edit after a pause, keeping the markup contract', async () => {
    await open();
    const toolbar = document.querySelector('[role="toolbar"]');
    expect([...(toolbar?.querySelectorAll('button') ?? [])].map((b) => b.getAttribute('aria-label') ?? b.textContent)).toEqual([
      'Bold',
      'Italic',
      'Heading',
      'Paragraph',
      'Bulleted list',
      'Numbered list',
      'Table',
      'Insert timestamp',
    ]);
    expect(button('Bold').disabled).toBe(true);
    const purpose = paper().querySelector('section[data-module="meetingPurpose"] p');
    if (purpose === null) {
      throw new Error('no purpose');
    }
    await act(async () => {
      caret(purpose, 3);
      await Promise.resolve();
    });
    expect(button('Paragraph').getAttribute('aria-pressed')).toBe('true');
    await click(button('Bulleted list'));
    expect(paper().querySelector('section[data-module="meetingPurpose"] ul > li')?.textContent).toBe('Agree the library layout before build starts.');
    expect(document.querySelector('.doc-saved')?.textContent).toBe('Edited');
    expect(button('Bulleted list').getAttribute('aria-pressed')).toBe('true');
    // Insert timestamp: the field starts at 0:00 (Review has not been played), Enter inserts the chip.
    await click(button('Insert timestamp'));
    const field = document.querySelector<HTMLInputElement>('#stamp-time');
    expect(field?.value).toBe('0:00');
    await type(field, '99:99');
    await click(button('Insert'));
    expect(document.querySelector('.stamp-hint--error')?.textContent).toBe('Type a time like 18:42 or 1:02:05.');
    await type(field, '19:31');
    await click(button('Insert'));
    expect(paper().querySelector('section[data-module="meetingPurpose"] a.ts[data-t="1171"]')?.textContent).toBe('19:31');
    await settle(SAVE_DEBOUNCE_MS + 100);
    await until(() => document.querySelector('.doc-saved')?.textContent === 'Saved');
    const saves = h.callsOf('documents.saveEdit') as DocumentSaveEditParams[];
    expect(saves).toHaveLength(1);
    const saved = importPaper(saves[0]?.html ?? '');
    expect(saved === null ? ['no paper'] : markupProblems(saved)).toEqual([]);
    expect(saves[0]?.html).not.toContain('contenteditable="true"');
    expect(saves[0]?.html).toContain('<ul><li>Agree');
    // The edit is version 3 now.
    await until(() => document.querySelector('.doc-ver.cur .doc-ver-title')?.textContent === 'Version 3 · current');
  });

  it('opens Review at the moment of a timestamp chip', async () => {
    await open();
    await click(paper().querySelector('section[data-module="executiveSummary"] a.ts'));
    await until(() => h.store.route.value.name === 'review');
    expect(h.store.route.value).toEqual({ name: 'review', recordingId: DESIGN, atMs: 1_122_000 });
  });

  it('lists versions with Restore, which asks first and keeps the current text as a version', async () => {
    await open();
    expect([...document.querySelectorAll('.doc-ver')].map((v) => v.textContent)).toEqual([
      expect.stringMatching(/^Version 2 · currentEdited by you · yesterday \d+:\d\d (AM|PM) · 3 changes$/),
      expect.stringMatching(/^Version 1RestoreGenerated · yesterday \d+:\d\d (AM|PM)$/),
    ]);
    expect(document.querySelector('.versions-head')?.textContent).toBe('VersionsHistory on · 90 days');
    await click(button('Restore version 1'));
    expect(document.querySelector('#restore-doc-title')?.textContent).toBe('Restore version 1?');
    await click(document.querySelector('[role="dialog"] .btn.p'));
    await until(() => document.querySelector('#restore-doc-title') === null);
    expect((h.callsOf('documents.restoreVersion') as DocumentRestoreVersionParams[])[0]?.versionId).toBe('v1');
    await until(() => document.querySelector('.doc-ver.cur .doc-ver-meta')?.textContent.startsWith('Restored') === true);
    await until(() => paper().textContent.includes('(reached with 8 minutes left)'));
    expect(paper().textContent).not.toContain('owners agreed');
  });

  it('says so when version history is off, renames, and opens Export with this document ticked', async () => {
    await open();
    await h.bridge.call('settings.set', { history: { keepVersions: false } });
    h.store.settings.value = await h.bridge.call('settings.get');
    await until(() => document.querySelector('.versions-off') !== null);
    expect(document.querySelector('.versions-off')?.textContent).toContain('Version history is off');
    const name = document.querySelector<HTMLInputElement>('#doc-name');
    await type(name, 'Minutes, library review');
    await press(name, 'Enter');
    await until(() => h.callsOf('documents.rename').length === 1);
    await click(button('Export'));
    await until(() => document.querySelector('.export-subrows') !== null);
    const boxes = [...document.querySelectorAll<HTMLInputElement>('.export-subrow input')];
    expect(boxes.map((b) => b.checked)).toEqual([true, false, false]);
    expect(document.querySelector<HTMLInputElement>('#exp-documents')?.checked).toBe(true);
  });

  it('copies the document to the clipboard through the host and says so beside Undo (after 1.2.0)', async () => {
    await open();
    await click(button('More actions'));
    await click(button('Copy to clipboard'));
    await until(() => h.callsOf('documents.copy').length === 1);
    expect(h.callsOf('documents.copy')[0]).toEqual({ recordingId: DESIGN, documentId: MINUTES });
    await until(() => document.querySelector('.undo-status')?.textContent === 'Copied “Meeting minutes”');
    expect(document.querySelector('.toast')).toBeNull();
  });

  it('deletes after confirming, back to the recording', async () => {
    await open('doc-20261005-notes');
    expect(document.querySelector('.how-made')?.textContent).toContain('No AI was involved.');
    expect(button('Regenerate').disabled).toBe(true);
    await click(button('More actions'));
    await click(button('Delete'));
    expect(document.querySelector('#delete-doc-title')?.textContent).toBe('Delete “My notes”?');
    await click(document.querySelector('[role="dialog"] .btn.d'));
    await until(() => h.store.route.value.name === 'review');
    const { documents } = await h.bridge.call('documents.list', { recordingId: DESIGN });
    expect(documents.map((d) => d.name)).toEqual(['Meeting minutes', 'Action items']);
  });
});

describe('Review › Documents (DESIGN.md §9)', () => {
  let h: Harness;

  afterEach(() => {
    h.unmount();
  });

  it('lists one card per document and opens it, creates in the Builder, and writes a new one', async () => {
    h = await mountApp({ name: 'review', recordingId: DESIGN });
    await until(() => document.querySelector('.review-panes') !== null);
    await click(document.querySelector('#review-tab-documents'));
    await until(() => document.querySelectorAll('.doc-card').length === 3);
    const cards = [...document.querySelectorAll('.doc-card')].map((c) => [...c.children].map((s) => s.textContent));
    expect(cards[0]).toEqual(['Meeting minutes', expect.stringMatching(/^Corporate style · generated yesterday, \d+:\d\d (AM|PM) · Claude$/), 'Edited · 2 versions']);
    expect(cards[2]?.[1]).toMatch(/^Minimal style · written by you · /);
    await click(button('Write a document'));
    await until(() => h.store.route.value.name === 'document');
    const route = h.store.route.value;
    const { documents } = await h.bridge.call('documents.list', { recordingId: DESIGN });
    expect(documents.at(-1)?.name).toBe('Notes');
    expect(route.name === 'document' ? route.documentId : null).toBe(documents.at(-1)?.id);
  });
});
