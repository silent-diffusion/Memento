// Review with video (DESIGN.md §18.3) against the browser-preview host: the well with the screen and the
// camera inset, the overlay row, Audio only, Fullscreen, the removed line, the outline's Video block and
// Remove video, the header meta and the details. jsdom plays no media: play() and pause() are stubbed to
// flip `paused` and fire their events, and the mix's readyState is set so seeks reach currentTime.
import { act } from 'preact/test-utils';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { Project } from '../../bridge/types';
import { button, click, mountApp, press, settle, until, type Harness } from '../../testing/appHarness';

const NOW = new Date(2026, 9, 6, 15, 0);
const DESIGN_REVIEW = '20261005-160000-dsrev';
const CS301 = '20261003-110000-cs301';
const TOWN_HALL = '20260918-160000-thall';
const BOARD = '20260929-090000-board';
const INTERVIEW = '20261006-141500-pnint';
const Q3_PLAN = '20261006-100000-q3plan';

describe('Review with video (against the browser-preview host)', () => {
  let h: Harness;
  const paused = new WeakMap<HTMLMediaElement, boolean>();
  const pausedDescriptor = Object.getOwnPropertyDescriptor(HTMLMediaElement.prototype, 'paused');

  beforeEach(() => {
    Object.defineProperty(HTMLMediaElement.prototype, 'paused', {
      configurable: true,
      get(this: HTMLMediaElement) {
        return paused.get(this) ?? true;
      },
    });
    vi.spyOn(HTMLMediaElement.prototype, 'play').mockImplementation(function (this: HTMLMediaElement) {
      paused.set(this, false);
      this.dispatchEvent(new Event('play'));
      return Promise.resolve();
    });
    vi.spyOn(HTMLMediaElement.prototype, 'pause').mockImplementation(function (this: HTMLMediaElement) {
      paused.set(this, true);
      this.dispatchEvent(new Event('pause'));
    });
  });

  afterEach(() => {
    h.unmount();
    vi.restoreAllMocks();
    if (pausedDescriptor !== undefined) {
      Object.defineProperty(HTMLMediaElement.prototype, 'paused', pausedDescriptor);
    }
  });

  const open = async (recordingId: string, video: string[] = []): Promise<void> => {
    h = await mountApp({ name: 'review', recordingId }, video.length === 0 ? {} : { video: video as never }, NOW);
    await until(() => h.container.querySelector('.review-panes') !== null);
    // The mix "has loaded", so the player's seeks reach the element.
    const audio = h.container.ownerDocument.querySelector('audio');
    if (audio !== null) {
      Object.defineProperty(audio, 'readyState', { configurable: true, value: 4 });
    }
  };

  const q = (selector: string): HTMLElement | null => h.container.querySelector<HTMLElement>(selector);
  const videoOf = (role: 'screen' | 'camera'): HTMLVideoElement | null => h.container.querySelector<HTMLVideoElement>(`video[data-track="${role}"]`);
  const meta = (): string => q('.spoke-meta')?.textContent ?? '';
  const fact = (name: string): string | null => {
    const dt = [...h.container.querySelectorAll('.facts dt')].find((d) => d.textContent === name);
    return dt?.nextElementSibling?.textContent ?? null;
  };

  it('shows the screen with the camera inset, the caption and the overlay row (Design review)', async () => {
    await open(DESIGN_REVIEW);
    const well = q('.vid');
    expect(well?.getAttribute('aria-label')).toBe('Video');
    const screen = videoOf('screen');
    const camera = videoOf('camera');
    expect(screen?.getAttribute('src')).toBe(`blob:memento-preview/${DESIGN_REVIEW}/screen/0`);
    expect(camera?.getAttribute('src')).toBe(`blob:memento-preview/${DESIGN_REVIEW}/camera/0`);
    // Muted (the mix carries the sound), no controls: the player strip's are the only ones.
    expect(screen?.muted).toBe(true);
    expect(camera?.muted).toBe(true);
    expect(screen?.hasAttribute('controls')).toBe(false);
    expect(q('.vid-cam')?.classList.contains('vid-cam--bottomRight')).toBe(true);
    expect(q('.vid-caption')?.textContent).toBe('Opening and goals · 0:00');
    // The well sits in the player strip above the waveform.
    expect(q('.player > .vid-wrap + .player-wave-anchor .wave')).not.toBeNull();
    const overlay = [...h.container.querySelectorAll<HTMLButtonElement>('.vid-ovl button')];
    expect(overlay.map((b) => b.getAttribute('aria-label') ?? b.textContent.trim())).toEqual([
      'Camera inset',
      'Top left',
      'Top right',
      'Bottom left',
      'Bottom right',
      'Audio only',
      'Fullscreen',
    ]);
    // Every overlay control is a real, enabled button in the tab order.
    expect(overlay.every((b) => b.type === 'button' && !b.disabled && b.tabIndex === 0)).toBe(true);
    expect(q('.vid-corners')?.getAttribute('role')).toBe('group');
    expect(q('.vid-corners')?.getAttribute('aria-label')).toBe('Inset corner');
  });

  it('toggles the camera inset and moves it between corners; nothing is saved', async () => {
    await open(DESIGN_REVIEW);
    expect(button('Camera inset').getAttribute('aria-pressed')).toBe('true');
    expect(button('Bottom right').getAttribute('aria-pressed')).toBe('true');
    await click(button('Top left'));
    expect(button('Top left').getAttribute('aria-pressed')).toBe('true');
    expect(button('Bottom right').getAttribute('aria-pressed')).toBe('false');
    expect(q('.vid-cam')?.classList.contains('vid-cam--topLeft')).toBe(true);
    await click(button('Camera inset'));
    expect(button('Camera inset').getAttribute('aria-pressed')).toBe('false');
    expect(q('.vid-corners')).toBeNull();
    expect(q('.vid-cam')).toBeNull();
    expect(videoOf('camera')).toBeNull();
    await click(button('Camera inset'));
    expect(q('.vid-cam--topLeft')).not.toBeNull();
    expect(h.calls.map(([m]) => m).filter((m) => m.startsWith('video.') || m === 'recording.setPip')).toEqual([]);
  });

  it('starts the inset where it was when recording ended (Town hall: top right)', async () => {
    await open(TOWN_HALL);
    expect(button('Top right').getAttribute('aria-pressed')).toBe('true');
    expect(q('.vid-cam--topRight')).not.toBeNull();
  });

  it('shows a screen-only recording without the inset controls (CS301)', async () => {
    await open(CS301);
    expect(videoOf('screen')).not.toBeNull();
    expect(videoOf('camera')).toBeNull();
    expect(h.container.textContent).not.toContain('Camera inset');
    expect(q('.vid-corners')).toBeNull();
    expect(meta()).toMatch(/ · screen · [\d.]+ GB video$/);
    expect(fact('Video')).toBe('1920 × 1080 · 30 fps · hardware encoder · 3 dropped frames');
  });

  it('follows the one playhead: seeks, play, pause and speed of the mix', async () => {
    await open(DESIGN_REVIEW);
    const screen = videoOf('screen');
    const camera = videoOf('camera');
    // A chapter seeks the mix; both videos follow to the same moment.
    await click(h.container.querySelector('button[aria-label^="Play from 18:10"]'));
    expect(screen?.currentTime).toBe(1090);
    expect(camera?.currentTime).toBe(1090);
    expect(q('.vid-caption')?.textContent).toBe('Row density and status pills · 18:10');
    // Skip forward 10 s.
    await click(button('Forward 10 seconds'));
    expect(screen?.currentTime).toBe(1100);
    // Play: the videos play with the mix, never on their own clock.
    await click(button('Play'));
    expect(screen?.paused).toBe(false);
    expect(camera?.paused).toBe(false);
    // Speed follows.
    await click(button('Playback speed: 1.0×'));
    await click(button('1.5×'));
    expect(screen?.playbackRate).toBe(1.5);
    expect(camera?.playbackRate).toBe(1.5);
    await click(button('Pause'));
    expect(screen?.paused).toBe(true);
    expect(camera?.paused).toBe(true);
  });

  it('holds the last frame at the end when the tracks ended with the recording', async () => {
    await open(DESIGN_REVIEW);
    await press(q('.scrubber'), 'End');
    expect(videoOf('screen')?.currentTime).toBeCloseTo(4201.999, 3);
    expect(q('.vid-still')).toBeNull();
    expect(q('.vid-cam--off')).toBeNull();
  });

  it('shows a calm still outside a track instead of a frozen frame: a camera that started late, a window that closed', async () => {
    await open(DESIGN_REVIEW);
    // The host's answer with a screen track that ended at 00:58:12 and a camera that started at 00:01:10.
    const original = h.bridge.call.bind(h.bridge) as (m: string, p?: unknown) => Promise<unknown>;
    (h.bridge as { call: unknown }).call = async (method: string, params?: unknown) => {
      const result = await original(method, params);
      if (method !== 'project.get') {
        return result;
      }
      const project = result as Project;
      const video = project.video;
      if (video === null) {
        return project;
      }
      const tracks = video.tracks.map((t) =>
        t.id === 'screen'
          ? { ...t, durationMs: 3_492_000, endReason: "'Zoom Meeting' was closed at 00:58:12.", parts: [{ ...(t.parts[0] ?? { url: null, startOffsetMs: 0 }), durationMs: 3_492_000 }] }
          : { ...t, startOffsetMs: 70_000, durationMs: t.durationMs - 70_000, parts: [{ url: t.parts[0]?.url ?? null, startOffsetMs: 70_000, durationMs: t.durationMs - 70_000 }] },
      );
      return { ...project, video: { ...video, tracks } };
    };
    await act(async () => {
      h.store.route.value = { name: 'library' };
      await Promise.resolve();
    });
    await act(async () => {
      h.store.route.value = { name: 'review', recordingId: DESIGN_REVIEW };
      await Promise.resolve();
    });
    await until(() => videoOf('screen') !== null);
    // 0:00: the screen plays; the camera has not started, so the inset is not drawn (no frozen frame).
    expect(q('.vid-cam--off')).not.toBeNull();
    expect(videoOf('camera')).toBeNull();
    await click(h.container.querySelector('button[aria-label^="Play from 6:30"]'));
    expect(q('.vid-cam--off')).toBeNull();
    expect(videoOf('camera')?.currentTime).toBe(320);
    // After the window closed: its end reason, the camera still plays in its corner.
    await click(h.container.querySelector('button[aria-label^="Play from 1:01:20"]'));
    expect(videoOf('screen')).toBeNull();
    expect(q('.vid-still')?.textContent).toBe("'Zoom Meeting' was closed at 00:58:12.Audio and the transcript continue.");
    expect(videoOf('camera')?.currentTime).toBe(3610);
    await press(q('.scrubber'), 'Home');
    expect(videoOf('screen')?.currentTime).toBe(0);
    expect(q('.vid-still')).toBeNull();
  });

  it('says the video is being prepared while its parts cannot be read yet', async () => {
    await open(Q3_PLAN, ['encoding']);
    expect(videoOf('screen')).toBeNull();
    expect(q('.vid-still-lead')?.textContent).toBe('The video is being prepared.');
    expect(q('.vid-still-detail')?.textContent).toBe('Encoding 42% · GPU');
  });

  it('hides the video with Audio only and brings it back with Show video, moving focus', async () => {
    await open(DESIGN_REVIEW);
    await click(button('Audio only'));
    expect(q('.vid')).toBeNull();
    expect(q('.vid-line')?.textContent).toBe('Video hidden. Playback and the transcript are unchanged.Show video');
    expect(document.activeElement?.textContent).toBe('Show video');
    // The player and the transcript are untouched.
    expect(q('.player .wave')).not.toBeNull();
    expect(q('.review-scroll')).not.toBeNull();
    await click(button('Show video'));
    expect(q('.vid')).not.toBeNull();
    expect(q('.vid-line')).toBeNull();
    expect(document.activeElement?.textContent).toBe('Audio only');
  });

  it('fills the centre pane in Fullscreen, hiding both side panes and the transcript; Esc only exits', async () => {
    await open(DESIGN_REVIEW);
    await until(() => q('.segm') !== null);
    const panes = q('.review-panes');
    await click(button('Fullscreen'));
    expect(panes?.classList.contains('review-panes--video-full')).toBe(true);
    const exit = button('Exit fullscreen');
    expect(exit.textContent.trim()).toBe('Exit fullscreen');
    expect(exit.querySelector('svg')).not.toBeNull();
    // The panes stay mounted (their state is kept) but are hidden by the stylesheet.
    expect(q('.review-outline')).not.toBeNull();
    exit.focus();
    await press(exit, 'Escape');
    expect(panes?.classList.contains('review-panes--video-full')).toBe(false);
    expect(button('Fullscreen').getAttribute('aria-label')).toBe('Fullscreen');
    // Out of Fullscreen, Esc is left to the screen's other handlers.
    let reached = false;
    const listener = (event: KeyboardEvent): void => {
      reached = event.key === 'Escape' && !event.defaultPrevented;
    };
    document.addEventListener('keydown', listener);
    await press(document.body, 'Escape');
    document.removeEventListener('keydown', listener);
    expect(reached).toBe(true);
  });

  it('adds the video block to the outline, the details rows and the header meta', async () => {
    await open(DESIGN_REVIEW);
    const block = q('.outline-video');
    expect(block?.querySelector('.lbl')?.textContent).toBe('Video');
    const facts = [...(block?.querySelectorAll('.vid-fact') ?? [])].map((f) => `${f.querySelector('dt')?.textContent}=${f.querySelector('dd')?.textContent}`);
    expect(facts).toEqual(['Sources=Screen + camera', 'Resolution=2560 × 1440', 'Frame rate=30 fps', 'Size=1.8 GB']);
    // The block follows the chapters.
    expect(block?.previousElementSibling?.querySelector('.lbl')?.textContent).toBe('Chapters');
    expect(meta()).toBe('Meeting · Yesterday, 4:00 PM · 1:10:02 · 4 people · screen + camera · 1.8 GB video');
    expect(q('.review-meta-video svg')?.getAttribute('aria-label')).toBe('Includes video');
    expect(fact('Tracks')).toMatch(/^5 · Screen, Camera, /);
    expect(fact('Video')).toBe('2560 × 1440 · 30 fps · hardware encoder · 0 dropped frames');
    expect(q('.vid-note')?.textContent).toBe('Video is never transcribed or sent anywhere. Documents are generated from the audio transcript and details only.');
  });

  it('removes the video after the confirmation, and the line says so', async () => {
    await open(DESIGN_REVIEW);
    await click(button('Remove video, keep audio…'));
    const dialog = document.querySelector('[role="dialog"]');
    expect(dialog?.querySelector('h2')?.textContent).toBe('Remove the video of "Design review: library screen"?');
    expect(dialog?.querySelector('.dialog-body')?.textContent).toBe(
      'Deletes the screen and camera video from this PC and frees 1.8 GB. The audio tracks, transcript and documents are kept. This cannot be undone.',
    );
    // Cancel is focused first, so Enter never removes.
    expect(document.activeElement?.textContent).toBe('Cancel');
    await click(button('Remove video'));
    await until(() => q('.vid-line') !== null);
    expect(h.callsOf('video.remove')).toEqual([{ recordingId: DESIGN_REVIEW }]);
    expect(document.querySelector('[role="dialog"]')).toBeNull();
    expect(q('.vid-line')?.textContent).toBe('Video was removed on Oct 6 to free 1.8 GB. Audio tracks, transcript and documents are intact.');
    expect(h.container.textContent).not.toContain('Show video');
    expect(q('.vid')).toBeNull();
    expect(q('.outline-video')).toBeNull();
    expect(meta()).toMatch(/ · video removed$/);
    expect(q('.review-meta-video svg')?.getAttribute('aria-label')).toBe('Video removed');
  });

  it('keeps everything when Cancel is chosen, and toasts a specific message when removal fails', async () => {
    await open(DESIGN_REVIEW);
    await click(button('Remove video, keep audio…'));
    await click(button('Cancel'));
    expect(document.querySelector('[role="dialog"]')).toBeNull();
    expect(h.callsOf('video.remove')).toEqual([]);
    const original = h.bridge.call.bind(h.bridge);
    (h.bridge as { call: unknown }).call = (method: string, params?: unknown) =>
      method === 'video.remove' ? Promise.reject(new Error('The video file is open in another program.')) : (original as (m: string, p?: unknown) => Promise<unknown>)(method, params);
    await click(button('Remove video, keep audio…'));
    await click(button('Remove video'));
    await settle(20);
    expect(document.querySelector('[role="dialog"]')).toBeNull();
    const toast = h.store.toasts.items.value.at(-1);
    expect(toast?.tone).toBe('danger');
    expect(toast?.title).toBe('The video was not removed');
    expect(toast?.body).toBe('The video file is open in another program. The video, audio tracks and transcript are as they were.');
    expect(q('.vid')).not.toBeNull();
  });

  it('shows the removed line without Show video for a recording whose video was removed (Board)', async () => {
    await open(BOARD);
    expect(q('.vid')).toBeNull();
    expect(q('.vid-line')?.textContent).toBe('Video was removed on Oct 1 to free 1.9 GB. Audio tracks, transcript and documents are intact.');
    expect(q('.vid-line button')).toBeNull();
    expect(q('.outline-video')).toBeNull();
    expect(meta()).toMatch(/ · video removed$/);
    expect(fact('Video')).toBe('Removed on Oct 1 · 1.9 GB freed');
  });

  it('shows nothing new for a recording without video (Interview)', async () => {
    await open(INTERVIEW);
    await act(async () => {
      await Promise.resolve();
    });
    expect(q('.vid')).toBeNull();
    expect(q('.vid-line')).toBeNull();
    expect(q('.outline-video')).toBeNull();
    expect(q('.review-meta-video')).toBeNull();
    expect(q('.vid-note')).toBeNull();
    expect(q('.player')?.className).toBe('player');
    expect(fact('Video')).toBeNull();
    expect(meta()).toMatch(/ · audio only$/);
  });
});
