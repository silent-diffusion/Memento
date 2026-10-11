import { afterEach, describe, expect, it } from 'vitest';
import { MOCK_UPDATE_VERSION } from '../../bridge/mockLibraryExtra';
import type { UpdateStatus } from '../../bridge/types';
import { button, click, mountApp, until, type Harness } from '../../testing/appHarness';

/** Settings › General › Updates: Check now only checks, Update now downloads (2.1.0, DESIGN.md §11). */
describe('Updates panel: Check now and Update now separated', () => {
  let h: Harness;

  afterEach(() => {
    h.unmount();
  });

  const open = async (mock = {}): Promise<void> => {
    h = await mountApp({ name: 'settings', section: 'general' }, mock);
    await until(() => h.callsOf('updates.status').length === 1);
    await until(() => row().textContent.includes('Not checked yet'));
  };
  const row = (): HTMLElement => {
    const found = [...document.querySelectorAll<HTMLElement>('.settings-row')].find((r) => r.querySelector('.settings-row-label')?.textContent === 'Updates');
    if (found === undefined) {
      throw new Error('no Updates row');
    }
    return found;
  };
  const hasButton = (name: string): boolean => [...row().querySelectorAll('button')].some((b) => b.textContent.trim() === name);
  const base: UpdateStatus = { currentVersion: '0.5.0', state: 'idle', availableVersion: null, percent: null, lastCheckedAt: null, message: null, deferred: false };

  it('on the newest version, Check now says so with the running version and downloads nothing', async () => {
    await open();
    await click(button('Check now'));
    await until(() => row().textContent.includes('You are on the newest version (0.5.0).'));
    expect(h.callsOf('updates.download')).toEqual([]);
    expect(hasButton('Update now')).toBe(false);
    expect(row().querySelector('.update-notes')).toBeNull();
  });

  it('with a newer version, Check now shows it and its release notes, and only Update now downloads', async () => {
    await open({ m3: { update: 'available' } });
    await click(button('Check now'));
    await until(() => row().textContent.includes(`Memento ${MOCK_UPDATE_VERSION} is available.`));

    const notes = row().querySelector('.update-notes');
    expect(notes?.getAttribute('aria-label')).toBe(`Release notes for Memento ${MOCK_UPDATE_VERSION}`);
    expect(notes?.querySelector('.update-notes-heading')?.textContent).toBe('What’s new');
    expect([...(notes?.querySelectorAll('li') ?? [])].map((li) => li.textContent)[0]).toBe('Check now and Update now are separate: checking never downloads.');
    expect(notes?.innerHTML).not.toContain('**');
    expect(row().querySelector('.settings-progress')).toBeNull();
    expect(h.callsOf('updates.download')).toEqual([]);
    expect(hasButton('Check now')).toBe(false);

    await click(button('Update now'));
    expect(h.callsOf('updates.download')).toHaveLength(1);
    await until(() => row().querySelector('.settings-progress') !== null);
    expect(row().textContent).toContain(`Downloading Memento ${MOCK_UPDATE_VERSION}`);
    await until(() => hasButton(`Restart to update to ${MOCK_UPDATE_VERSION}`), 5000);
    expect(row().textContent).toContain(`Memento ${MOCK_UPDATE_VERSION} is downloaded and installs when you restart.`);
  });

  it('a version with no release notes says so instead of an empty block', async () => {
    await open();
    h.store.updates.value = { ...base, state: 'available', availableVersion: '0.6.0', releaseNotes: null };
    await until(() => row().textContent.includes('This release has no release notes.'));
    expect(hasButton('Update now')).toBe(true);
  });

  it('shows the download progress, then Restart to update, and a failure as an alert with Check now', async () => {
    await open();
    h.store.updates.value = { ...base, state: 'downloading', availableVersion: '0.6.0', percent: 40 };
    await until(() => row().textContent.includes('Downloading Memento 0.6.0 · 40%'));
    expect(button('Check now').hasAttribute('disabled')).toBe(true);

    h.store.updates.value = { ...base, state: 'ready', availableVersion: '0.6.0', percent: 100 };
    await until(() => hasButton('Restart to update to 0.6.0'));

    const failure =
      'Memento 0.6.0 could not be downloaded: the update could not be saved (disk full). Nothing was changed and this version keeps working as it is. Choose Check now to try again.';
    h.store.updates.value = { ...base, state: 'failed', message: failure };
    await until(() => row().querySelector('[role="alert"]')?.textContent === failure);
    expect(hasButton('Check now')).toBe(true);
  });
});
