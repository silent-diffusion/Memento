import { afterEach, describe, expect, it } from 'vitest';
import type { LibraryUsage, SettingsSetParams } from '../../bridge/types';
import { button, click, mountApp, settle, until, type Harness } from '../../testing/appHarness';
import { MIX_ONLY_SPEAKERS } from '../review/PeopleWhoSpoke';
import { keepOnlyMixConfirmText, separateTracksText } from './sections-m3';

const DESIGN = '20261005-160000-dsrev';

const usage = (over: Partial<LibraryUsage>): LibraryUsage => ({
  totalBytes: 9_000_000_000,
  freeBytes: 200_000_000_000,
  count: 4,
  largest: null,
  separateTracksBytes: 3_400_000_000,
  separateTracksRecordings: 3,
  mixOnlyRecordings: 0,
  ...over,
});

describe('Keep only the mix (Settings › Recording and Storage and history, 2.0)', () => {
  let h: Harness;

  afterEach(() => {
    h.unmount();
  });

  const rowOf = (label: string): HTMLElement => {
    const row = [...document.querySelectorAll<HTMLElement>('.settings-row')].find((r) => r.querySelector('.settings-row-label')?.textContent === label);
    if (row === undefined) {
      throw new Error(`no row ${label}`);
    }
    return row;
  };

  it('is off by default in Settings › Recording, states the consequence, and saves the storage block', async () => {
    h = await mountApp({ name: 'settings', section: 'recording' });
    await until(() => document.querySelector('.settings-row') !== null);
    await until(() => [...document.querySelectorAll('.settings-row-label')].some((l) => l.textContent === 'Keep only the mix'));

    expect(button('Keep only the mix').getAttribute('aria-checked')).toBe('false');
    expect(rowOf('Keep only the mix').textContent).toContain("speakers can't be identified per track again and tracks can't be exported");

    await click(button('Keep only the mix'));
    await until(() => h.callsOf('settings.set').length === 1);
    const sent = h.callsOf('settings.set')[0] as SettingsSetParams;
    expect(sent.recording?.storage).toEqual({ codec: 'flac', bitrateKbps: null, downmixMono: false, keepOnlyMix: true });
  });

  it('shows what separate tracks use and removes them from existing recordings only after the confirmation', async () => {
    h = await mountApp({ name: 'settings', section: 'storage' });
    await until(() => [...document.querySelectorAll('.settings-row-label')].some((l) => l.textContent === 'Separate tracks'));
    await until(() => !rowOf('Separate tracks').textContent.includes('…'));
    expect(rowOf('Separate tracks').textContent).toMatch(/In \d+ recordings, beside the mix\./);

    await click(button('Remove tracks…'));
    const confirm = rowOf('Keep only the mix for existing recordings');
    expect(confirm.textContent).toContain('This cannot be undone.');
    expect(h.callsOf('storage.keepOnlyMix')).toEqual([]);

    await click(button('Cancel'));
    expect(rowOf('Keep only the mix for existing recordings').textContent).not.toContain('This cannot be undone.');
    expect(h.callsOf('storage.keepOnlyMix')).toEqual([]);

    await click(button('Remove tracks…'));
    const remove = [...rowOf('Keep only the mix for existing recordings').querySelectorAll('button')].find((b) => b.textContent.startsWith('Remove ') && b.textContent.endsWith(' of tracks'));
    await click(remove);
    await until(() => h.callsOf('storage.keepOnlyMix').length === 1);
    expect(h.callsOf('storage.keepOnlyMix')[0]).toEqual({ recordingIds: null });
    await until(() => document.body.textContent.includes('only the mix. Transcripts, speakers and the mixes were not changed.'), 15_000);
    await until(() => rowOf('Separate tracks').textContent.includes('No recording keeps separate tracks.'), 15_000);
    await settle(20);
    expect(button('Remove tracks…').disabled).toBe(true);
  });

  it('words the usage and the confirmation by the §17 rules', () => {
    expect(separateTracksText(null)).toBe('Each source saved as its own file, beside the mix.');
    expect(separateTracksText(usage({}))).toBe('In 3 recordings, beside the mix.');
    expect(separateTracksText(usage({ separateTracksRecordings: 0, separateTracksBytes: 0, mixOnlyRecordings: 1 }))).toBe(
      'No recording keeps separate tracks. 1 recording already keeps only the mix.',
    );
    expect(keepOnlyMixConfirmText(usage({ separateTracksRecordings: 1 }))).toMatch(
      /^Remove the separate tracks of 1 recording \(3\.\d GB\)\? Each mix, transcript and the speakers found are kept, but speakers can no longer be identified per track and tracks can no longer be exported for them\. This cannot be undone\.$/,
    );
    expect(MIX_ONLY_SPEAKERS).toContain('Reduce and renaming still work.');
  });

  it('leaves Individual tracks out of the Export dialog once only the mix is kept', async () => {
    h = await mountApp({ name: 'settings', section: 'storage' });
    let done = false;
    const off = h.bridge.on('storage.reclaimProgress', (progress) => {
      done ||= progress.state === 'done';
    });
    await h.bridge.call('storage.keepOnlyMix', { recordingIds: [DESIGN] });
    await until(() => done, 15_000);
    off();
    h.store.route.value = { name: 'review', recordingId: DESIGN };
    await until(() => document.querySelector('.review-panes') !== null);
    await settle(50);

    await click([...document.querySelectorAll<HTMLButtonElement>('.spoke-ghost')].find((b) => b.textContent.trim() === 'Export'));
    await until(() => document.querySelector('.export-dialog') !== null);
    await until(() => h.callsOf('project.get').length > 1 && !(document.querySelector('.export-summary')?.textContent ?? 'Estimating').includes('Estimating'));
    await settle(50);

    const names = [...document.querySelectorAll('.export-row-name')].map((n) => n.textContent);
    expect(names).toContain('Audio (mixed)');
    expect(names).not.toContain('Individual tracks');
  });
});
