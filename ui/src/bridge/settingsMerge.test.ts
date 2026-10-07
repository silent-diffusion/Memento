import { describe, expect, it } from 'vitest';
import { createBridgeClient } from './client';
import { mergeExport, mergeSettings } from './settingsMerge';
import type { SettingsSnapshot } from './types';

const quiet = { info: (): void => undefined, warn: (): void => undefined };

async function snapshot(): Promise<SettingsSnapshot> {
  const bridge = createBridgeClient({ logger: quiet, mock: { live: false, recovery: false } });
  const settings = await bridge.call('settings.get');
  return { ...settings, export: { ...settings.export, defaultFolder: 'D:\\Exports' }, storage: { reclaimOlderThanDays: 90 } };
}

describe('settings.set merge (as the host merges it)', () => {
  it('keeps a field that is missing or null, and clears the export folder and reclaim age on null', async () => {
    const before = await snapshot();

    expect(mergeSettings(before, { export: { askWhereEachTime: false } }).export).toEqual({ ...before.export, askWhereEachTime: false });
    expect(mergeSettings(before, { export: { defaultFolder: null } }).export.defaultFolder).toBeNull();
    expect(mergeSettings(before, { storage: {} }).storage.reclaimOlderThanDays).toBe(90);
    expect(mergeSettings(before, { storage: { reclaimOlderThanDays: null } }).storage.reclaimOlderThanDays).toBeNull();
    expect(mergeSettings(before, { general: { keepRunningInTray: true } }).general).toEqual({ ...before.general, keepRunningInTray: true });
    expect(mergeExport(before.export, null)).toBe(before.export);
  });

  it('merges ai.share field by field and never takes providers from a patch', async () => {
    const before = await snapshot();

    const after = mergeSettings(before, { ai: { share: { attachments: true } } });

    expect(after.ai.share).toEqual({ ...before.ai.share, attachments: true });
    expect(after.ai.enabled).toBe(before.ai.enabled);
    expect(after.ai.providers).toBe(before.ai.providers);
  });

  it('replaces export.defaults and the recording block whole', async () => {
    const before = await snapshot();
    const defaults = { ...before.export.defaults, details: { on: !before.export.defaults.details.on } };

    expect(mergeSettings(before, { export: { defaults } }).export.defaults).toBe(defaults);
    expect(mergeSettings(before, { recording: before.recording }).recording).toBe(before.recording);
  });

  it('the browser-preview host clears the folder and age the same way', async () => {
    const bridge = createBridgeClient({ logger: quiet, mock: { live: false, recovery: false } });
    await bridge.call('settings.set', { export: { defaultFolder: 'E:\\Copies' }, storage: { reclaimOlderThanDays: 30 } });

    const cleared = await bridge.call('settings.set', { export: { defaultFolder: null }, storage: { reclaimOlderThanDays: null } });
    const kept = await bridge.call('settings.set', { export: { createSubfolder: false } });

    expect([cleared.export.defaultFolder, cleared.storage.reclaimOlderThanDays]).toEqual([null, null]);
    expect(kept.export.defaultFolder).toBeNull();
    expect(kept.export.createSubfolder).toBe(false);
  });
});
