import { afterEach, describe, expect, it } from 'vitest';
import { attachmentKind } from '../../components/attachments/AttachmentsSection';
import { removeAttachmentCopy } from '../../components/attachments/RemoveAttachmentDialog';
import { button, click, mountApp, until, type Harness } from '../../testing/appHarness';

const DESIGN = '20261005-160000-dsrev';

describe('Import audio or video (against the browser-preview host)', () => {
  let h: Harness;

  afterEach(() => {
    h.unmount();
  });

  it('imports from the first-run Library and shows the new recording processing', async () => {
    h = await mountApp({ name: 'library' }, { library: 'empty' });
    await until(() => document.querySelector('.empty-title') !== null);
    await click(button('Import audio or video'));
    await until(() => h.callsOf('library.importMedia').length === 1);
    expect(h.callsOf('library.importMedia')[0]).toEqual({});
    await until(() => document.querySelector('.lib-title') !== null);
    await until(() => (document.querySelector('.processing-card, [class*="proc"]')?.textContent ?? '').includes('Customer interview'));
    expect(h.store.libraryView.value.selectedId).not.toBeNull();
    // Stored, then the transcript is queued like any recording.
    await until(() => h.store.processing.value?.current?.stages.some((s) => s.stage === 'transcript') === true, 10_000);
  });

  it('reports a file that cannot be decoded inline, with Choose another file', async () => {
    h = await mountApp({ name: 'library' }, { library: 'empty', m3: { import: 'unsupported' } });
    await until(() => document.querySelector('.empty-title') !== null);
    await click(button('Import audio or video'));
    await until(() => document.querySelector('.import-error') !== null);
    const card = document.querySelector('.import-error')?.textContent ?? '';
    expect(card).toContain('The file was not imported');
    expect(card).toContain('Nothing was added to the library.');
    // The card sits in the first-run column, below the buttons.
    expect(document.querySelector('.empty-column .import-error')).not.toBeNull();
    await click(button('Choose another file'));
    await until(() => h.callsOf('library.importMedia').length === 2);
    await until(() => document.querySelector('.import-error') !== null);
    await click(button('Dismiss'));
    expect(document.querySelector('.import-error')).toBeNull();
  });

  it('offers Import again on an import that stopped, and imports the same file into the same recording', async () => {
    h = await mountApp({ name: 'library' }, { library: 'empty', m3: { import: 'interrupted' } });
    await until(() => document.querySelector('.empty-title') !== null);
    await click(button('Import audio or video'));
    await until(() => (document.querySelector('.pill.failed')?.textContent ?? '') === 'Import interrupted · Import again', 10_000);
    const id = h.store.library.value?.recordings[0]?.id ?? '';
    await until(() => h.store.library.value?.recordings[0]?.state === 'failed');
    expect(document.querySelector('.pill.failed')?.getAttribute('title')).toBe('Import the same file again');

    await click(document.querySelector('.pill.failed'));
    await until(() => h.callsOf('processing.retry').length === 1);
    expect(h.callsOf('processing.retry')[0]).toEqual({ recordingId: id, stage: 'stored', remedyId: 'importAgain' });
    await until(() => h.store.processing.value?.current?.stages.some((s) => s.stage === 'transcript') === true, 10_000);
    expect(h.store.library.value?.recordings.map((r) => r.id)).toEqual([id]);
    const project = await h.bridge.call('project.get', { recordingId: id });
    expect(project.history.map((e) => e.summary)).toEqual(expect.arrayContaining(['Import interrupted', 'Importing again']));
  });

  it('offers Import in the Library header menu', async () => {
    h = await mountApp({ name: 'library' });
    await until(() => document.querySelector('.lib-title') !== null);
    await click(button('More library actions'));
    expect([...document.querySelectorAll('[role="menuitem"]')].map((m) => m.textContent)).toEqual(['Import audio or video…']);
    await click(button('Import audio or video…'));
    await until(() => h.callsOf('library.importMedia').length === 1);
    // The Library reads itself again with the new recording before the test ends.
    await until(() => h.store.library.value?.totalCount === 15);
  });

  it('sorts the Library largest first', async () => {
    h = await mountApp({ name: 'library' });
    await until(() => document.querySelector('.lib-title') !== null);
    await click(document.querySelector('[aria-label^="Sort:"]'));
    await click(button('Largest'));
    await until(() => document.querySelector('.lib-group-label')?.textContent === 'Largest first');
    expect(h.callsOf('library.list').at(-1)).toEqual({ sort: 'size' });
  });
});

describe('Attachments (against the browser-preview host)', () => {
  let h: Harness;

  afterEach(() => {
    h.unmount();
  });

  const names = (): string[] => [...document.querySelectorAll('.attachments--review .attachment-name')].map((n) => n.textContent);

  it('lists, adds, opens and removes after a confirmation that names the file', async () => {
    h = await mountApp({ name: 'review', recordingId: DESIGN });
    await until(() => names().length === 2);
    expect(names()).toEqual(['agenda.docx', 'library-mockups-v3.pdf']);
    const meta = [...document.querySelectorAll('.attachments--review .attachment-meta')].map((m) => m.textContent);
    expect(meta).toEqual(['38 KB · Agenda file', '4 MB · PDF']);

    await click(button('+ Add a file'));
    await until(() => names().length === 3);
    expect(names()[2]).toBe('project-brief.pdf');

    await click(button('Open library-mockups-v3.pdf'));
    await until(() => h.callsOf('attachments.open').length === 1);

    await click(button('Remove library-mockups-v3.pdf'));
    expect(document.querySelector('[role="dialog"] h2')?.textContent).toBe('Remove library-mockups-v3.pdf?');
    expect(document.querySelector('[role="dialog"] .dialog-body')?.textContent).toContain('The file you added it from is not touched');
    // Cancel is focused, never the destructive button.
    expect(document.activeElement?.textContent).toBe('Cancel');
    await click(button('Cancel'));
    expect(h.callsOf('attachments.remove')).toEqual([]);
    await click(button('Remove library-mockups-v3.pdf'));
    await click([...document.querySelectorAll<HTMLButtonElement>('[role="dialog"] .btn.d')][0]);
    await until(() => names().length === 2);
    expect(names()).toEqual(['agenda.docx', 'project-brief.pdf']);
  });

  it('names kinds and writes the removal copy', () => {
    const base = { id: 'f1', name: 'notes.txt', sizeBytes: 2048, addedAt: '2026-10-06T10:00:00+02:00', kind: 'file' as const, contentType: null };
    expect(attachmentKind(base)).toBe('TXT');
    expect(attachmentKind({ ...base, contentType: 'image/png' })).toBe('Image');
    expect(attachmentKind({ ...base, kind: 'agenda' })).toBe('Agenda file');
    expect(removeAttachmentCopy(base).title).toBe('Remove notes.txt?');
    expect(removeAttachmentCopy(base).body).toContain('notes.txt (2 KB)');
  });
});
