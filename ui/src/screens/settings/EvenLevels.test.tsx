import { afterEach, describe, expect, it } from 'vitest';
import type { Project, SettingsSetParams } from '../../bridge/types';
import { button, click, mountApp, until, type Harness } from '../../testing/appHarness';
import { REBUILD_AS_RECORDED, REBUILD_EVENED, rebuildMixAction } from '../review/mixRebuild';

describe('Even out source levels (Settings › Recording, after 2.0.0-beta.1)', () => {
  let h: Harness;

  afterEach(() => {
    h.unmount();
  });

  it('is on by default, says what it does, and saves the storage block', async () => {
    h = await mountApp({ name: 'settings', section: 'recording' });
    await until(() => [...document.querySelectorAll('.settings-row-label')].some((l) => l.textContent === 'Even out source levels'));
    const row = [...document.querySelectorAll<HTMLElement>('.settings-row')].find((r) => r.querySelector('.settings-row-label')?.textContent === 'Even out source levels');

    expect(button('Even out source levels').getAttribute('aria-checked')).toBe('true');
    expect(row?.textContent).toContain('a quiet microphone is lifted to match the loudest source');
    expect(row?.textContent).toContain("Each source's own track stays as recorded");

    await click(button('Even out source levels'));
    await until(() => h.callsOf('settings.set').length === 1);
    const sent = h.callsOf('settings.set')[0] as SettingsSetParams;
    expect(sent.recording?.storage).toEqual({ codec: 'flac', bitrateKbps: null, downmixMono: false, keepOnlyMix: false, evenLevels: false });
  });
});

describe('Rebuild the mix (Review › More › Reprocess)', () => {
  const project = (over: Partial<Project>): Project => ({ mixLevels: null, mixOnly: null, ...over }) as Project;

  it('offers evened levels for a mix summed as recorded, and the way back once evened', () => {
    const calls: boolean[] = [];
    const evened = rebuildMixAction(project({}), false, false, (even) => calls.push(even));
    expect(evened).toMatchObject({ label: REBUILD_EVENED, disabled: false });
    evened.run();

    const back = rebuildMixAction(project({ mixLevels: { at: '2026-10-11T10:00:00+02:00', tracks: [{ trackId: 'mic', levelDbfs: -53.9, gainDb: 24 }] } }), false, false, (even) => calls.push(even));
    expect(back).toMatchObject({ label: REBUILD_AS_RECORDED, disabled: false });
    back.run();

    expect(calls).toEqual([true, false]);
  });

  it('says why it is not offered', () => {
    const none = (): undefined => undefined;
    expect(rebuildMixAction(project({}), true, false, none)).toMatchObject({ disabled: true, note: 'Once the recording is stored' });
    expect(rebuildMixAction(project({ mixOnly: { at: '2026-10-11T10:00:00+02:00', tracks: 2, bytesFreed: 1 } }), false, false, none)).toMatchObject({ disabled: true, note: 'Only the mix was kept' });
    expect(rebuildMixAction(project({}), false, true, none)).toMatchObject({ disabled: true, note: 'Rebuilding the mix now' });
    expect(rebuildMixAction(null, false, false, none)).toMatchObject({ disabled: true });
  });
});
