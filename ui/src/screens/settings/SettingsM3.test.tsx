import { afterEach, describe, expect, it } from 'vitest';
import type { AiSetKeyParams, SettingsSetParams, StorageReclaimParams } from '../../bridge/types';
import type { SettingsSection } from '../../state/router';
import { button, click, mountApp, press, settle, type, until, type Harness } from '../../testing/appHarness';
import { expectNeverSentRows } from '../../testing/neverSent';
import { MASKED_KEY } from './sections-m3';
import { libraryMoveCopy } from './SettingsDialogs';

const FICTIONAL_KEY = 'sk-fictional-key-0123456789abcdef';

describe('Settings completed in M3 (DESIGN.md §11, against the browser-preview host)', () => {
  let h: Harness;

  afterEach(() => {
    h.unmount();
  });

  const open = async (section: SettingsSection, mock = {}): Promise<void> => {
    h = await mountApp({ name: 'settings', section }, mock);
    await until(() => document.querySelector('.settings-row') !== null);
  };
  const labels = (): string[] => [...document.querySelectorAll('.settings-row-label')].map((l) => l.textContent);
  const rowOf = (label: string): HTMLElement => {
    const row = [...document.querySelectorAll<HTMLElement>('.settings-row')].find((r) => r.querySelector('.settings-row-label')?.textContent === label);
    if (row === undefined) {
      throw new Error(`no row ${label}`);
    }
    return row;
  };

  it('gives every row of every section a one-sentence description', async () => {
    await open('general');
    for (const section of ['general', 'ai-privacy', 'documents', 'export', 'storage'] as const) {
      h.store.route.value = { name: 'settings', section };
      await settle(30);
      const rows = [...document.querySelectorAll('.settings-row')];
      expect(rows.length, section).toBeGreaterThan(1);
      for (const row of rows) {
        const description = row.querySelector('.settings-row-desc')?.textContent ?? '';
        expect(description.length, `${section}: ${row.querySelector('.settings-row-label')?.textContent ?? ''}`).toBeGreaterThan(5);
      }
    }
    expect(document.querySelector('.settings-later')).toBeNull();
  });

  it('General: Start with Windows, the stored tray option and Language', async () => {
    await open('general');
    expect(labels()).toEqual([
      'Theme',
      'List density',
      'Start Memento with Windows',
      'Keep running in the tray when closed',
      'Library location',
      'Language',
      'Updates',
      'Install updates automatically',
      'Version',
      'Library',
      'Licenses',
    ]);
    await click(button('Start Memento with Windows'));
    await until(() => h.callsOf('app.setStartup').length === 1);
    expect(h.callsOf('app.setStartup')[0]).toEqual({ startWithWindows: true });
    await until(() => button('Start Memento with Windows').getAttribute('aria-checked') === 'true');
    expect(rowOf('Keep running in the tray when closed').textContent).toContain('Applied in a later version');
    await click(button('Keep running in the tray when closed'));
    await until(() => h.callsOf('settings.set').length === 1);
    expect((h.callsOf('settings.set')[0] as SettingsSetParams).general).toEqual({ startWithWindows: true, keepRunningInTray: false, language: 'en', autoUpdate: true });
    expect(rowOf('Language').textContent).toContain('English');
  });

  it('General: Updates checks by hand, the automatic toggle is stored, and About lists the bundled licenses', async () => {
    await open('general');
    await until(() => h.callsOf('updates.status').length === 1);
    await until(() => rowOf('Updates').textContent.includes('Version 0.5.0'));
    expect(rowOf('Updates').textContent).toContain('Not checked yet');
    await click(button('Check now'));
    await until(() => rowOf('Updates').textContent.includes('Memento 0.5.0 is the newest version.'));
    expect(rowOf('Updates').textContent).toContain('Last checked');
    await click(button('Install updates automatically'));
    await until(() => h.callsOf('settings.set').length === 1);
    expect((h.callsOf('settings.set')[0] as SettingsSetParams).general?.autoUpdate).toBe(false);

    expect(rowOf('Library').textContent).toContain('D:\\Memento Library');
    expect(document.querySelector('#about-licenses')).toBeNull();
    await click(button('Show licenses'));
    await until(() => document.querySelector('#about-licenses') !== null);
    const licenses = document.querySelector('#about-licenses')?.textContent ?? '';
    for (const component of ['Preact', 'Velopack', 'Whisper.net', 'org.k2fsa.sherpa.onnx', 'NAudio.Core', 'Manrope (font)']) {
      expect(licenses).toContain(component);
    }
    expect(licenses).not.toContain('|');
    expect(button('Hide licenses').getAttribute('aria-expanded')).toBe('true');
  });

  it('General: moving the library asks first, explains copy-verify-delete, then shows progress', async () => {
    await open('general');
    await click(button('Change library location'));
    await until(() => document.querySelector('[role="dialog"]') !== null);
    expect(document.querySelector('[role="dialog"] h2')?.textContent).toBe('Move the library?');
    expect(document.querySelector('[role="dialog"] .dialog-body')?.textContent).toBe(libraryMoveCopy('D:\\Memento Library', 'E:\\Recordings\\Memento'));
    expect(document.querySelector('[role="dialog"] .dialog-body')?.textContent).toContain('checks each copied file against its SHA-256 hash');
    // Cancel is the default and Esc cancels.
    expect(document.activeElement?.textContent).toBe('Cancel');
    await press(document.querySelector('[role="dialog"]'), 'Escape');
    expect(h.callsOf('library.move')).toEqual([]);

    await click(button('Change library location'));
    await until(() => document.querySelector('[role="dialog"]') !== null);
    await click(button('Move library'));
    await until(() => document.querySelector('.settings-move-banner') !== null);
    expect(document.querySelector('.settings-move-banner')?.textContent).toMatch(/Moving the library to E:\\Recordings\\Memento · \d+%/);
    expect(button('Change library location').disabled).toBe(true);
    await until(() => h.store.settings.value?.libraryPath === 'E:\\Recordings\\Memento', 10_000);
    await until(() => document.querySelector('.settings-move-banner') === null);
    expect(rowOf('Library location').textContent).toContain('the old folder was removed');
  });

  it('General: a busy library refuses the move in the dialog', async () => {
    await open('general', { m3: { move: 'busy' } });
    await click(button('Change library location'));
    await until(() => document.querySelector('[role="dialog"]') !== null);
    await click(button('Move library'));
    await until(() => document.querySelector('.dialog-error') !== null);
    expect(document.querySelector('.dialog-error')?.textContent).toContain('nothing was moved');
  });

  it('AI and privacy: off by default, masked keys, the key dialog never echoes the key, and the share checklist', async () => {
    await open('ai-privacy');
    expect(labels()).toEqual([
      'Allow external AI services',
      'Ask before every send',
      'Keep a record of what was sent',
      'Claude (Anthropic)',
      'ChatGPT (OpenAI)',
      'Allowed data',
      // M4
      'Default provider',
      'Local model',
    ]);
    expect(button('Allow external AI services').getAttribute('aria-checked')).toBe('false');
    expect(rowOf('Claude (Anthropic)').querySelector('.settings-key')?.textContent).toBe(MASKED_KEY);
    expect(rowOf('ChatGPT (OpenAI)').textContent).toContain('Not configured.');

    await click(button('Add a ChatGPT (OpenAI) key'));
    const input = document.querySelector<HTMLInputElement>('#ai-key-input');
    expect(input?.type).toBe('password');
    expect(document.activeElement).toBe(input);
    await type(input, FICTIONAL_KEY);
    await click(button('Save key'));
    await until(() => document.querySelector('#ai-key-input') === null);
    expect((h.callsOf('ai.setKey')[0] as AiSetKeyParams).provider).toBe('openai');
    // The key went to the host once and is shown nowhere: not in the page, not in the store.
    expect(document.body.innerHTML).not.toContain(FICTIONAL_KEY);
    expect(JSON.stringify(h.store.settings.value)).not.toContain(FICTIONAL_KEY);
    expect(rowOf('ChatGPT (OpenAI)').querySelector('.settings-key')?.textContent).toBe(MASKED_KEY);

    await click(button('Replace the Claude (Anthropic) key'));
    expect(document.querySelector('[role="dialog"] h2')?.textContent).toBe('Replace the Claude key');
    await type(document.querySelector('#ai-key-input'), 'short');
    await click(button('Replace key'));
    await until(() => document.querySelector('.dialog-error') !== null);
    expect(document.querySelector('.dialog-error')?.textContent).toContain('8 to 500 characters with no spaces. Nothing was saved.');
    expect(document.body.textContent).not.toContain('short');
    await click(button('Cancel'));

    await click(button('Remove the Claude (Anthropic) key'));
    await until(() => rowOf('Claude (Anthropic)').querySelector('.settings-key') === null);
    expect(h.callsOf('ai.clearKey')).toEqual([{ provider: 'anthropic' }]);

    const checks = [...rowOf('Allowed data').querySelectorAll<HTMLInputElement>('input[type="checkbox"]')];
    expect(checks.map((c) => [c.closest('label')?.textContent, c.checked, c.disabled])).toEqual([
      ['Transcript', true, false],
      ['Recording detailstitle, date, type', true, false],
      ['Participants', true, false],
      ['Agenda and imported documents', true, false],
      ['Highlights and notes', true, false],
      ['Attachments', false, false],
    ]);
    // Audio and video are never sent: greyed rows with a lock and "never sent", no checkbox.
    expectNeverSentRows([...rowOf('Allowed data').querySelectorAll('.never-sent')]);
    expect(rowOf('Allowed data').querySelector('.never-sent')?.previousElementSibling?.textContent).toBe('Attachments');
    await click(checks[5]);
    await until(() => h.callsOf('settings.set').length === 1);
    expect((h.callsOf('settings.set')[0] as SettingsSetParams).ai?.share?.attachments).toBe(true);
    expect((h.callsOf('settings.set')[0] as SettingsSetParams).ai).not.toHaveProperty('providers');
  });

  it('Export: external copies, default components and formats', async () => {
    await open('export');
    expect(labels()).toEqual([
      'Save copies outside Memento',
      'Default folder',
      'Ask where to save each time',
      'Put each export in its own folder',
      'Components',
      'Transcript',
      'Audio',
      'Documents',
    ]);
    expect(button('Save copies outside Memento').getAttribute('aria-checked')).toBe('false');
    expect(rowOf('Default folder').textContent).toContain('D:\\Exports');
    await click(button('Ask where to save each time'));
    await until(() => h.store.settings.value?.export.askWhereEachTime === false);
    const tracks = [...rowOf('Components').querySelectorAll<HTMLInputElement>('input')].find((i) => i.closest('label')?.textContent === 'Individual tracks');
    await click(tracks);
    await until(() => h.store.settings.value?.export.defaults.tracks.on === true);
    await click([...rowOf('Transcript').querySelectorAll('.seg')].find((b) => b.textContent === 'SRT'));
    await until(() => h.store.settings.value?.export.defaults.transcript.formats[0] === 'srt');
    await click(button('Change the default export folder'));
    await until(() => h.store.settings.value?.export.defaultFolder === 'E:\\Recordings\\Memento');
    // M4: documents are exported too, all of a recording's by default, in the chosen format.
    const documents = [...rowOf('Components').querySelectorAll<HTMLInputElement>('input')].find((i) => i.closest('label')?.textContent.startsWith('Documents') === true);
    expect(documents?.disabled).toBe(false);
    await click(documents);
    await until(() => h.store.settings.value?.export.defaults.documents.on === true);
    await click(rowOf('Documents').querySelector('.select-btn'));
    await click([...document.querySelectorAll('[role="option"]')].find((o) => o.textContent.trim() === 'PDF'));
    await until(() => h.store.settings.value?.export.defaults.documents.format === 'pdf');
  });

  it('Storage: usage, reclaim with progress, Review large recordings and Rebuild index', async () => {
    await open('storage');
    await until(() => !rowOf('Library size').textContent.includes('…'));
    expect(rowOf('Library size').textContent).toContain('14 recordings, all on this PC.');
    expect(rowOf('Free space').textContent).toContain('212 GB');
    expect(rowOf('Largest recording').textContent).toContain('Design review: library screen');

    expect(button('Run now').disabled).toBe(true);
    await click(document.querySelector('[aria-label^="Downmix tracks older than:"]'));
    await click([...document.querySelectorAll('[role="option"]')].find((o) => o.textContent.trim() === '30 days'));
    await until(() => h.store.settings.value?.storage.reclaimOlderThanDays === 30);
    await click(button('Run now'));
    await until(() => h.callsOf('storage.reclaim').length === 1);
    expect(h.callsOf('storage.reclaim')[0] as StorageReclaimParams).toEqual({ recordingIds: null, downmixMono: true, codec: 'aac', bitrateKbps: 160 });
    await until(() => document.querySelector('.settings-progress') !== null || document.querySelector('.settings-inline--ok') !== null);
    await until(() => document.querySelector('.settings-inline--ok') !== null, 10_000);
    expect(document.querySelector('.settings-inline--ok')?.textContent).toMatch(/made smaller, freeing .+\. Transcripts were not touched\./);

    await click(button('Rebuild'));
    await until(() => document.querySelector('.toast') !== null);
    expect(document.querySelector('.toast-title')?.textContent).toBe('The library index was rebuilt');

    await click(button('Review'));
    await until(() => h.store.route.value.name === 'library');
    expect(h.store.libraryView.value.sort).toBe('size');
    await until(() => document.querySelector('.lib-group-label')?.textContent === 'Largest first');
  });

  it('Storage: Open goes to the largest recording', async () => {
    await open('storage');
    await until(() => document.querySelector('.settings-open') !== null);
    await click(button('Open Design review: library screen'));
    expect(h.store.route.value).toEqual({ name: 'review', recordingId: '20261005-160000-dsrev' });
  });

  it('moves between the settings rows with the keyboard', async () => {
    await open('ai-privacy');
    const toggle = button('Allow external AI services');
    toggle.focus();
    expect(document.activeElement).toBe(toggle);
    // Toggles are real switches: Space and Enter reach them as clicks.
    await click(toggle);
    await until(() => toggle.getAttribute('aria-checked') === 'true');
    expect(document.querySelector('.ai-card')).toBeNull();
  });
});
