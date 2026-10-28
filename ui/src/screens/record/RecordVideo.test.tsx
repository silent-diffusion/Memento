import { afterEach, describe, expect, it } from 'vitest';
import type { MockOptions } from '../../bridge/mock';
import { SAMPLE_CAMERAS, SAMPLE_DISPLAYS, SAMPLE_WINDOWS } from '../../bridge/mockVideo';
import type { RecordingVideoProblemPayload, VideoTrackStatus } from '../../bridge/types';
import { withVideo } from '../../format/recordFooter';
import {
  encoderFallbackBanner,
  fallbackDisplay,
  lowSpaceVideoBanner,
  previewNoticeText,
  rowNoteText,
  videoFooterLine,
  videoProblemCard,
  videoProblemToast,
} from '../../format/videoProblems';
import { cameraLabel, DEFAULT_VIDEO_SETTINGS } from '../../format/video';
import { button, click, mountApp, press, settle, type, until, type Harness } from '../../testing/appHarness';
import { encoderFacts } from './VideoCards';
import { FrameHistory } from './TracksCard';
import {
  initialSelection,
  laneLabel,
  rateNote,
  selectedVideoIds,
  selectionFromStatus,
  sublineSources,
  videoBytesPerSecond,
} from './videoSelection';

const SOURCES = { displays: [...SAMPLE_DISPLAYS], windows: [...SAMPLE_WINDOWS], cameras: [...SAMPLE_CAMERAS] };
const DISPLAY1 = SAMPLE_DISPLAYS[0]?.id ?? '';
const DISPLAY2 = SAMPLE_DISPLAYS[1]?.id ?? '';
const ZOOM = 'window:1A2B3C';
const BRIO = 'camera:usb-brio';

function track(over: Partial<VideoTrackStatus>): VideoTrackStatus {
  return {
    trackId: 'screen',
    state: 'recording',
    sourceId: DISPLAY1,
    sourceName: 'Display 1',
    reason: null,
    encoder: 'NVIDIA H.264 Encoder MFT',
    hardware: true,
    width: 2560,
    height: 1440,
    frameRate: 30,
    targetBitsPerSecond: 12_000_000,
    actualBitsPerSecond: 11_000_000,
    framesWritten: 900,
    framesDropped: 0,
    bytesWritten: 1_700_000_000,
    durationMs: 30_000,
    ...over,
    role: over.role ?? (over.trackId === 'camera' ? 'camera' : 'screen'),
  };
}

function problem(over: Partial<RecordingVideoProblemPayload>): RecordingVideoProblemPayload {
  return {
    sessionId: 's',
    trackId: 'screen',
    state: 'waiting',
    code: 'video.displayDisconnected',
    sourceId: DISPLAY2,
    sourceName: 'Display 2',
    detail: null,
    message: 'Display 2 was disconnected.',
    atMs: (23 * 60 + 40) * 1000,
    ...over,
  };
}

describe('the video choice (DESIGN.md §18.1)', () => {
  it('starts from Settings: Audio only leaves video off, Audio + video turns on the default source', () => {
    const off = initialSelection({ captureVideo: false, video: DEFAULT_VIDEO_SETTINGS }, SOURCES);
    expect([off.screen, off.window, off.camera, off.displayId, off.cameraId]).toEqual([false, false, false, DISPLAY1, BRIO]);
    const both = initialSelection({ captureVideo: true, video: DEFAULT_VIDEO_SETTINGS }, SOURCES);
    expect([both.screen, both.camera, both.pip]).toEqual([true, true, { enabled: true, corner: 'bottomRight' }]);
    expect(selectedVideoIds(both)).toEqual([DISPLAY1, BRIO]);
    const window = initialSelection({ captureVideo: true, video: { ...DEFAULT_VIDEO_SETTINGS, defaultSource: 'window' } }, SOURCES);
    expect(selectedVideoIds(window)).toEqual([ZOOM]);
    const camera = initialSelection({ captureVideo: true, video: { ...DEFAULT_VIDEO_SETTINGS, defaultSource: 'camera', defaultCameraId: 'camera:integrated' } }, SOURCES);
    expect(selectedVideoIds(camera)).toEqual(['camera:integrated']);
    expect(selectedVideoIds(null)).toEqual([]);
  });

  it('follows the session while recording: a window track turns the Window row on', () => {
    const before = initialSelection({ captureVideo: true, video: DEFAULT_VIDEO_SETTINGS }, SOURCES);
    const now = selectionFromStatus([track({ sourceId: ZOOM, sourceName: 'Zoom Meeting' }), track({ trackId: 'camera', state: 'waiting', sourceId: BRIO })], { enabled: true, corner: 'topLeft' }, before);
    expect([now.screen, now.window, now.camera, now.windowId, now.displayId, now.pip.corner]).toEqual([false, true, true, ZOOM, DISPLAY1, 'topLeft']);
    const ended = selectionFromStatus([track({ state: 'ended' })], { enabled: false, corner: 'topLeft' }, before);
    expect([ended.screen, ended.camera]).toEqual([false, false]);
  });

  it('words the sub-line, the lane and the footer note', () => {
    const both = initialSelection({ captureVideo: true, video: DEFAULT_VIDEO_SETTINGS }, SOURCES);
    expect(sublineSources(both, 3)).toBe('screen + camera + 3 audio tracks');
    expect(sublineSources({ ...both, screen: false, window: true, camera: false }, 1)).toBe('window + 1 audio track');
    expect([laneLabel(['screen', 'camera']), laneLabel(['camera']), laneLabel([])]).toEqual(['Screen + camera', 'Camera', 'Video (off)']);
    expect(rateNote(both, { video: DEFAULT_VIDEO_SETTINGS }, null)).toBe('(screen 30 fps + camera)');
    expect(rateNote(both, { video: DEFAULT_VIDEO_SETTINGS }, [track({ frameRate: 60 })])).toBe('(screen 60 fps)');
  });

  it('counts the video rate from Settings before recording and from the encoders while recording', () => {
    const both = initialSelection({ captureVideo: true, video: DEFAULT_VIDEO_SETTINGS }, SOURCES);
    expect(videoBytesPerSecond(both, SOURCES, { video: DEFAULT_VIDEO_SETTINGS }, null)).toBe((12_000_000 + 7_800_000) / 8);
    expect(videoBytesPerSecond(both, SOURCES, { video: { ...DEFAULT_VIDEO_SETTINGS, resolution: '720p', quality: 'smaller' } }, null)).toBe((2_100_000 + 3_900_000) / 8);
    expect(videoBytesPerSecond(both, SOURCES, { video: DEFAULT_VIDEO_SETTINGS }, [track({}), track({ trackId: 'camera', state: 'ended' })])).toBe(1_500_000);
  });
});

describe('what the session says when video fails (DESIGN.md §18.6)', () => {
  it('toasts a disconnected display with a switch to another one, or a calm wait', () => {
    const copy = videoProblemToast(problem({}), SOURCES.displays, true);
    expect(copy.title).toBe('Display 2 disconnected at 00:23:40');
    expect(copy.body).toBe('Video is paused. Audio and the camera keep recording. Switch the screen track to Display 1?');
    expect([copy.captureLabel, copy.quiet]).toEqual(['Switch to Display 1', { label: 'Keep waiting', endVideo: false }]);
    expect(fallbackDisplay(SOURCES.displays, DISPLAY1)?.name).toBe('Display 2');
    const alone = videoProblemToast(problem({}), SOURCES.displays.filter((d) => d.id === DISPLAY2), false);
    expect(alone.body).toBe('Video is paused. Audio keeps recording. The screen track continues when the display comes back.');
    expect(alone.capture).toBeNull();
  });

  it('toasts a closed window and a window hidden from capture', () => {
    const closed = videoProblemToast(problem({ code: 'video.windowClosed', state: 'ended', sourceId: ZOOM, sourceName: 'Zoom Meeting', atMs: (58 * 60 + 12) * 1000 }), SOURCES.displays, false);
    expect(closed.title).toBe('"Zoom Meeting" was closed at 00:58:12');
    expect(closed.body).toBe('The video track ends there; everything before it is saved. Audio continues.');
    expect([closed.captureLabel, closed.quiet.label, closed.quiet.endVideo]).toEqual(['Capture Display 1 instead', 'Audio only from here', true]);
    const hidden = videoProblemToast(problem({ code: 'video.protectedContent', state: 'ended', sourceId: ZOOM, sourceName: 'Password vault', atMs: 12_000 }), SOURCES.displays, false);
    expect(hidden.title).toBe('"Password vault" hid itself from capture at 00:00:12');
  });

  it('writes the inline and permission cards, before and during recording', () => {
    expect(videoProblemCard('video.cameraInUse', 'camera', 'Logitech Brio', 'Zoom', true)).toEqual({
      kind: 'cameraInUse',
      role: 'camera',
      lead: 'Logitech Brio is in use by Zoom.',
      rest: 'Recording continues with the screen and all audio tracks. The camera track will start the moment it becomes free.',
    });
    expect(videoProblemCard('video.cameraInUse', 'camera', 'Logitech Brio', null, false)?.lead).toBe('Logitech Brio is in use by another app.');
    expect(videoProblemCard('video.captureBlocked', 'screen', '', null, false)).toMatchObject({
      lead: 'Screen capture is blocked for Memento in Windows privacy settings.',
      rest: 'Allow it under Privacy & security › Screen capture, then come back. Audio sources are not affected.',
    });
    expect(videoProblemCard('video.cameraPrivacy', 'camera', 'Logitech Brio', 'Let desktop apps access your camera', true)?.rest).toContain('("Let desktop apps access your camera")');
    expect(videoProblemCard('video.encoderUnavailable', 'screen', '', null, true)?.lead).toBe('No video encoder could be opened on this PC.');
    expect(videoProblemCard('video.windowClosed', 'screen', '', null, true)).toBeNull();
  });

  it('writes the banners, the row notes, the preview notice and the footer variants', () => {
    expect(encoderFallbackBanner((12 * 60 + 3) * 1000)).toEqual({
      lead: 'Hardware encoder failed at 00:12:03.',
      rest: ' Switched to software encoding; no frames were lost. CPU use is higher until this recording ends.',
    });
    const GIB = 1024 ** 3;
    expect(lowSpaceVideoBanner(4 * GIB, (4 * GIB) / (25 * 60)).lead).toBe('Low disk space · 4 GB free · about 25 minutes of video at this quality.');
    expect(rowNoteText(problem({}))).toEqual({ text: 'Disconnected at 00:23:40 · waiting', tone: 'accent' });
    expect(rowNoteText(problem({ code: 'video.windowClosed', atMs: 1000 }))).toEqual({ text: 'Closed at 00:00:01', tone: 'danger' });
    expect(previewNoticeText({ state: 'waiting', sourceName: 'Display 2' }, problem({}))).toBe('Video paused · Display 2 disconnected');
    expect(videoFooterLine([track({ state: 'waiting', sourceName: 'Display 2' })], [problem({})])).toEqual({ tone: 'accent', text: 'Video paused · Display 2 disconnected · audio recording' });
    expect(videoFooterLine([track({ hardware: false, framesDropped: 3 })], [])).toEqual({ tone: 'accent', text: 'Software encoder · 3 dropped frames' });
    expect(videoFooterLine([track({})], [])).toEqual({ tone: 'ok', text: 'video and audio in sync' });
    expect(videoFooterLine([], [])).toBeNull();
  });

  it('puts the video rate into the footer: in sync, the estimate at video rates, and video minutes when space is low', () => {
    const GIB = 1024 ** 3;
    const status = (free: number, low: boolean) => ({
      engine: { ready: true, device: 'GPU' as const, detail: { ready: true, device: 'GPU' as const, gpuName: null, freeVramBytes: null, model: null, paused: null, gpuMemory: null, note: null } },
      storage: { freeBytes: free, lowSpace: low },
      recording: { active: true, lastCheckpointAt: null, lostSource: null },
      processingPaused: null,
      export: { active: false, percent: null, title: null },
    });
    const left = { tone: 'ok' as const, text: 'Saving continuously · last checkpoint 6 s ago' };
    const right = { text: 'x', low: false };
    const video = { line: { tone: 'ok' as const, text: 'video and audio in sync' }, bytesPerSecond: 2_000_000, note: '(screen 30 fps + camera)' };
    const out = withVideo('recording', left, status(212 * GIB, false), [], video, right);
    expect(out.left.text).toBe('Saving continuously · last checkpoint 6 s ago · video and audio in sync');
    expect(out.right.text).toBe(`212 GB free · about ${Math.floor((212 * GIB) / 2_000_000 / 3600)} hours at this quality (screen 30 fps + camera)`);
    const low = withVideo('recording', left, status(3_000_000_000, true), [], video, right);
    expect(low.right).toEqual({ text: 'Low disk space · about 25 min of video', low: true });
    const paused = withVideo('recording', left, status(212 * GIB, false), [], { ...video, line: { tone: 'accent', text: 'Video paused · Display 2 disconnected · audio recording' } }, right);
    expect(paused.left).toEqual({ tone: 'accent', text: 'Video paused · Display 2 disconnected · audio recording' });
    expect(withVideo('recording', left, null, [], null, right)).toEqual({ left, right });
  });

  it('reads the Encoder card facts and the video lane history', () => {
    expect(encoderFacts([track({}), track({ trackId: 'camera', width: 1920, height: 1080, framesDropped: 2, actualBitsPerSecond: 1_000_000, bytesWritten: 300_000_000 })])).toEqual({
      kind: 'Hardware · GPU',
      hardware: true,
      resolution: '2560 × 1440',
      frameRate: '30 fps · 2 dropped',
      bitrate: '12 Mb/s',
      size: '1.9 GB',
    });
    expect(encoderFacts([track({ hardware: false })])?.kind).toBe('Software · CPU');
    expect(encoderFacts([])).toBeNull();
    const history = new FrameHistory();
    history.add(0, true);
    history.add(10_000, false);
    history.add(20_000, true);
    expect(history.blocks(30_000, 3)).toEqual([true, false, true]);
  });
});

describe('Recording session with video (against the browser-preview host)', () => {
  let h: Harness;

  afterEach(async () => {
    const current = (await h.bridge.call('recording.current')).session;
    if (current !== null && (current.state === 'recording' || current.state === 'paused')) {
      await h.bridge.call('recording.stop', { sessionId: current.sessionId });
    }
    h.unmount();
  });

  const toggle = (name: string): HTMLButtonElement => {
    const el = [...document.querySelectorAll<HTMLButtonElement>('button[role="switch"]')].find((b) => b.getAttribute('aria-label') === name);
    if (el === undefined) {
      throw new Error(`no switch named ${name}`);
    }
    return el;
  };
  const checked = (name: string): boolean => toggle(name).getAttribute('aria-checked') === 'true';
  const text = (selector: string): string => document.querySelector(selector)?.textContent ?? '';

  async function open(mock: MockOptions = {}): Promise<void> {
    h = await mountApp({ name: 'record', sessionId: null }, mock);
    await until(() => document.querySelector('.rec-video-src') !== null);
  }

  async function start(): Promise<string> {
    await click(button('Start recording'));
    await until(() => document.querySelector('.rec-encoder') !== null || h.store.recording.value !== null);
    await settle(20);
    return h.store.recording.value?.sessionId ?? '';
  }

  it('Audio only by default: the VIDEO section is there with everything off and no preview', async () => {
    await open();
    expect([checked('Screen'), checked('Window'), checked('Camera')]).toEqual([false, false, false]);
    expect(document.querySelector('.rec-preview')).toBeNull();
    expect(text('.rec-subline')).toBe('3 audio sources selected · audio only');
    expect(text('.rec-video-list')).toContain('Capture one window instead of a whole display');
    expect(text('.rec-foot-note')).toBe('Each source is saved as its own synchronized track. Video never leaves this PC.');
  });

  it('Audio + video: screen and camera with the inset, the preview, the sub-line and the footer at video rates', async () => {
    await open({ video: ['on'] });
    expect([checked('Screen'), checked('Window'), checked('Camera'), checked('Screen plus camera')]).toEqual([true, false, true, true]);
    expect(text('[data-row="screen"] .src-sub')).toBe('Display 1 · 2560 × 1440 · 30 fps');
    expect(text('[data-row="camera"] .src-sub')).toBe('Logitech Brio · 1080p');
    expect(button('Choose the display to capture: Display 1').textContent).toBe('Display 1');
    expect(text('.rec-subline')).toBe('screen + camera + 3 audio tracks');
    const preview = document.querySelector('.rec-preview');
    expect(preview?.getAttribute('aria-label')).toBe('Preview: Display 1 · 2560 × 1440 · 30 fps, camera inset bottom right');
    expect(text('.rec-preview-tag')).toBe('PREVIEW');
    expect(button('Bottom right').getAttribute('aria-pressed')).toBe('true');
    expect(text('.footer-storage')).toMatch(/at this quality \(screen 30 fps \+ camera\)$/);
    expect(text('.rec-live-foot')).toContain('Video is not analysed; only the audio tracks are transcribed.');
    await until(() => document.querySelector('.rec-preview-main') !== null);
    expect(document.querySelector('.rec-preview-cam--bottomRight')).not.toBeNull();
  });

  it('a camera reached through DirectShow (the OBS Virtual Camera) says so in its row and in the camera list', async () => {
    await open({ video: ['on', 'virtualcam'] });
    expect(checked('Camera')).toBe(true);
    expect(text('[data-row="camera"] .src-sub')).toBe('OBS Virtual Camera · via DirectShow');
    const chip = button('Camera: OBS Virtual Camera');
    expect(chip.textContent).toContain('OBS Virtual Camera');
    await click(chip);
    await until(() => document.querySelector('[role="listbox"] [role="option"]') !== null);
    const options = [...document.querySelectorAll('[role="listbox"] [role="option"]')].map((o) => o.textContent.trim());
    expect(options).toEqual(['OBS Virtual Camera · via DirectShow']);
  });

  it('a Media Foundation camera has no backend note', async () => {
    await open({ video: ['on'] });
    expect(text('[data-row="camera"] .src-sub')).not.toContain('DirectShow');
  });

  it('turns rows on and off before recording: Window replaces Screen, and the inset corner moves', async () => {
    await open({ video: ['on'] });
    await click(toggle('Window'));
    expect([checked('Screen'), checked('Window')]).toEqual([true, false]);
    // No window chosen yet: the picker opens on This window.
    await until(() => document.querySelector('.sheet--wide') !== null);
    expect(button('This window').getAttribute('aria-selected')).toBe('true');
    await click(button('Cancel'));
    await click(button('Top left'));
    expect(button('Top left').getAttribute('aria-pressed')).toBe('true');
    expect(document.querySelector('.rec-preview-cam--topLeft')).not.toBeNull();
    await click(toggle('Camera'));
    expect(text('.rec-subline')).toBe('screen + 3 audio tracks');
    expect(document.querySelector('.rec-pip')?.classList.contains('off')).toBe(true);
  });

  it('the picker: displays and windows as tiles, excluded windows explained but not selectable, search, keyboard, Use', async () => {
    await open({ video: ['on'] });
    await click(button('Choose the display to capture: Display 1'));
    await until(() => document.querySelector('.sheet--wide') !== null);
    const sheet = document.querySelector('.sheet--wide');
    expect(sheet?.getAttribute('role')).toBe('dialog');
    expect(text('.sheet-title')).toBe('Choose what to capture');
    expect(text('.rec-pick-note')).toBe(
      'Captures everything shown on the chosen display, including other windows and notifications that appear on it. Your other displays are not captured. The mouse pointer is included.',
    );
    const tiles = (): HTMLButtonElement[] => [...document.querySelectorAll<HTMLButtonElement>('.rec-tile')];
    expect(tiles().map((t) => t.querySelector('.rec-tile-name')?.textContent)).toEqual(['Display 1', 'Display 2']);
    expect(tiles()[0]?.getAttribute('aria-pressed')).toBe('true');
    expect(button('Use Display 1')).toBeTruthy();
    // Arrow keys move between tiles; Enter (a click) chooses.
    tiles()[0]?.focus();
    await press(tiles()[0] ?? null, 'ArrowRight');
    expect(document.activeElement).toBe(tiles()[1]);
    await click(tiles()[1]);
    expect(button('Use Display 2')).toBeTruthy();
    await click(button('This window'));
    expect(text('.rec-pick-note')).toBe('Captures only this window, even when other windows cover it. If it is minimised, the video pauses and resumes when it comes back; audio keeps recording.');
    const memento = tiles().find((t) => t.textContent.includes('Memento'));
    expect(memento?.getAttribute('aria-disabled')).toBe('true');
    expect(memento?.textContent).toContain('This app is excluded from capture');
    await click(memento);
    expect(memento?.getAttribute('aria-pressed')).toBe('false');
    await type(document.querySelector('.rec-pick-search-input'), 'figma');
    expect(tiles().map((t) => t.querySelector('.rec-tile-name')?.textContent)).toEqual(['Library — Figma']);
    await click(tiles()[0]);
    await click(button('Use Library — Figma'));
    await until(() => document.querySelector('.sheet--wide') === null);
    expect([checked('Screen'), checked('Window')]).toEqual([false, true]);
    expect(text('[data-row="window"] .src-sub')).toBe('Figma · 2000 × 1250');
    await type(document.querySelector('.rec-title-input'), 'Window test');
    await click(button('Start recording'));
    await until(() => h.callsOf('recording.start').length > 0);
    expect((h.callsOf('recording.start')[0] as { sourceIds: string[] }).sourceIds).toEqual(['mic:usb-mv7', 'system:default-output', 'app:4120', 'window:2B3C4D', BRIO]);
  });

  it('records video: the inset, the Encoder card, the video lane, and changes while recording', async () => {
    await open({ video: ['on'] });
    const sessionId = await start();
    await until(() => document.querySelector('.rec-encoder') !== null);
    expect((h.callsOf('recording.start')[0] as { sourceIds: string[] }).sourceIds.slice(-2)).toEqual([DISPLAY1, BRIO]);
    expect(h.callsOf('recording.setPip')).toEqual([{ sessionId, enabled: true, corner: 'bottomRight' }]);
    expect(text('.rec-sources .pill.active')).toBe('Recording video');
    expect(text('.rec-encoder')).toContain('Hardware · GPU');
    expect(text('.rec-encoder')).toContain('2560 × 1440');
    expect(text('.rec-encoder')).toContain('Change resolution and quality in Settings › Recording. Changes apply to the next recording.');
    expect(text('.rec-subline')).toMatch(/^Started .* · screen \+ camera \+ 3 audio tracks$/);
    expect(text('.rec-lane-name--video')).toBe('Screen + camera');
    expect(text('.rec-preview-tag')).toBe('LIVE');
    expect(text('.app-footer')).toContain('video and audio in sync');

    // Switch to a window through the picker: the track continues.
    await click(button('Choose the window to capture: none chosen'));
    await until(() => document.querySelector('.rec-tile') !== null);
    await click([...document.querySelectorAll('.rec-tile')].find((t) => t.textContent.includes('Zoom Meeting')));
    await click(button('Use Zoom Meeting'));
    await until(() => checked('Window'));
    expect(h.callsOf('recording.setVideoSource')).toContainEqual({ sessionId, role: 'screen', sourceId: ZOOM });
    expect(checked('Screen')).toBe(false);
    // The inset moves; the camera is turned off.
    await click(button('Top right'));
    expect(h.callsOf('recording.setPip').at(-1)).toEqual({ sessionId, enabled: true, corner: 'topRight' });
    await click(toggle('Camera'));
    await until(() => !checked('Camera'));
    expect(h.callsOf('recording.setVideoSource')).toContainEqual({ sessionId, role: 'camera', sourceId: null });
    expect(text('.rec-lane-name--video')).toBe('Screen');
  });

  it('shows the permission cards before recording and Record audio only turns video off', async () => {
    await open({ video: ['blocked', 'privacy'] });
    await until(() => document.querySelectorAll('.rec-video-card').length === 2);
    expect(text('.rec-video-list')).toContain('Screen capture is blocked for Memento in Windows privacy settings.');
    expect(text('.rec-video-list')).toContain('Logitech Brio is turned off in Windows privacy settings.');
    expect(text('.rec-preview-notice')).toBe('Blocked in Windows privacy settings');
    const openSettings = [...document.querySelectorAll<HTMLButtonElement>('.rec-video-card button')].filter((b) => b.textContent === 'Open Windows settings');
    await click(openSettings[0]);
    expect(h.callsOf('video.openPrivacySettings')).toEqual([{ which: 'screenCapture' }]);
    await click(button('Continue without camera'));
    expect(checked('Camera')).toBe(false);
    await click(button('Record audio only'));
    expect([checked('Screen'), checked('Camera')]).toEqual([false, false]);
    expect(document.querySelector('.rec-video-card')).toBeNull();
  });

  it('a camera in use waits in place: the card, the row note, Retry and the footer', async () => {
    await open({ video: ['inuse'] });
    await until(() => document.querySelector('.rec-video-card') !== null);
    expect(text('.rec-video-card')).toContain('Logitech Brio is in use by Zoom.');
    expect(text('.rec-video-card')).toContain('You can start recording now');
    const sessionId = await start();
    await until(() => text('[data-row="camera"] .src-sub') === 'In use by Zoom · waiting');
    expect(text('.rec-video-card')).toContain('Recording continues with the screen and all audio tracks.');
    expect(text('.app-footer')).toContain('Camera waiting');
    await click(button('Retry camera'));
    expect(h.callsOf('recording.setVideoSource')).toContainEqual({ sessionId, role: 'camera', sourceId: BRIO });
  });

  it('a display that goes away: one toast with Switch to Display 2, the row note and the footer', async () => {
    let t = Date.parse('2026-10-10T16:00:00+02:00');
    await open({ video: ['on', 'disconnect'], now: () => t });
    const sessionId = await start();
    t += 12_000;
    await click(button('Pause'));
    await until(() => document.querySelector('[aria-label="Resume"]') !== null);
    await click(button('Resume'));
    await until(() => document.querySelector('.toast') !== null);
    expect(text('.toast')).toContain('Display 1 disconnected at 00:00:12');
    expect(text('.toast')).toContain('Video is paused. Audio and the camera keep recording. Switch the screen track to Display 2?');
    expect(text('[data-row="screen"] .src-sub')).toBe('Disconnected at 00:00:12 · waiting');
    expect(text('.rec-preview-notice')).toBe('Video paused · Display 1 disconnected');
    expect(text('.app-footer')).toContain('Video paused · Display 1 disconnected · audio recording');
    expect(document.querySelectorAll('.toast')).toHaveLength(1);
    await click(button('Switch to Display 2'));
    await until(() => h.callsOf('recording.setVideoSource').length > 0);
    expect(h.callsOf('recording.setVideoSource')).toContainEqual({ sessionId, role: 'screen', sourceId: DISPLAY2 });
    await until(() => text('[data-row="screen"] .src-sub') === 'Display 2 · 1920 × 1080 · 30 fps');
  });

  it('the encoder falls back to software: a banner with Lower quality, the Encoder card and the footer say so', async () => {
    let t = Date.parse('2026-10-10T16:00:00+02:00');
    await open({ video: ['on', 'fallback'], now: () => t });
    const sessionId = await start();
    t += 9_000;
    await click(button('Pause'));
    await until(() => document.querySelector('[aria-label="Resume"]') !== null);
    await click(button('Resume'));
    await until(() => text('.banner').includes('Hardware encoder failed at 00:00:09.'));
    expect(text('.banner')).toContain('Switched to software encoding; no frames were lost.');
    expect(text('.rec-encoder')).toContain('Software · CPU');
    expect(text('.app-footer')).toMatch(/Software encoder · \d+ dropped frames?/);
    await click(button('Lower quality'));
    expect(h.callsOf('recording.setVideoQuality')).toEqual([{ sessionId, quality: 'smaller' }]);
    await until(() => !document.body.textContent.includes('Lower quality'));
  });

  it('low disk with video: the banner speaks in minutes of video and Stop video now ends both tracks', async () => {
    await open({ video: ['on'], lowSpace: true });
    const sessionId = await start();
    await until(() => text('.banner').includes('of video at this quality.'));
    expect(text('.banner')).toMatch(/^Low disk space · 4 GB free · (about \d+ minutes|about \d+ hours|under a minute) of video at this quality\. Recording continues\. If space runs out, video stops first and audio last\./);
    expect(text('.footer-storage')).toMatch(/^Low disk space · (about \d+|under a) min of video$/);
    await click(button('Stop video now'));
    await until(() => h.callsOf('recording.setVideoSource').length === 2);
    expect(h.callsOf('recording.setVideoSource')).toEqual([
      { sessionId, role: 'screen', sourceId: null },
      { sessionId, role: 'camera', sourceId: null },
    ]);
  });
});

describe('camera labels (BRIDGE.md, VideoSource.backend)', () => {
  it('names the camera, its height when known, and "via DirectShow" for a DirectShow camera', () => {
    expect(cameraLabel({ name: 'Logitech Brio', height: 1080, backend: 'mf' })).toBe('Logitech Brio · 1080p');
    expect(cameraLabel({ name: 'Integrated camera', height: 0, backend: 'mf' })).toBe('Integrated camera');
    expect(cameraLabel({ name: 'OBS Virtual Camera', height: 0, backend: 'dshow' })).toBe('OBS Virtual Camera · via DirectShow');
    expect(cameraLabel({ name: 'Capture card', height: 720, backend: 'dshow' })).toBe('Capture card · 720p · via DirectShow');
    expect(cameraLabel({ name: 'Older host', height: 0 })).toBe('Older host');
  });
});
