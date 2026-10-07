import { afterEach, describe, expect, it } from 'vitest';
import type { ExportEstimateParams, ExportRunParams } from '../../bridge/types';
import { exportFolderName } from '../../format/export';
import { button, click, mountApp, press, settle, until, type Harness } from '../../testing/appHarness';
import { ESTIMATE_DEBOUNCE_MS, failureText } from './ExportDialog';

const DESIGN = '20261005-160000-dsrev';
const Q3 = '20261006-100000-q3plan';

describe('Export copies (DESIGN.md §15, against the browser-preview host)', () => {
  let h: Harness;

  afterEach(() => {
    h.unmount();
  });

  const dialog = (): HTMLElement => {
    const el = document.querySelector<HTMLElement>('.export-dialog');
    if (el === null) {
      throw new Error('no export dialog');
    }
    return el;
  };
  const summary = (): string => dialog().querySelector('.export-summary')?.textContent ?? '';
  const row = (id: string): HTMLElement => {
    const el = dialog().querySelector<HTMLElement>(`#${id}`)?.closest<HTMLElement>('.export-row');
    if (el === null || el === undefined) {
      throw new Error(`no row ${id}`);
    }
    return el;
  };
  const choose = async (label: string, option: string): Promise<void> => {
    await click(dialog().querySelector(`[aria-label^="${label}:"]`));
    await click([...document.querySelectorAll('[role="option"]')].find((o) => o.textContent.trim() === option));
  };

  const open = async (recordingId = DESIGN, mock = {}): Promise<void> => {
    h = await mountApp({ name: 'review', recordingId }, mock);
    await until(() => document.querySelector('.review-panes') !== null);
    await click([...document.querySelectorAll<HTMLButtonElement>('.spoke-ghost')].find((b) => b.textContent.trim() === 'Export'));
    await until(() => document.querySelector('.export-dialog') !== null && !summary().includes('Estimating'));
  };

  it('shows the header, the component rows and the defaults from Settings › Export with sizes', async () => {
    await open();
    expect(dialog().querySelector('h2')?.textContent).toBe('Export copies');
    expect(dialog().querySelector('#export-subtitle')?.textContent).toBe(
      'Design review: library screen. Files are written outside Memento; the recording inside Memento stays the original.',
    );
    const names = [...dialog().querySelectorAll('.export-row-name')].map((n) => n.textContent);
    expect(names.slice(0, 7)).toEqual(['Audio (mixed)', 'Individual tracks', 'Video', 'Transcript', 'Documents', 'Recording details', 'Attachments']);
    expect(row('exp-tracks').textContent).toContain('Shure MV7, Everything this PC plays and Zoom as separate files');
    expect(row('exp-transcript').textContent).toContain('Speakers, timestamps and confidence');
    expect(row('exp-video').textContent).toContain('Not in this version');
    expect(row('exp-documents').textContent).toContain('Documents arrive in a later version');
    expect(row('exp-attachments').textContent).toContain('agenda.docx and library-mockups-v3.pdf');
    // Defaults: mixed audio as FLAC and the transcript as JSON.
    expect(dialog().querySelector<HTMLInputElement>('#exp-audio')?.checked).toBe(true);
    expect(dialog().querySelector<HTMLInputElement>('#exp-transcript')?.checked).toBe(true);
    expect(dialog().querySelector<HTMLInputElement>('#exp-video')?.disabled).toBe(true);
    expect(dialog().querySelector<HTMLInputElement>('#exp-documents')?.disabled).toBe(true);
    // Format selects are disabled while their row is unticked.
    expect(row('exp-tracks').querySelector<HTMLButtonElement>('.select-btn')?.disabled).toBe(true);
    expect(row('exp-audio').querySelector<HTMLButtonElement>('.select-btn')?.disabled).toBe(false);
    expect(row('exp-audio').querySelector('.export-row-size')?.textContent).toMatch(/^\d+ MB$/);
    expect(summary()).toMatch(/^2 files · about \d+ MB$/);
  });

  it('asks for every size once after a pause in changes and sums the ticked rows', async () => {
    await open();
    const before = h.callsOf('export.estimate').length;
    expect(before).toBe(1);
    // Ticks change nothing the host is asked: the summary is summed locally.
    await click(dialog().querySelector('#exp-tracks'));
    expect(summary()).toMatch(/^5 files · about /);
    expect(row('exp-tracks').querySelector<HTMLButtonElement>('.select-btn')?.disabled).toBe(false);
    await click(dialog().querySelector('#exp-details'));
    expect(summary()).toMatch(/^6 files · about /);
    // Three quick format changes make one estimate.
    await choose('Format for Audio (mixed)', 'WAV');
    await choose('Format for Audio (mixed)', 'MP3');
    await choose('Format for Individual tracks', 'MP3');
    await settle(ESTIMATE_DEBOUNCE_MS + 60);
    const estimates = h.callsOf('export.estimate') as ExportEstimateParams[];
    expect(estimates).toHaveLength(2);
    expect(estimates[1]?.selection.audioMixed).toEqual({ on: true, format: 'mp3', bitrateKbps: 192 });
    expect(estimates[1]?.selection.tracks.format).toBe('mp3');
    await until(() => /about \d+ MB/.test(summary()));
  });

  it('previews the path with and without the recording folder and remembers the choices', async () => {
    await open();
    const path = (): string => dialog().querySelector('.export-path')?.textContent ?? '';
    const createdAt = h.store.library.value?.recordings.find((r) => r.id === DESIGN)?.createdAt ?? '';
    const folder = exportFolderName('Design review: library screen', createdAt);
    expect(path()).toBe(`D:\\Exports\\${folder}\\`);
    await click(button('Put everything in a folder named after the recording'));
    expect(path()).toBe('D:\\Exports\\');
    await click(button('Put everything in a folder named after the recording'));
    await click(button('Remember these choices'));
    await click(button('Change export folder'));
    await until(() => (dialog().querySelector('.export-path')?.textContent ?? '').startsWith('E:'));
    await until(() => path().startsWith('E:\\Recordings\\Memento'));
    await click(dialog().querySelector('.export-foot .btn.p'));
    await until(() => h.callsOf('export.run').length === 1 && document.querySelector('.export-dialog') === null);
    const run = h.callsOf('export.run')[0] as ExportRunParams;
    expect(run.destination).toEqual({ folder: 'E:\\Recordings\\Memento', createSubfolder: true });
    expect(run.remember).toBe(true);
    // Documents and anything unavailable are never sent as ticked.
    expect(run.selection.documents.on).toBe(false);
    await until(() => h.store.settings.value?.export.defaultFolder === 'E:\\Recordings\\Memento');
  });

  it('asks where to save first when Settings says to ask each time', async () => {
    await open();
    expect(h.store.settings.value?.export.askWhereEachTime).toBe(true);
    await click(dialog().querySelector('.export-foot .btn.p'));
    await until(() => h.callsOf('export.run').length === 1);
    expect(h.callsOf('dialog.pickFolder')).toHaveLength(1);
    expect((h.callsOf('export.run')[0] as ExportRunParams).destination.folder).toBe('E:\\Recordings\\Memento');
  });

  it('shows progress in the footer and a toast with Open folder when done', async () => {
    await open();
    await click(button('Change export folder'));
    await until(() => (dialog().querySelector('.export-path')?.textContent ?? '').startsWith('E:'));
    await click(dialog().querySelector('.export-foot .btn.p'));
    await until(() => document.querySelector('.footer-export') !== null);
    expect(document.querySelector('.footer-export')?.textContent).toMatch(/^Exporting Design review: library screen · \d+%$/);
    await until(() => document.querySelector('.toast') !== null);
    expect(document.querySelector('.toast-title')?.textContent).toBe('Exported Design review: library screen');
    expect(document.querySelector('.toast-body')?.textContent).toContain('in E:\\Recordings\\Memento\\Design review - library screen');
    expect(document.querySelector('.footer-export')).toBeNull();
    await click(button('Open folder'));
    await until(() => h.callsOf('export.openFolder').length === 1);
  });

  it('reports a failed job with the inline card, Try again and Choose another folder', async () => {
    await open(DESIGN, { m3: { export: 'fail' } });
    await click(button('Change export folder'));
    await until(() => (dialog().querySelector('.export-path')?.textContent ?? '').startsWith('E:'));
    await click(dialog().querySelector('.export-foot .btn.p'));
    await until(() => document.querySelector('.toast') !== null);
    expect(document.querySelector('.toast-title')?.textContent).toBe('Design review: library screen was not exported');
    await click(button('Try again'));
    await until(() => document.querySelector('.export-failure') !== null);
    const card = dialog().querySelector('.export-failure')?.textContent ?? '';
    expect(card).toContain('the drive was disconnected');
    expect(card).toContain('Nothing inside Memento was changed.');
    // The same choices come back.
    expect(dialog().querySelector('.export-path')?.textContent).toContain('E:\\Recordings\\Memento');
    await click(button('Choose another folder'));
    await until(() => h.callsOf('dialog.pickFolder').length === 2 && document.querySelector('.export-failure') === null);
  });

  it('keeps the dialog open with the card when the host refuses the folder', async () => {
    await open(DESIGN, { m3: { export: 'unwritable' } });
    await click(button('Change export folder'));
    await until(() => (dialog().querySelector('.export-path')?.textContent ?? '').startsWith('E:'));
    await click(dialog().querySelector('.export-foot .btn.p'));
    await until(() => document.querySelector('.export-failure') !== null);
    expect(dialog().querySelector('.export-failure')?.textContent).toContain('Nothing was written, and nothing inside Memento was changed.');
    expect(document.querySelector('.footer-export')).toBeNull();
    expect(failureText('The drive is full.')).toBe('The drive is full. Nothing inside Memento was changed.');
  });

  it('dims unavailable rows with the reason and leaves them out', async () => {
    await open(Q3);
    const transcript = dialog().querySelector<HTMLInputElement>('#exp-transcript');
    expect(transcript?.disabled).toBe(true);
    expect(transcript?.checked).toBe(false);
    expect(row('exp-transcript').classList.contains('export-row--unavailable')).toBe(true);
    expect(row('exp-transcript').textContent).toContain('Not transcribed yet');
    expect(summary()).toMatch(/^1 file · about /);
  });

  it('moves through the dialog with the keyboard and Esc cancels', async () => {
    await open();
    expect(document.activeElement?.closest('.export-dialog')).not.toBeNull();
    const audio = dialog().querySelector<HTMLInputElement>('#exp-audio');
    audio?.focus();
    await press(dialog(), 'Escape');
    expect(document.querySelector('.export-dialog')).toBeNull();
    expect(h.callsOf('export.run')).toEqual([]);
  });
});
