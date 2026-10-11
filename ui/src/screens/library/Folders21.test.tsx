// 2.1 against the browser-preview host: the Date format presets (Settings › General), Delete audio
// tracks older than (Settings › Storage), the Library's Folders view (tree, counts, create, move,
// delete with its confirmation) and Delete audio's confirmation from the row menu.
import { afterEach, describe, expect, it } from 'vitest';
import type { FolderInfo, SettingsSetParams } from '../../bridge/types';
import { button, click, mountApp, press, settle, until, type Harness } from '../../testing/appHarness';
import { DATE_FORMAT_OPTIONS, dateFormat, formatFullDate, untitledTitle } from '../../format/dates';
import { deleteAudioCopy, deleteFolderCopy, folderNameProblem, folderTree } from '../../format/folders';
import { audioAgeWords, deleteAudioConfirmText, deleteAudioText } from '../settings/DeleteAudio';
import { dateFormatText } from '../settings/DateFormat';
import { formatShortDate } from '../../format/when';

const rowOf = (label: string): HTMLElement => {
  const row = [...document.querySelectorAll<HTMLElement>('.settings-row')].find((r) => r.querySelector('.settings-row-label')?.textContent === label);
  if (row === undefined) {
    throw new Error(`no row ${label}`);
  }
  return row;
};
const descOf = (label: string): string => rowOf(label).querySelector('.settings-row-desc')?.textContent ?? '';
const selectOf = (label: string): HTMLButtonElement | null => rowOf(label).querySelector<HTMLButtonElement>(`[aria-label^="${label}:"]`);
const options = (): string[] => [...document.querySelectorAll('[role="option"]')].map((o) => o.textContent.trim());
const choose = async (label: string, option: string): Promise<void> => {
  await click(selectOf(label));
  await click([...document.querySelectorAll('[role="option"]')].find((o) => o.textContent.trim() === option));
};
const dialog = (): HTMLElement | null => document.querySelector<HTMLElement>('[role="dialog"]');
const menuItem = (name: string): HTMLElement | undefined => [...document.querySelectorAll<HTMLElement>('[role="menuitem"]')].find((m) => m.textContent === name);
const treeNames = (): string[] => [...document.querySelectorAll('.folder-tree .folder-name')].map((n) => n.textContent);

const FEB_16 = new Date(2026, 1, 16, 9, 30);

describe('dates (2.1, Settings › General › Date format)', () => {
  afterEach(() => {
    dateFormat.value = 'yearMonthDay';
  });

  it('writes each preset as its label shows', () => {
    expect(DATE_FORMAT_OPTIONS.map((o) => o.label)).toEqual(['2026 February 16', '16 February 2026', 'February 16, 2026', '2026-02-16', 'Windows setting']);
    for (const option of DATE_FORMAT_OPTIONS.filter((o) => o.value !== 'windows')) {
      expect(formatFullDate(FEB_16, option.value)).toBe(option.label);
    }
    expect(formatFullDate(FEB_16, 'windows')).toBe(new Intl.DateTimeFormat(undefined, { dateStyle: 'long' }).format(FEB_16));
  });

  it('dates new titles and other years in the chosen format, and leaves this year’s short dates and times alone', () => {
    expect(untitledTitle('meeting', FEB_16)).toBe('Untitled meeting · 2026 February 16');
    dateFormat.value = 'dayMonthYear';
    expect(untitledTitle('interview', FEB_16)).toBe('Untitled interview · 16 February 2026');
    expect(formatShortDate(FEB_16, new Date(2027, 0, 2))).toBe('16 February 2026');
    expect(formatShortDate(FEB_16, new Date(2026, 5, 2))).toBe('Feb 16');
    expect(dateFormatText('iso', FEB_16)).toBe('New recordings are titled like "Untitled meeting · 2026-02-16". Used for every full date; times are unchanged.');
  });
});

describe('folders as a tree (2.1)', () => {
  it('builds the tree from the flat list', () => {
    const f = (path: string, count = 0): FolderInfo => ({ path, name: path.split('/').at(-1) ?? path, parent: path.includes('/') ? path.slice(0, path.lastIndexOf('/')) : null, count, totalCount: count });
    const tree = folderTree([f('Team/Retros'), f('Clients'), f('Team'), f('Clients/Acme')]);
    expect(tree.map((n) => [n.folder.path, n.children.map((c) => c.folder.path)])).toEqual([
      ['Clients', ['Clients/Acme']],
      ['Team', ['Team/Retros']],
    ]);
    expect(folderNameProblem('a/b')).toBe('A folder name can’t contain / or \\.');
    expect(folderNameProblem(' ok ')).toBeNull();
  });

});

describe('Settings (2.1)', () => {
  let h: Harness;

  afterEach(() => {
    h.unmount();
    dateFormat.value = 'yearMonthDay';
  });

  it('General › Date format offers the five presets and saves the one chosen', async () => {
    h = await mountApp({ name: 'settings', section: 'general' });
    await until(() => document.querySelector('.settings-row') !== null);
    expect(selectOf('Date format')?.getAttribute('aria-label')).toBe('Date format: 2026 February 16');
    await click(selectOf('Date format'));
    expect(options().slice(0, 4)).toEqual(['2026 February 16', '16 February 2026', 'February 16, 2026', '2026-02-16']);
    expect(options()[4]).toMatch(/^Windows setting \(.+\)$/);
    await press(document.querySelector('[role="listbox"]'), 'Escape');
    await choose('Date format', '2026-02-16');
    await until(() => h.callsOf('settings.set').length === 1);
    expect(h.callsOf('settings.set')[0]).toEqual({ general: { dateFormat: 'iso' } });
    await until(() => descOf('Date format').includes('Untitled meeting · '));
    expect(descOf('Date format')).toMatch(/"Untitled meeting · \d{4}-\d{2}-\d{2}"/);
  });

  it('Storage › Delete audio tracks older than: exactly the designed ages, Never by default, confirmed in place, Cancel keeps Never', async () => {
    let clock = Date.now();
    h = await mountApp({ name: 'settings', section: 'storage' }, { now: () => clock });
    await until(() => [...document.querySelectorAll('.settings-row-label')].some((l) => l.textContent === 'Delete audio tracks older than'));
    expect(selectOf('Delete audio tracks older than')?.getAttribute('aria-label')).toBe('Delete audio tracks older than: Never');
    expect(descOf('Delete audio tracks older than')).toBe(deleteAudioText(null, null));
    await click(selectOf('Delete audio tracks older than'));
    expect(options()).toEqual(['Never', 'a day', 'a week', 'a month', '90 days', 'a year', '2 years', '3 years', '4 years', '5 years']);
    await press(document.querySelector('[role="listbox"]'), 'Escape');

    // Every sample is older than a day once the clock moves on.
    clock += 3 * 86_400_000;
    await choose('Delete audio tracks older than', 'a day');
    await until(() => rowOf('Delete audio tracks older than').querySelector('[aria-label="Confirm deleting audio"]') !== null);
    const estimate = await h.bridge.call('audio.removeEstimate', { recordingIds: null, olderThanDays: 1 });
    expect(estimate.recordings).toBeGreaterThan(0);
    expect(rowOf('Delete audio tracks older than').textContent).toContain(deleteAudioConfirmText(1, estimate));
    expect(deleteAudioConfirmText(1, estimate)).toContain('This cannot be undone.');
    expect(h.callsOf('settings.set')).toEqual([]);
    await click(button('Cancel'));
    expect(selectOf('Delete audio tracks older than')?.getAttribute('aria-label')).toBe('Delete audio tracks older than: Never');
    expect(h.callsOf('settings.set')).toEqual([]);

    await choose('Delete audio tracks older than', 'a day');
    await until(() => rowOf('Delete audio tracks older than').querySelector('[aria-label="Confirm deleting audio"]') !== null);
    await click(button('Delete audio'));
    await until(() => h.callsOf('settings.set').length === 1);
    expect(h.callsOf('settings.set')[0]).toEqual({ storage: { deleteAudioOlderThanDays: 1 } });
    await until(() => descOf('Delete audio tracks older than').startsWith('Would free'));
    expect([1, 7, 30, 90, 365, 730, 1095, 1460, 1825].map(audioAgeWords)).toEqual(['a day', 'a week', 'a month', '90 days', 'a year', '2 years', '3 years', '4 years', '5 years']);
  });
});

describe('Library folders (2.1, DESIGN.md §6.1)', () => {
  let h: Harness;

  afterEach(() => {
    h.unmount();
  });

  const openFolders = async (): Promise<void> => {
    h = await mountApp({ name: 'library' });
    await until(() => document.querySelector('.lib-row') !== null);
    await click([...document.querySelectorAll<HTMLButtonElement>('[aria-label="Library view"] .seg')].find((b) => b.textContent === 'Folders'));
    await until(() => treeNames().length > 1);
  };
  const folderButton = (name: string): HTMLButtonElement | undefined =>
    [...document.querySelectorAll<HTMLButtonElement>('.folder-select')].find((b) => b.querySelector('.folder-name')?.textContent === name);
  const rowTitles = (): string[] => [...document.querySelectorAll('.lib-row .row-title')].map((t) => t.textContent);

  it('switches to Folders (remembered), shows the tree with counts and lists one folder with search and type still applied', async () => {
    await openFolders();
    await until(() => h.callsOf('settings.set').length === 1);
    expect(h.callsOf('settings.set')[0]).toEqual({ general: { libraryView: 'folders' } });
    expect(treeNames()).toEqual(['Library', 'Clients', 'Hiring', 'Team']);
    expect(document.querySelector('.lib-folder-title')?.textContent).toBe('Library');
    expect(rowTitles()).not.toContain('Interview — Priya Natarajan, product research');

    await click(folderButton('Hiring'));
    await until(() => document.querySelector('.lib-folder-title')?.textContent === 'Hiring');
    await until(() => rowTitles().length === 2);
    expect(rowTitles().sort()).toEqual(['Interview — Marcus Lee, candidate', 'Interview — Priya Natarajan, product research']);
    expect(folderButton('Hiring')?.getAttribute('aria-label')).toBe('Hiring, 2 recordings');
    expect((h.callsOf('library.list').at(-1) as { folder?: string | null }).folder).toBe('Hiring');

    // Opening Clients shows Acme inside it; its count includes Acme's recording.
    await click(document.querySelector<HTMLButtonElement>('[aria-label="Open Clients"]'));
    expect(treeNames()).toEqual(['Library', 'Clients', 'Acme', 'Hiring', 'Team']);
    expect(folderButton('Clients')?.getAttribute('aria-label')).toBe('Clients, 2 recordings, 1 here');

    // Back to Timeline: every recording again.
    await click([...document.querySelectorAll<HTMLButtonElement>('[aria-label="Library view"] .seg')].find((b) => b.textContent === 'Timeline'));
    await until(() => document.querySelector('.folder-pane') === null && rowTitles().length > 10);
  });

  it('creates a folder, files a recording there with Move to folder…, and deletes the folder without deleting the recording', async () => {
    await openFolders();
    await click(button('New folder'));
    expect(dialog()?.querySelector('h2')?.textContent).toBe('New folder');
    const input = document.querySelector<HTMLInputElement>('#folder-name-input');
    if (input === null) {
      throw new Error('no name field');
    }
    input.value = 'a/b';
    input.dispatchEvent(new Event('input', { bubbles: true }));
    await settle();
    expect(dialog()?.textContent).toContain('A folder name can’t contain / or \\.');
    input.value = 'Board';
    input.dispatchEvent(new Event('input', { bubbles: true }));
    await settle();
    await click(button('Create folder'));
    await until(() => document.querySelector('.lib-folder-title')?.textContent === 'Board');
    expect(document.querySelector('.no-match')?.textContent).toContain('This folder has no recordings yet.');

    await click(folderButton('Library'));
    await until(() => rowTitles().includes('Design review: library screen'));
    await click(document.querySelector('[aria-label="More actions for Design review: library screen"]'));
    await click(menuItem('Move to folder…'));
    expect(dialog()?.querySelector('h2')?.textContent).toBe('Move “Design review: library screen” to a folder');
    await until(() => document.querySelectorAll('.move-option').length > 1);
    const target = [...document.querySelectorAll<HTMLLabelElement>('.move-option')].find((l) => l.textContent === 'Board');
    await click(target?.querySelector('input'));
    await click(button('Move to “Board”'));
    await until(() => h.callsOf('library.moveToFolder').length === 1);
    expect(h.callsOf('library.moveToFolder')[0]).toEqual({ recordingIds: ['20261005-160000-dsrev'], folder: 'Board' });
    await until(() => !rowTitles().includes('Design review: library screen'));

    // Delete the folder: the confirmation names it and where the recording goes.
    await click(folderButton('Board'));
    await until(() => rowTitles().includes('Design review: library screen'));
    await click(document.querySelector('[aria-label="More actions for the folder Board"]'));
    await click(menuItem('Delete…'));
    const board: FolderInfo = { path: 'Board', name: 'Board', parent: null, count: 1, totalCount: 1 };
    expect(dialog()?.querySelector('h2')?.textContent).toBe(deleteFolderCopy(board, 0).title);
    expect(dialog()?.textContent).toContain('Its 1 recording move to the Library itself. No recording is deleted, and nothing changes on disk.');
    expect(document.activeElement?.textContent).toBe('Cancel');
    await click(button('Delete folder'));
    await until(() => h.callsOf('folders.delete').length === 1);
    await until(() => document.querySelector('.lib-folder-title')?.textContent === 'Library');
    await until(() => rowTitles().includes('Design review: library screen'));
    await until(() => !treeNames().includes('Board'));
  });
});

describe('Delete audio (2.1, DESIGN.md §6.2)', () => {
  let h: Harness;

  afterEach(() => {
    h.unmount();
  });

  it('confirms with the recording, its length and size and what stays, then keeps the recording listed', async () => {
    h = await mountApp({ name: 'library' });
    await until(() => document.querySelector('.lib-row') !== null);
    const title = 'Weekly 1:1 with Sam';
    await click(document.querySelector(`[aria-label="More actions for ${title}"]`));
    await click(menuItem('Delete audio…'));
    await until(() => dialog() !== null);
    const estimate = h.calls.filter(([m]) => m === 'audio.removeEstimate').at(-1)?.[1];
    expect(estimate).toEqual({ recordingIds: ['20261002-153000-sam11'], olderThanDays: null });
    const recording = (await h.bridge.call('library.list', {})).recordings.find((r) => r.title === title);
    const bytes = (await h.bridge.call('audio.removeEstimate', { recordingIds: ['20261002-153000-sam11'], olderThanDays: null })).bytes;
    const copy = deleteAudioCopy(title, recording?.durationMs ?? 0, bytes, false);
    expect(dialog()?.querySelector('h2')?.textContent).toBe(`Delete the audio of “${title}”?`);
    expect(dialog()?.textContent).toContain(copy.body);
    expect(copy.body).toContain('The transcript and documents stay');
    expect(document.activeElement?.textContent).toBe('Cancel');

    await click(button('Cancel'));
    expect(h.callsOf('audio.remove')).toEqual([]);

    await click(document.querySelector(`[aria-label="More actions for ${title}"]`));
    await click(menuItem('Delete audio…'));
    await until(() => dialog() !== null);
    await click(button('Delete audio'));
    await until(() => h.callsOf('audio.remove').length === 1);
    await until(() => [...document.querySelectorAll('.lib-row')].some((r) => r.textContent.includes(title) && r.textContent.includes('audio deleted')));
    // Not offered again.
    await click(document.querySelector(`[aria-label="More actions for ${title}"]`));
    expect(menuItem('Delete audio…')).toBeUndefined();
    const project = await h.bridge.call('project.get', { recordingId: '20261002-153000-sam11' });
    expect([project.mixUrl, project.peaksUrl, project.audioRemoved?.bytesFreed]).toEqual([null, null, bytes]);
    expect((h.calls.filter(([m]) => m === 'settings.set').at(-1)?.[1] as SettingsSetParams | undefined)?.storage).toBeUndefined();
  });
});
