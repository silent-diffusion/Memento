import { afterEach, describe, expect, it } from 'vitest';
import type { AgendaApplyParams, AgendaImportDroppedParams, Project } from '../../bridge/types';
import { MERGED_REASON } from '../../bridge/mockAgenda';
import { button, click, dropFiles, fakeWebView, mountApp, press, settle, type, until, type Harness } from '../../testing/appHarness';
import { blankItem, checkLimits, tooLongMessage, tooManyMessage, uncertainNotice } from './agendaItems';

const Q3 = '20261006-100000-q3plan';

describe('agenda items (DESIGN.md §14 limits and notice)', () => {
  it('flags items over 200 characters and agendas over 200 items', () => {
    const ok = { ...blankItem(), text: 'x'.repeat(200) };
    const long = { ...blankItem(), text: 'x'.repeat(201) };
    expect(checkLimits([ok]).ok).toBe(true);
    const limits = checkLimits([ok, long]);
    expect([...limits.tooLong]).toEqual([long.key]);
    expect(limits.ok).toBe(false);
    const many = Array.from({ length: 203 }, (_, i) => ({ ...blankItem(), text: `Item ${i}` }));
    expect(checkLimits(many).overCount).toBe(3);
    // Blank rows are left out of the count (and out of agenda.apply).
    expect(checkLimits([...many.slice(0, 200), blankItem()]).ok).toBe(true);
    expect(tooLongMessage(2, 245)).toBe('Item 3 is 245 characters. Agenda items can be up to 200; shorten it or split it in two.');
    expect(tooManyMessage(214)).toBe('This agenda has 214 items. Memento keeps up to 200; remove 14 to apply it.');
  });

  it('explains one uncertain item with its reason and location, several with a list', () => {
    const one = { ...blankItem(), text: 'Launch date Options: Nov', uncertain: true, uncertainReason: MERGED_REASON, location: 'paragraph 4' };
    expect(uncertainNotice([blankItem(), one])?.lead).toBe(
      'One item is uncertain (dotted). A heading and its bullet may have been merged (paragraph 4). Fix it here, or try the AI option below.',
    );
    const two = { ...one, key: 'k2', location: null, uncertainReason: 'The same item appears twice.' };
    const notice = uncertainNotice([one, blankItem(), two]);
    expect(notice?.lead).toBe('2 items are uncertain (dotted). Fix them here, or try the AI option below.');
    expect(notice?.reasons).toEqual(['Item 1: A heading and its bullet may have been merged (paragraph 4).', 'Item 3: The same item appears twice.']);
    expect(uncertainNotice([blankItem()])).toBeNull();
  });
});

describe('Details sheet agenda import (against the browser-preview host)', () => {
  let h: Harness;

  afterEach(() => {
    h.unmount();
  });

  const sheet = (): HTMLElement => {
    const el = document.querySelector<HTMLElement>('.sheet');
    if (el === null) {
      throw new Error('no sheet');
    }
    return el;
  };
  const items = (): string[] => [...sheet().querySelectorAll<HTMLInputElement>('.agenda-item .ii')].map((i) => i.value);

  /** Review › Details › Agenda › Replace opens the sheet at the drop zone. */
  const openReplace = async (mock = {}): Promise<void> => {
    h = await mountApp({ name: 'review', recordingId: Q3 }, mock);
    await until(() => document.querySelector('.detail-caption') !== null);
    await click(button('Replace'));
    await until(() => document.querySelector('.agenda-drop') !== null);
  };

  it('drops a Word file, shows the parsed preview with the uncertain item and its reason, reorders and applies it', async () => {
    await openReplace();
    await dropFiles(document.querySelector('.agenda-drop'), ['agenda.docx']);
    await until(() => items().length === 5);
    expect((h.callsOf('agenda.importDropped')[0] as AgendaImportDroppedParams).paths).toEqual(['agenda.docx']);
    expect(sheet().querySelector('.sheet-caption')?.textContent).toBe('From agenda.docx · parsed on this PC · Replace');
    const low = [...sheet().querySelectorAll('.agenda-item.low .ii')] as HTMLInputElement[];
    expect(low.map((i) => i.value)).toEqual(['Launch date Options: 2nd week of Nov, after beta']);
    // The reason is on the field for screen readers and in the accent-soft notice.
    const describedBy = low[0]?.getAttribute('aria-describedby') ?? '';
    expect(document.getElementById(describedBy)?.textContent).toBe(MERGED_REASON);
    expect(sheet().querySelector('.agenda-notice')?.textContent).toContain('One item is uncertain (dotted). A heading and its bullet may have been merged (paragraph 4). Fix it here, or try the AI option below.');
    expect(button('Extract').disabled).toBe(true);

    // Keyboard reorder: the second item's handle moves up, and keeps focus.
    const handle = sheet().querySelectorAll<HTMLButtonElement>('.drag-handle')[1];
    handle?.focus();
    await press(handle ?? null, 'ArrowUp');
    await settle(10);
    expect(items().slice(0, 2)).toEqual(['Hiring plan', 'Q2 recap']);
    expect(document.activeElement?.getAttribute('aria-label')).toContain('Hiring plan');

    // Fixing the uncertain item here clears its doubt.
    await type(low[0] ?? null, 'Launch date');
    expect(sheet().querySelector('.agenda-item.low')).toBeNull();
    expect(sheet().querySelector('.agenda-notice')).toBeNull();

    await click(button('Apply agenda'));
    await until(() => h.callsOf('agenda.apply').length === 1 && sheet().querySelector('.agenda-apply') === null);
    const applied = h.callsOf('agenda.apply')[0] as AgendaApplyParams;
    expect(applied.items.map((i) => i.text)).toEqual(['Hiring plan', 'Q2 recap', 'Launch date', 'Budget asks', 'Open questions']);
    expect(applied.source).toBe('agenda.docx');
    expect(applied.attachmentToken).not.toBeNull();
    const project: Project = await h.bridge.call('project.get', { recordingId: Q3 });
    expect(project.details.agenda.items.map((i) => i.text)[0]).toBe('Hiring plan');
    expect(project.history.at(-1)?.summary).toBe('Agenda imported');
  });

  it('enforces the item length and count limits inline before Apply', async () => {
    await openReplace();
    await dropFiles(document.querySelector('.agenda-drop'), ['agenda.docx']);
    await until(() => items().length === 5);
    const second = sheet().querySelectorAll<HTMLInputElement>('.agenda-item .ii')[1] ?? null;
    await type(second, 'y'.repeat(230));
    expect(sheet().querySelector('.agenda-limit')?.textContent).toBe(tooLongMessage(1, 230));
    expect(second?.getAttribute('aria-invalid')).toBe('true');
    expect(button('Apply agenda').disabled).toBe(true);
    // Done does not close the sheet while the preview cannot be applied.
    await click(button('Done'));
    await settle(20);
    expect(document.querySelector('.sheet')).not.toBeNull();
    expect(h.callsOf('agenda.apply')).toEqual([]);
    await type(second, 'Hiring plan');
    expect(sheet().querySelector('.agenda-limit')).toBeNull();
    expect(button('Apply agenda').disabled).toBe(false);

    // A pasted agenda with too many lines.
    await click(sheet().querySelector('.sheet-caption .link-btn'));
    await click(button('Paste text'));
    await type(document.querySelector('#agenda-paste'), Array.from({ length: 205 }, (_, i) => `${i + 1}. Point ${i + 1}`).join('\n'));
    await click(button('Add items'));
    await until(() => items().length === 205);
    expect(sheet().querySelector('.agenda-limit--count')?.textContent).toBe(tooManyMessage(205));
    expect(button('Apply agenda').disabled).toBe(true);
  });

  it('Discard drops the preview and its original; Done applies a pending preview', async () => {
    await openReplace();
    await dropFiles(document.querySelector('.agenda-drop'), ['board.pdf']);
    await until(() => items().length === 5);
    expect(sheet().textContent).toContain('Page 2 has two columns; the left column was read first.');
    await click(button('Discard'));
    await until(() => h.callsOf('agenda.discard').length === 1);
    expect(items()).toEqual(['Q2 recap', 'Hiring plan', 'Launch date', 'Budget asks', 'Open questions']);

    await click(sheet().querySelector('.sheet-caption .link-btn'));
    await click(button('Choose a file'));
    await until(() => sheet().querySelector('.agenda-apply') !== null);
    expect(h.callsOf('agenda.importFile').at(-1)).toEqual({ recordingId: Q3 });
    await click(button('Done'));
    await until(() => document.querySelector('.sheet') === null);
    expect(h.callsOf('agenda.apply')).toHaveLength(1);
  });

  it('in the Memento window, posts agenda.importDropped with the dropped files attached (postMessageWithAdditionalObjects)', async () => {
    const fake = fakeWebView({ live: false, recovery: false, stepMs: 10 });
    h = await mountApp({ name: 'review', recordingId: Q3 }, {}, undefined, fake.webview);
    await until(() => document.querySelector('.detail-caption') !== null);
    await click(button('Replace'));
    await until(() => document.querySelector('.agenda-drop') !== null);
    await dropFiles(document.querySelector('.agenda-drop'), ['agenda.docx']);
    await until(() => items().length === 5);
    const withFiles = fake.posted.filter((p) => p.additionalObjects !== null);
    expect(withFiles).toHaveLength(1);
    expect(withFiles[0]?.message).toMatchObject({ method: 'agenda.importDropped', params: { recordingId: Q3, paths: ['agenda.docx'] } });
    expect((withFiles[0]?.additionalObjects as { name: string }[]).map((f) => f.name)).toEqual(['agenda.docx']);
    // Every other request goes through plain postMessage.
    expect(fake.posted.filter((p) => p.additionalObjects === null).every((p) => (p.message as { method: string }).method !== 'agenda.importDropped')).toBe(true);
  });

  it('falls back to the file picker when the host cannot resolve a drop', async () => {
    await openReplace({ m3: { agenda: 'noDrop' } });
    await dropFiles(document.querySelector('.agenda-drop'), ['agenda.docx']);
    await until(() => items().length > 0);
    expect(h.callsOf('agenda.importDropped')).toHaveLength(1);
    expect(h.callsOf('agenda.importFile')).toEqual([{ recordingId: Q3 }]);
    expect(sheet().querySelector('.agenda-error')).toBeNull();
  });

  it('reports a photo that cannot be read and offers the Windows setting', async () => {
    await openReplace({ m3: { agenda: 'ocrMissing' } });
    await dropFiles(document.querySelector('.agenda-drop'), ['whiteboard.jpg']);
    await until(() => document.querySelector('.agenda-error') !== null);
    expect(sheet().querySelector('.agenda-error')?.textContent).toContain('Nothing was changed.');
    await click(button('Open Windows settings'));
    await until(() => h.callsOf('app.openExternal').length === 1);
    expect(h.callsOf('app.openExternal')[0]).toEqual({ url: 'ms-settings:regionlanguage' });
    await click(button('Dismiss'));
    expect(sheet().querySelector('.agenda-error')).toBeNull();
  });

  it('keeps a dropped agenda with the recording that has not started, then attaches the original once it exists', async () => {
    h = await mountApp({ name: 'record', sessionId: null });
    await until(() => h.container.querySelectorAll('.src').length === 5);
    await click(button('Details and agenda'));
    await dropFiles(document.querySelector('.agenda-drop'), ['agenda.docx']);
    await until(() => items().length === 5);
    expect((h.callsOf('agenda.importDropped')[0] as AgendaImportDroppedParams).recordingId).toBeNull();
    expect(sheet().querySelector('.ai-card-sub')?.textContent).toBe('External AI is off. Turn it on in Settings › AI and privacy.');
    await click(button('Done'));
    await until(() => document.querySelector('.sheet') === null);
    expect(h.callsOf('agenda.apply')).toEqual([]);
    expect(h.container.querySelectorAll('.rec-agenda-item')).toHaveLength(5);
    await click(button('Start recording'));
    await until(() => h.callsOf('agenda.apply').length === 1);
    const recordingId = h.store.recording.value?.recordingId ?? '';
    await until(() => h.calls.filter(([m]) => m === 'agenda.apply').length === 1);
    await settle(30);
    const attachments = await h.bridge.call('attachments.list', { recordingId });
    expect(attachments.attachments.map((a) => [a.name, a.kind])).toEqual([['agenda.docx', 'agenda']]);

    // Ticking an item off while recording uses agenda.setCovered.
    await click(h.container.querySelector('.rec-agenda-item'));
    await until(() => h.callsOf('agenda.setCovered').length === 1);
    const project = await h.bridge.call('project.get', { recordingId });
    expect(project.details.agenda.items.map((i) => i.covered)).toEqual([true, false, false, false, false]);
  });
});
