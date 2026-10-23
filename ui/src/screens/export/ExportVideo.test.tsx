import { afterEach, describe, expect, it } from 'vitest';
import type { ExportEstimateParams, ExportRunParams, ProjectVideo } from '../../bridge/types';
import { DEFAULT_VIDEO_EXPORT, everythingOn, validVideoChoice, withVideoFormat, withVideoLayout } from '../../format/export';
import { button, click, mountApp, press, settle, until, type Harness } from '../../testing/appHarness';
import { ESTIMATE_DEBOUNCE_MS } from './ExportDialog';
import { FORMAT_NOTE, ONE_FILE_NOTE, SWITCH_NOTES, TWO_FILES_NOTE, videoRowDescription } from './ExportVideo';

const DESIGN = '20261005-160000-dsrev';
const CS301 = '20261003-110000-cs301';
const BOARD = '20260929-090000-board';
const Q3 = '20261006-100000-q3plan';

describe('Export copies with video (DESIGN.md §18.4)', () => {
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
  const videoRow = (): HTMLElement => {
    const el = dialog().querySelector<HTMLElement>('#exp-video')?.closest<HTMLElement>('.export-row');
    if (el === null || el === undefined) {
      throw new Error('no video row');
    }
    return el;
  };
  const videoBox = (): HTMLInputElement | null => dialog().querySelector<HTMLInputElement>('#exp-video');
  const options = (): HTMLElement | null => dialog().querySelector<HTMLElement>('.export-video-options');
  const layout = (name: string): HTMLButtonElement | undefined =>
    [...dialog().querySelectorAll<HTMLButtonElement>('[aria-label="Screen and camera"] .seg')].find((b) => b.textContent.trim() === name);
  const format = (): HTMLButtonElement | null => dialog().querySelector<HTMLButtonElement>('[aria-label^="Format for Video:"]');
  const chooseFormat = async (option: string): Promise<void> => {
    await click(format());
    await click([...document.querySelectorAll('[role="option"]')].find((o) => o.textContent.trim() === option));
  };
  const estimated = async (): Promise<void> => {
    await settle(ESTIMATE_DEBOUNCE_MS + 60);
    await until(() => /about \d/.test(summary()));
  };

  const open = async (recordingId = DESIGN): Promise<void> => {
    h = await mountApp({ name: 'review', recordingId });
    await until(() => document.querySelector('.review-panes') !== null);
    await click([...document.querySelectorAll<HTMLButtonElement>('.btn.ghost')].find((b) => b.textContent.trim() === 'Export'));
    await until(() => document.querySelector('.export-dialog') !== null && !summary().includes('Estimating'));
  };

  it('describes the recording’s video, sizes it and shows the sub-row only while ticked', async () => {
    await open();
    expect(videoRow().textContent).toContain('Screen and camera, 2560 × 1440 · 30 fps, with the mixed audio');
    expect(videoRow().querySelector('.export-row-size')?.textContent).toMatch(/^[\d.]+ (GB|MB)$/);
    expect(format()?.getAttribute('aria-label')).toBe('Format for Video: MP4');
    expect(format()?.disabled).toBe(true);
    expect(options()).toBeNull();
    expect(summary()).toMatch(/^3 files · /);

    await click(videoBox());
    expect(format()?.disabled).toBe(false);
    expect(options()?.textContent).toContain(`${ONE_FILE_NOTE} ${FORMAT_NOTE}`);
    expect(layout('One file, as recorded')?.getAttribute('aria-pressed')).toBe('true');
    expect(layout('Two files: screen and camera')?.getAttribute('aria-pressed')).toBe('false');
    expect(dialog().querySelector('[role="group"][aria-label="Screen and camera"]')).not.toBeNull();
    // One more file: the screen with the camera inset and the mixed audio.
    expect(summary()).toMatch(/^4 files · /);
  });

  it('two files add the camera file to the footer, and the note follows the choice', async () => {
    await open();
    await click(videoBox());
    const oneFileSize = videoRow().querySelector('.export-row-size')?.textContent ?? '';
    await click(layout('Two files: screen and camera'));
    expect(options()?.textContent).toContain(`${TWO_FILES_NOTE} ${FORMAT_NOTE}`);
    await estimated();
    await until(() => summary().startsWith('5 files · '));
    expect(videoRow().querySelector('.export-row-size')?.textContent).not.toBe(oneFileSize);
    const estimates = h.callsOf('export.estimate') as ExportEstimateParams[];
    expect(estimates.at(-1)?.selection.video).toEqual({ on: true, format: 'mp4', layout: 'twoFiles' });

    await click(layout('One file, as recorded'));
    await estimated();
    await until(() => summary().startsWith('4 files · '));
  });

  it('never sends Original container as one file: Original brings two files, One file brings MP4', async () => {
    await open();
    await click(videoBox());
    await chooseFormat('Original container');
    expect(format()?.getAttribute('aria-label')).toBe('Format for Video: Original');
    expect(layout('Two files: screen and camera')?.getAttribute('aria-pressed')).toBe('true');
    expect(dialog().querySelector('.export-video-switched')?.textContent).toBe(` ${SWITCH_NOTES.toTwoFiles}`);
    expect(dialog().querySelector('.export-video-switched')?.getAttribute('role')).toBe('status');

    await click(layout('One file, as recorded'));
    expect(format()?.getAttribute('aria-label')).toBe('Format for Video: MP4');
    expect(dialog().querySelector('.export-video-switched')?.textContent).toBe(` ${SWITCH_NOTES.toMp4}`);

    await estimated();
    const estimates = h.callsOf('export.estimate') as ExportEstimateParams[];
    for (const e of estimates) {
      expect(e.selection.video?.format === 'original' && e.selection.video.layout === 'oneFile').toBe(false);
    }
    // The pure rules.
    expect(withVideoFormat(DEFAULT_VIDEO_EXPORT, 'original')).toEqual({ on: false, format: 'original', layout: 'twoFiles' });
    expect(withVideoLayout({ on: true, format: 'original', layout: 'twoFiles' }, 'oneFile')).toEqual({ on: true, format: 'mp4', layout: 'oneFile' });
    expect(validVideoChoice({ on: true, format: 'original', layout: 'oneFile' })).toEqual({ on: true, format: 'original', layout: 'twoFiles' });
    expect(validVideoChoice(null)).toEqual(DEFAULT_VIDEO_EXPORT);
    const defaults = h.store.settings.value?.export.defaults;
    if (defaults === undefined) {
      throw new Error('no export defaults');
    }
    expect(everythingOn({ ...defaults, video: { on: false, format: 'original', layout: 'oneFile' } }).video).toEqual({
      on: true,
      format: 'original',
      layout: 'twoFiles',
    });
  });

  it('moves between One file and Two files with the arrow keys', async () => {
    await open();
    await click(videoBox());
    layout('One file, as recorded')?.focus();
    await press(document.activeElement, 'ArrowRight');
    expect(document.activeElement).toBe(layout('Two files: screen and camera'));
    // A focused button: Enter and Space click it (the browser does; jsdom needs the click).
    await click(document.activeElement);
    expect(layout('Two files: screen and camera')?.getAttribute('aria-pressed')).toBe('true');
  });

  it('sends the video choice with export.run and remembers it', async () => {
    await open();
    await click(videoBox());
    await click(layout('Two files: screen and camera'));
    await click(button('Remember these choices'));
    await estimated();
    await click(button('Change export folder'));
    await until(() => (dialog().querySelector('.export-path')?.textContent ?? '').startsWith('E:'));
    await click(dialog().querySelector('.export-foot .btn.p'));
    await until(() => h.callsOf('export.run').length === 1);
    const run = h.callsOf('export.run')[0] as ExportRunParams;
    expect(run.selection.video).toEqual({ on: true, format: 'mp4', layout: 'twoFiles' });
    expect(run.remember).toBe(true);
    await until(() => h.store.settings.value?.export.defaults.video?.layout === 'twoFiles');
    expect(h.store.settings.value?.export.defaults.video).toEqual({ on: true, format: 'mp4', layout: 'twoFiles' });
  });

  it('a screen-only recording has no layout choice and one video file', async () => {
    await open(CS301);
    expect(videoRow().textContent).toContain('Screen, 1920 × 1080 · 30 fps, with the mixed audio');
    await click(videoBox());
    expect(dialog().querySelector('[aria-label="Screen and camera"]')).toBeNull();
    expect(options()?.textContent).toBe(FORMAT_NOTE);
    await chooseFormat('Original container');
    await estimated();
    const estimates = h.callsOf('export.estimate') as ExportEstimateParams[];
    expect(estimates.at(-1)?.selection.video).toEqual({ on: true, format: 'original', layout: 'twoFiles' });
    expect(dialog().querySelector('.export-video-switched')?.textContent).toBe('');
  });

  it('shows the estimate’s reason for a recording whose video was removed or that has none, and sends it off', async () => {
    await open(BOARD);
    expect(videoBox()?.disabled).toBe(true);
    expect(videoBox()?.checked).toBe(false);
    expect(videoRow().classList.contains('export-row--unavailable')).toBe(true);
    expect(videoRow().textContent).toContain('Video was removed');
    expect(videoRow().querySelector('.export-row-size')?.textContent).toBe('—');
    expect(format()?.disabled).toBe(true);
    h.unmount();

    await open(Q3);
    expect(videoBox()?.disabled).toBe(true);
    expect(videoRow().textContent).toContain('No video');
    expect(options()).toBeNull();
  });

  it('words the description from the tracks', () => {
    const track = (id: 'screen' | 'camera', width: number, height: number): ProjectVideo['tracks'][number] => ({
      id,
      sourceKind: id === 'screen' ? 'display' : 'camera',
      sourceName: id,
      width,
      height,
      frameRate: 30,
      encoder: 'x',
      hardware: true,
      framesWritten: 0,
      framesDropped: 0,
      sizeBytes: 0,
      startOffsetMs: 0,
      durationMs: 0,
      endReason: null,
      parts: [],
    });
    const video = (tracks: ProjectVideo['tracks']): ProjectVideo => ({ tracks, pip: { enabled: true, corner: 'bottomRight' }, sizeBytes: 0, removed: null });
    expect(videoRowDescription(video([track('camera', 1920, 1080)]))).toBe('Camera, 1920 × 1080 · 30 fps, with the mixed audio');
    expect(videoRowDescription(video([track('screen', 2560, 1440), track('camera', 1920, 1080)]))).toBe(
      'Screen and camera, 2560 × 1440 · 30 fps, with the mixed audio',
    );
    expect(videoRowDescription(video([]))).toBeNull();
    expect(videoRowDescription(null)).toBeNull();
  });
});
