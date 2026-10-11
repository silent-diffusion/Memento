import { afterEach, describe, expect, it } from 'vitest';
import type { LibraryUsage, SettingsSetParams, VideoSettings, VideoSource } from '../../bridge/types';
import { SAMPLE_CAMERAS, SAMPLE_DISPLAYS } from '../../bridge/mockVideo';
import { button, click, mountApp, press, settle, until, type Harness } from '../../testing/appHarness';
import { ageWords, removeVideoConfirmText, removeVideoText } from './RemoveVideo';
import { videoUsageText } from './sections-m3';
import { defaultCamera, qualityHeight, resolutionOptions } from './VideoSettings';

const DEFAULTS: VideoSettings = {
  defaultSource: 'screenAndCamera',
  resolution: 'display',
  frameRate: 30,
  quality: 'balanced',
  defaultCameraId: null,
  pipCorner: 'bottomRight',
  hardwareEncoder: true,
  includePointer: true,
};

const rowOf = (label: string): HTMLElement => {
  const row = [...document.querySelectorAll<HTMLElement>('.settings-row')].find((r) => r.querySelector('.settings-row-label')?.textContent === label);
  if (row === undefined) {
    throw new Error(`no row ${label}`);
  }
  return row;
};
const descOf = (label: string): string => rowOf(label).querySelector('.settings-row-desc')?.textContent ?? '';
const selectOf = (label: string): HTMLButtonElement | null => rowOf(label).querySelector<HTMLButtonElement>(`[aria-label^="${label}:"]`);
const seg = (group: string, name: string): HTMLButtonElement | undefined =>
  [...document.querySelectorAll<HTMLButtonElement>(`[role="group"][aria-label="${group}"] .seg`)].find((b) => b.textContent.trim() === name);
const choose = async (label: string, option: string): Promise<void> => {
  await click(selectOf(label));
  await click([...document.querySelectorAll('[role="option"]')].find((o) => o.textContent.trim() === option));
};

describe('Settings › Recording › Video defaults (DESIGN.md §18.5)', () => {
  let h: Harness;

  afterEach(() => {
    h.unmount();
  });

  const openRecording = async (mock = {}): Promise<void> => {
    h = await mountApp({ name: 'settings', section: 'recording' }, mock);
    await until(() => [...document.querySelectorAll('.settings-group-label')].some((l) => l.textContent === 'Video defaults'));
    await until(() => h.callsOf('video.listSources').length > 0 && !descOf('Default camera').includes('Looking'));
  };
  const lastRecording = (): SettingsSetParams['recording'] => (h.callsOf('settings.set').at(-1) as SettingsSetParams).recording;

  it('shows Recording mode (Audio only by default) and the eight video rows with their values', async () => {
    await openRecording();
    expect(seg('Recording mode', 'Audio only')?.getAttribute('aria-pressed')).toBe('true');
    expect(seg('Recording mode', 'Audio + video')?.getAttribute('aria-pressed')).toBe('false');
    const groups = [...document.querySelectorAll('.settings-group-label')].map((l) => l.textContent);
    expect(groups.indexOf('Video defaults')).toBe(groups.indexOf('Tracks and storage') + 1);
    const group = [...document.querySelectorAll<HTMLElement>('.settings-group')].find((g) => g.querySelector('.settings-group-label')?.textContent === 'Video defaults');
    expect([...(group?.querySelectorAll('.settings-row-label') ?? [])].map((l) => l.textContent)).toEqual([
      'Default video source',
      'Resolution',
      'Frame rate',
      'Quality',
      'Default camera',
      'Picture-in-picture position',
      'Hardware encoder',
      'Include the mouse pointer',
    ]);
    expect(selectOf('Default video source')?.getAttribute('aria-label')).toBe('Default video source: Screen + camera');
    expect(selectOf('Resolution')?.getAttribute('aria-label')).toBe('Resolution: Match display (2560 × 1440)');
    expect(seg('Frame rate', '30')?.getAttribute('aria-pressed')).toBe('true');
    expect(descOf('Frame rate')).toBe('Higher is smoother and roughly doubles the file size.');
    expect(seg('Quality', 'Balanced')?.getAttribute('aria-pressed')).toBe('true');
    expect(descOf('Quality')).toBe('Balanced is about 12 Mb/s at 1440p.');
    expect(selectOf('Default camera')?.getAttribute('aria-label')).toBe('Default camera: Logitech Brio · 1080p');
    expect(descOf('Default camera')).toBe('Used when Camera is enabled.');
    expect(selectOf('Picture-in-picture position')?.getAttribute('aria-label')).toBe('Picture-in-picture position: Bottom right');
    expect(button('Hardware encoder').getAttribute('aria-checked')).toBe('true');
    expect(button('Hardware encoder').getAttribute('role')).toBe('switch');
    expect(descOf('Hardware encoder')).toBe('Uses the GPU to encode. Falls back to software automatically if it fails, with a notice.');
    expect(button('Include the mouse pointer').getAttribute('aria-checked')).toBe('true');
    expect(descOf('Include the mouse pointer')).toBe('Clicks are not highlighted.');
  });

  it('saves each control as the whole recording block', async () => {
    await openRecording();
    const before = h.store.settings.value?.recording;
    if (before === undefined) {
      throw new Error('no settings');
    }
    await click(seg('Recording mode', 'Audio + video'));
    await until(() => h.callsOf('settings.set').length === 1);
    expect(lastRecording()).toEqual({ ...before, captureVideo: true });

    const steps: [() => Promise<void>, Partial<VideoSettings>][] = [
      [() => choose('Default video source', 'Window'), { defaultSource: 'window' }],
      [() => choose('Resolution', '720p'), { resolution: '720p' }],
      [() => click(seg('Frame rate', '60')), { frameRate: 60 }],
      [() => click(seg('Quality', 'Best')), { quality: 'best' }],
      [() => choose('Default camera', 'Integrated camera · 720p'), { defaultCameraId: 'camera:integrated' }],
      [() => choose('Picture-in-picture position', 'Top left'), { pipCorner: 'topLeft' }],
      [() => click(button('Hardware encoder')), { hardwareEncoder: false }],
      [() => click(button('Include the mouse pointer')), { includePointer: false }],
    ];
    let video = { ...before.video };
    for (const [act, change] of steps) {
      const count = h.callsOf('settings.set').length;
      await act();
      await until(() => h.callsOf('settings.set').length === count + 1);
      video = { ...video, ...change };
      // Every field of the block goes, with the change.
      expect(lastRecording()).toEqual({ ...before, captureVideo: true, video });
    }
    expect(h.store.settings.value?.recording.video).toEqual(video);
  });

  it('states the bitrate for the chosen resolution and frame rate', async () => {
    await openRecording();
    await click(seg('Frame rate', '60'));
    await until(() => descOf('Quality') === 'Balanced is about 20 Mb/s at 1440p.');
    await choose('Resolution', '1080p');
    await until(() => descOf('Quality').endsWith('at 1080p.'));
    await click(seg('Quality', 'Smaller'));
    await until(() => descOf('Quality').startsWith('Smaller is about'));
    expect(descOf('Quality')).toBe('Smaller is about 6.6 Mb/s at 1080p.');
  });

  it('moves through the segmented controls with the arrow keys', async () => {
    await openRecording();
    seg('Frame rate', '24')?.focus();
    await press(document.activeElement, 'ArrowRight');
    expect(document.activeElement).toBe(seg('Frame rate', '30'));
    await press(document.activeElement, 'ArrowRight');
    expect(document.activeElement).toBe(seg('Frame rate', '60'));
    // The select opens from the keyboard.
    await press(selectOf('Picture-in-picture position'), 'ArrowDown');
    await settle(10);
    expect(document.querySelector('[role="listbox"][aria-label="Picture-in-picture position"]')).not.toBeNull();
    expect([...document.querySelectorAll('[role="option"]')].map((o) => o.textContent.trim())).toEqual(['Top left', 'Top right', 'Bottom left', 'Bottom right']);
    await press(document.querySelector('[role="listbox"]'), 'Escape');
  });

  it('says plainly when no camera is found', async () => {
    await openRecording({ video: ['nocamera'] });
    expect(descOf('Default camera')).toBe('No camera was found on this PC. Connect one and open Settings again; screen recordings work without it.');
    expect(rowOf('Default camera').querySelector('.settings-value')?.textContent).toBe('None found');
    expect(selectOf('Default camera')).toBeNull();
  });

  it('works out its figures from the sources', () => {
    const [display] = SAMPLE_DISPLAYS as [VideoSource];
    const [brio, integrated] = SAMPLE_CAMERAS as [VideoSource, VideoSource];
    expect(resolutionOptions(display).map((o) => o.label)).toEqual(['Match display (2560 × 1440)', '1080p', '720p']);
    expect(resolutionOptions(null)[0]?.label).toBe('Match display');
    expect(defaultCamera(DEFAULTS, [brio, integrated])).toBe(brio);
    expect(defaultCamera({ ...DEFAULTS, defaultCameraId: integrated.id }, [brio, integrated])).toBe(integrated);
    expect(defaultCamera({ ...DEFAULTS, defaultCameraId: 'camera:gone' }, [brio])).toBeNull();
    expect(qualityHeight(DEFAULTS, display, brio)).toBe(1440);
    expect(qualityHeight({ ...DEFAULTS, resolution: '720p' }, display, brio)).toBe(720);
    expect(qualityHeight({ ...DEFAULTS, defaultSource: 'camera' }, display, integrated)).toBe(720);
    expect(qualityHeight(DEFAULTS, null, null)).toBe(1080);
  });
});

describe('Settings › Export › Video by default', () => {
  let h: Harness;

  afterEach(() => {
    h.unmount();
  });

  it('ticks Video in What to include by default and saves export.defaults.video', async () => {
    h = await mountApp({ name: 'settings', section: 'export' });
    await until(() => document.querySelector('[role="group"][aria-label="Components"]') !== null);
    const video = [...document.querySelectorAll<HTMLLabelElement>('[aria-label="Components"] .settings-check')].find((l) => l.textContent.startsWith('Video'));
    const box = video?.querySelector<HTMLInputElement>('input');
    expect(box?.disabled).toBe(false);
    expect(box?.checked).toBe(false);
    await click(box);
    await until(() => h.callsOf('settings.set').length === 1);
    expect((h.callsOf('settings.set')[0] as SettingsSetParams).export?.defaults?.video).toEqual({ on: true, format: 'mp4', layout: 'oneFile' });
  });
});

describe('Settings › Storage and history with video (DESIGN.md §18.5)', () => {
  let h: Harness;

  afterEach(() => {
    h.unmount();
  });

  // The preview's recordings are dated relative to its clock; moving the clock on ages them.
  let clock = Date.now();
  const openStorage = async (): Promise<void> => {
    clock = Date.now();
    h = await mountApp({ name: 'settings', section: 'storage' }, { now: () => clock });
    await until(() => [...document.querySelectorAll('.settings-row-label')].some((l) => l.textContent === 'Remove video older than'));
    await until(() => !rowOf('Video').textContent.includes('…'));
  };

  it('shows what video uses beside the other usage rows', async () => {
    await openStorage();
    const usage = await h.bridge.call('library.usage');
    expect(usage.videoRecordings).toBeGreaterThan(0);
    expect(descOf('Video')).toBe(`${usage.videoRecordings} ${usage.videoRecordings === 1 ? 'recording' : 'recordings'} with video`);
    expect(rowOf('Video').querySelector('.settings-value')?.textContent).toMatch(/^[\d.]+ (GB|MB)$/);
    expect(rowOf('Largest recording')).toBeDefined();
  });

  it('saves an age that removes nothing now without asking', async () => {
    await openStorage();
    const estimate = await h.bridge.call('video.removeEstimate', { recordingIds: null, olderThanDays: 365 });
    if (estimate.recordings > 0) {
      return;
    }
    await choose('Remove video older than', '1 year');
    await until(() => h.callsOf('settings.set').length === 1);
    expect(h.callsOf('settings.set')[0]).toEqual({ storage: { removeVideoOlderThanDays: 365 } });
    await until(() => descOf('Remove video older than').startsWith('No video is older than 1 year now.'));
  });

  it('is Never by default, confirms an age that removes video now, and Cancel keeps Never', async () => {
    await openStorage();
    // A year and more later: the recordings with video are older than every age.
    clock += 400 * 86_400_000;
    expect(selectOf('Remove video older than')?.getAttribute('aria-label')).toBe('Remove video older than: Never');
    expect(descOf('Remove video older than')).toBe(removeVideoText(null, null));
    await click(selectOf('Remove video older than'));
    expect([...document.querySelectorAll('[role="option"]')].map((o) => o.textContent.trim())).toEqual(['Never', '30 days', '90 days', '180 days', '1 year']);
    await press(document.querySelector('[role="listbox"]'), 'Escape');

    // The sample with video is a few days old; a year from now, every age would remove it.
    await choose('Remove video older than', '30 days');
    await until(() => rowOf('Remove video older than').querySelector('[aria-label="Confirm removing video"]') !== null);
    const estimate = await h.bridge.call('video.removeEstimate', { recordingIds: null, olderThanDays: 30 });
    expect(estimate.recordings).toBeGreaterThan(0);
    expect(rowOf('Remove video older than').textContent).toContain(removeVideoConfirmText(30, estimate));
    expect(selectOf('Remove video older than')?.getAttribute('aria-label')).toBe('Remove video older than: 30 days');
    expect(h.callsOf('settings.set')).toEqual([]);

    await click(button('Cancel'));
    expect(rowOf('Remove video older than').querySelector('[aria-label="Confirm removing video"]')).toBeNull();
    expect(selectOf('Remove video older than')?.getAttribute('aria-label')).toBe('Remove video older than: Never');
    expect(h.callsOf('settings.set')).toEqual([]);

    await choose('Remove video older than', '30 days');
    await until(() => rowOf('Remove video older than').querySelector('[aria-label="Confirm removing video"]') !== null);
    await click(button('Remove video'));
    await until(() => h.callsOf('settings.set').length === 1);
    expect(h.callsOf('settings.set')[0]).toEqual({ storage: { removeVideoOlderThanDays: 30 } });
    await until(() => descOf('Remove video older than').startsWith('Would free'));
    expect(descOf('Remove video older than')).toBe(removeVideoText(30, estimate));
    expect(selectOf('Remove video older than')?.getAttribute('aria-label')).toBe('Remove video older than: 30 days');

    // Back to Never needs no confirmation.
    await choose('Remove video older than', 'Never');
    await until(() => h.callsOf('settings.set').length === 2);
    expect(h.callsOf('settings.set')[1]).toEqual({ storage: { removeVideoOlderThanDays: null } });
  });

  it('words the effect, the confirmation and the usage by the §17 rules', () => {
    expect(ageWords(365)).toBe('1 year');
    expect(ageWords(90)).toBe('90 days');
    expect(removeVideoText(90, { recordings: 3, bytes: 11.3 * 1024 ** 3 })).toBe(
      'Would free 11.3 GB now, from 3 recordings. Audio tracks, transcript and documents are kept; the Library shows "Video removed".',
    );
    expect(removeVideoText(365, { recordings: 0, bytes: 0 })).toBe(
      'No video is older than 1 year now. Audio tracks, transcript and documents are kept; the Library shows "Video removed".',
    );
    expect(removeVideoText(null, null)).toBe('Video is kept until you remove it. When it is removed, audio tracks, transcript and documents are kept.');
    expect(removeVideoConfirmText(90, { recordings: 1, bytes: 4.4 * 1024 ** 3 })).toBe(
      'Remove the video of 1 recording older than 90 days (4.4 GB)? Audio tracks, transcript and documents are kept, and later recordings lose their video as they pass 90 days. This cannot be undone.',
    );
    const usage = (over: Partial<LibraryUsage>): LibraryUsage => ({
      totalBytes: 0,
      freeBytes: 0,
      count: 0,
      largest: null,
      separateTracksBytes: 0,
      separateTracksRecordings: 0,
      mixOnlyRecordings: 0,
      videoBytes: 0,
      videoRecordings: 0,
      ...over,
    });
    expect(videoUsageText(usage({ videoRecordings: 3 }))).toBe('3 recordings with video');
    expect(videoUsageText(usage({}))).toBe('No recording has video.');
  });
});
