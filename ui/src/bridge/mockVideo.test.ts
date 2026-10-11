import { describe, expect, it } from 'vitest';
import { createBridgeClient, type BridgeClient } from './client';
import type { MockOptions } from './mock';
import { DEFAULT_EXPORT_SELECTION } from './mockLibraryExtra';
import { muxWebm } from './mockVideoFile';
import { roleOf, videoFlagsFromQuery, type VideoFlag } from './mockVideo';
import type { BridgeError, EventName, EventPayload, RecordingVideoProblemPayload } from './types';

const quiet = { info: () => undefined, warn: () => undefined };
const DESIGN = '20261005-160000-dsrev';
const CS301 = '20261003-110000-cs301';
const BOARD = '20260929-090000-board';
const Q3 = '20261006-100000-q3plan';
const DISPLAY1 = 'display:\\\\.\\DISPLAY1';
const DISPLAY2 = 'display:\\\\.\\DISPLAY2';
const ZOOM = 'window:1A2B3C';
const BRIO = 'camera:usb-brio';

/** A preview host whose clock the test moves: `advance(ms)` then `resume` runs one status tick. */
function host(flags: VideoFlag[] = [], extra: MockOptions = {}): { bridge: BridgeClient; advance: (ms: number) => void } {
  let t = Date.parse('2026-10-10T16:00:00+02:00');
  const bridge = createBridgeClient({ logger: quiet, mock: { live: false, recovery: false, stepMs: 5, video: flags, now: () => t, ...extra } });
  return {
    bridge,
    advance: (ms) => {
      t += ms;
    },
  };
}

async function failure(promise: Promise<unknown>): Promise<BridgeError> {
  return promise.then(
    () => {
      throw new Error('expected the call to fail');
    },
    (e: unknown) => e as BridgeError,
  );
}

function collect<E extends EventName>(bridge: BridgeClient, event: E): EventPayload<E>[] {
  const seen: EventPayload<E>[] = [];
  bridge.on(event, (payload) => {
    seen.push(payload);
  });
  return seen;
}

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 5));

async function startWith(bridge: BridgeClient, video: string[]): Promise<string> {
  const { sessionId } = await bridge.call('recording.start', { title: 'Video test', type: 'meeting', sourceIds: ['mic:usb-mv7', ...video] });
  return sessionId;
}

/** One tick of the status cadence (the preview host ticks on pause and resume). */
async function tick(bridge: BridgeClient, sessionId: string): Promise<void> {
  await bridge.call('recording.resume', { sessionId });
  await settle();
}

describe('preview video flags and ids', () => {
  it('reads ?video= flags and sorts source ids into roles', () => {
    expect(videoFlagsFromQuery(new URLSearchParams('video=on,Disconnect,nonsense'))).toEqual(['on', 'disconnect']);
    expect(videoFlagsFromQuery(new URLSearchParams(''))).toEqual([]);
    expect([roleOf(DISPLAY1), roleOf(ZOOM), roleOf(BRIO), roleOf('mic:usb-mv7')]).toEqual(['screen', 'screen', 'camera', null]);
  });
});

describe('video sources and thumbnails', () => {
  it('lists displays, windows (Memento and protected ones excluded) and cameras, without opening anything', async () => {
    const { bridge } = host();
    expect((await bridge.call('sources.list')).videoAvailable).toBe(true);
    const sources = await bridge.call('video.listSources', {});
    expect(sources.displays.map((d) => d.name)).toEqual(['Display 1', 'Display 2']);
    expect(sources.displays[0]?.detail).toBe('2560 × 1440 · primary · this is where Memento is');
    expect(sources.windows.filter((w) => w.excluded).map((w) => w.detail)).toEqual(['This app is excluded from capture', 'This window keeps itself out of captures']);
    expect(sources.cameras.map((c) => c.name)).toEqual(['Logitech Brio', 'Integrated camera']);
    const thumb = await bridge.call('video.thumbnail', { sourceId: DISPLAY1, width: 144 });
    expect(thumb.dataUrl.startsWith('data:image/svg+xml,')).toBe(true);
    expect((await failure(bridge.call('video.thumbnail', { sourceId: 'display:gone', width: 144 }))).code).toBe('video.sourceNotFound');
  });

  it('answers the privacy, in-use and blocked cases from thumbnails, and nothing when video is off', async () => {
    const blocked = host(['blocked', 'privacy']).bridge;
    expect((await failure(blocked.call('video.thumbnail', { sourceId: DISPLAY1, width: 144 }))).code).toBe('video.captureBlocked');
    const privacy = await failure(blocked.call('video.thumbnail', { sourceId: BRIO, width: 144 }));
    expect([privacy.code, privacy.detail]).toEqual(['video.cameraPrivacy', 'Let desktop apps access your camera']);
    const inUse = await failure(host(['inuse']).bridge.call('video.thumbnail', { sourceId: BRIO, width: 144 }));
    expect([inUse.code, inUse.detail]).toEqual(['video.cameraInUse', 'Zoom']);
    const off = host(['off']).bridge;
    expect((await off.call('sources.list')).videoAvailable).toBe(false);
    expect(await off.call('video.listSources', {})).toEqual({ displays: [], windows: [], cameras: [] });
    expect((await host(['nocamera']).bridge.call('video.listSources', {})).cameras).toEqual([]);
  });

  it('starts with Audio + video when a flag needs it; the product default is Audio only', async () => {
    expect((await host().bridge.call('settings.get')).recording.captureVideo).toBe(false);
    const on = (await host(['on']).bridge.call('settings.get')).recording;
    expect([on.captureVideo, on.video.defaultSource, on.video.frameRate, on.video.quality]).toEqual([true, 'screenAndCamera', 30, 'balanced']);
    expect((await host(['closed']).bridge.call('settings.get')).recording.video.defaultSource).toBe('window');
  });
});

describe('the session video tracks', () => {
  it('starts screen and camera beside audio, reports status, switches, moves the inset and turns a track off', async () => {
    const { bridge, advance } = host();
    const statuses = collect(bridge, 'recording.videoStatus');
    const sessionId = await startWith(bridge, [DISPLAY1, BRIO]);
    await settle();
    const first = statuses.at(-1) ?? { videoTracks: [], pip: null };
    expect(first.videoTracks.map((t) => [t.trackId, t.sourceName, t.state, t.width, t.height, t.frameRate])).toEqual([
      ['screen', 'Display 1', 'recording', 2560, 1440, 30],
      ['camera', 'Logitech Brio', 'recording', 1920, 1080, 30],
    ]);
    expect(first.videoTracks[0]?.targetBitsPerSecond).toBe(12_000_000);
    expect(first.pip).toEqual({ enabled: true, corner: 'bottomRight' });
    // Audio started with only the audio ids.
    const state = await bridge.call('recording.current');
    expect(state.session?.tracks.map((t) => t.sourceId)).toEqual(['mic:usb-mv7']);

    advance(3000);
    await tick(bridge, sessionId);
    const later = statuses.at(-1) ?? { videoTracks: [] };
    expect(later.videoTracks[0]?.framesWritten).toBe(90);
    expect(later.videoTracks[0]?.bytesWritten).toBeGreaterThan(0);

    const switched = await bridge.call('recording.setVideoSource', { sessionId, role: 'screen', sourceId: ZOOM });
    expect(switched.videoTracks[0]).toMatchObject({ trackId: 'screen', sourceName: 'Zoom Meeting', state: 'recording', framesWritten: 90 });
    const refused = await failure(bridge.call('recording.setVideoSource', { sessionId, role: 'screen', sourceId: 'window:4D5E6F' }));
    expect(refused.code).toBe('video.protectedContent');
    await bridge.call('recording.setPip', { sessionId, enabled: true, corner: 'topLeft' });
    await settle();
    expect(statuses.at(-1)?.pip).toEqual({ enabled: true, corner: 'topLeft' });
    const lower = await bridge.call('recording.setVideoQuality', { sessionId, quality: 'smaller' });
    expect(lower.videoTracks[0]?.targetBitsPerSecond).toBe(Math.round(targetFor(900, 'smaller')));
    const off = await bridge.call('recording.setVideoSource', { sessionId, role: 'camera', sourceId: null });
    expect(off.videoTracks.map((t) => t.trackId)).toEqual(['screen']);
    expect((await failure(bridge.call('recording.setPip', { sessionId: 'nope', enabled: false, corner: 'topLeft' }))).code).toBe('recording.noSession');
  });

  it('pauses the video tracks with the recording', async () => {
    const { bridge } = host();
    const statuses = collect(bridge, 'recording.videoStatus');
    const sessionId = await startWith(bridge, [DISPLAY1]);
    await bridge.call('recording.pause', { sessionId });
    await settle();
    expect(statuses.at(-1)?.videoTracks[0]?.state).toBe('paused');
    await bridge.call('recording.resume', { sessionId });
    await settle();
    expect(statuses.at(-1)?.videoTracks[0]?.state).toBe('recording');
  });

  it('never keeps audio from starting: blocked screen, camera privacy and no encoder are problems, not refusals', async () => {
    const { bridge } = host(['blocked', 'privacy']);
    const problems = collect(bridge, 'recording.videoProblem');
    await startWith(bridge, [DISPLAY1, BRIO]);
    await settle();
    expect(problems.map((p) => [p.trackId, p.code, p.state])).toEqual([
      ['screen', 'video.captureBlocked', 'stopped'],
      ['camera', 'video.cameraPrivacy', 'stopped'],
    ]);
    expect(problems[1]?.detail).toBe('Let desktop apps access your camera');
    const none = host(['noencoder']);
    const failed = collect(none.bridge, 'recording.videoProblem');
    await startWith(none.bridge, [DISPLAY1]);
    await settle();
    expect(failed.map((p) => p.code)).toEqual(['video.encoderUnavailable']);
  });

  it('waits for a camera in use and starts it the moment it is free', async () => {
    const { bridge, advance } = host(['inuse']);
    const problems = collect(bridge, 'recording.videoProblem');
    const statuses = collect(bridge, 'recording.videoStatus');
    const sessionId = await startWith(bridge, [DISPLAY1, BRIO]);
    await settle();
    expect(problems.map((p) => [p.code, p.state, p.detail])).toEqual([['video.cameraInUse', 'waiting', 'Zoom']]);
    advance(21_000);
    await tick(bridge, sessionId);
    const camera = statuses.at(-1)?.videoTracks.find((t) => t.trackId === 'camera');
    expect(camera?.state).toBe('recording');
  });

  it.each<[VideoFlag, string, RecordingVideoProblemPayload['code'], RecordingVideoProblemPayload['state']]>([
    ['disconnect', DISPLAY1, 'video.displayDisconnected', 'waiting'],
    ['closed', ZOOM, 'video.windowClosed', 'ended'],
    ['protected', ZOOM, 'video.protectedContent', 'ended'],
    ['minimised', ZOOM, 'video.windowMinimised', 'waiting'],
    ['fallback', DISPLAY1, 'video.encoderFallback', 'recording'],
    ['diskfull', DISPLAY1, 'video.diskFull', 'stopped'],
  ])('%s: reports %s once, with the source and the time', async (flag, source, code, state) => {
    const { bridge, advance } = host([flag]);
    const problems = collect(bridge, 'recording.videoProblem');
    const sessionId = await startWith(bridge, [source]);
    advance(16_000);
    await tick(bridge, sessionId);
    advance(1_000);
    await tick(bridge, sessionId);
    expect(problems.map((p) => [p.code, p.state])).toEqual([[code, state]]);
    expect(problems[0]?.atMs).toBe(16_000);
    expect(problems[0]?.sourceName).toBe(source === ZOOM ? 'Zoom Meeting' : 'Display 1');
    expect(problems[0]?.message).toMatch(/\./);
  });

  it('a display that went away waits for another one; the track continues', async () => {
    const { bridge, advance } = host(['disconnect']);
    const sessionId = await startWith(bridge, [DISPLAY1]);
    advance(11_000);
    await tick(bridge, sessionId);
    const switched = await bridge.call('recording.setVideoSource', { sessionId, role: 'screen', sourceId: DISPLAY2 });
    expect(switched.videoTracks[0]).toMatchObject({ sourceName: 'Display 2', state: 'recording', height: 1080 });
  });

  it('finalizes the session video into the project, with a URL per part and the inset as it was', async () => {
    const { bridge, advance } = host();
    const sessionId = await startWith(bridge, [DISPLAY1, BRIO]);
    advance(5_000);
    await tick(bridge, sessionId);
    const { recordingId } = await bridge.call('recording.stop', { sessionId });
    await new Promise((resolve) => setTimeout(resolve, 1300));
    const project = await bridge.call('project.get', { recordingId });
    expect(project.summary.hasVideo).toBe(true);
    expect(project.summary.video).toEqual({ roles: ['screen', 'camera'], height: 1440, removedAt: null });
    expect(project.video?.pip).toEqual({ enabled: true, corner: 'bottomRight' });
    expect(project.video?.tracks.map((t) => [t.id, t.startOffsetMs, t.parts.length])).toEqual([
      ['screen', 0, 1],
      ['camera', 0, 1],
    ]);
    expect(project.video?.tracks[0]?.parts[0]?.url).toMatch(/^blob:/);
  });
});

function targetFor(height: number, quality: 'smaller' | 'balanced'): number {
  return (height <= 1080 ? 7_800_000 : 12_000_000) * (quality === 'smaller' ? 0.5 : 1);
}

describe('the samples, the video stage, removing video and exporting it', () => {
  it('gives the samples their video: screen + camera, screen only, and a removed one', async () => {
    const { bridge } = host();
    const design = await bridge.call('project.get', { recordingId: DESIGN });
    expect(design.video?.tracks.map((t) => [t.id, t.sourceName, t.width, t.height])).toEqual([
      ['screen', 'Display 1', 2560, 1440],
      ['camera', 'Logitech Brio', 1920, 1080],
    ]);
    expect(design.summary.video).toEqual({ roles: ['screen', 'camera'], height: 1440, removedAt: null });
    expect((await bridge.call('project.get', { recordingId: CS301 })).video?.tracks.map((t) => t.id)).toEqual(['screen']);
    const board = await bridge.call('project.get', { recordingId: BOARD });
    expect(board.summary.hasVideo).toBe(false);
    expect(board.summary.video?.removedAt).not.toBeNull();
    expect(board.video?.removed?.bytesFreed).toBeGreaterThan(0);
    expect(board.history.at(-1)?.summary).toMatch(/^Removed the video · /);
    const usage = await bridge.call('library.usage');
    expect(usage.videoRecordings).toBe(3);
    expect(usage.videoBytes).toBeGreaterThan(0);
  });

  it('removes a recording\'s video, keeps its audio, and says what removal would free', async () => {
    const { bridge } = host();
    const estimate = await bridge.call('video.removeEstimate', { recordingIds: [DESIGN], olderThanDays: null });
    const design = await bridge.call('project.get', { recordingId: DESIGN });
    expect(estimate).toEqual({ recordings: 1, bytes: design.video?.sizeBytes });
    const all = await bridge.call('video.removeEstimate', { recordingIds: null, olderThanDays: 14 });
    expect(all.recordings).toBe(1);
    const { freedBytes } = await bridge.call('video.remove', { recordingId: DESIGN });
    expect(freedBytes).toBe(design.video?.sizeBytes);
    const after = await bridge.call('project.get', { recordingId: DESIGN });
    expect(after.video?.tracks).toEqual([]);
    expect(after.video?.removed?.bytesFreed).toBe(freedBytes);
    expect(after.tracks.length).toBe(design.tracks.length);
    expect(after.summary.sizeBytes).toBe(design.summary.sizeBytes - freedBytes);
    expect((await failure(bridge.call('video.remove', { recordingId: DESIGN }))).code).toBe('bridge.invalidParams');
  });

  it('runs and fails the video stage on demand, and a remedy settles it', async () => {
    const encoding = host(['encoding']).bridge;
    const current = (await encoding.call('library.processing', {})).current;
    expect(current?.stages.map((s) => s.stage)).toEqual(['stored', 'video', 'transcript', 'speakers']);
    expect(current?.stages[1]).toMatchObject({ state: 'active', percent: 42, label: 'Encoding 42% · GPU' });
    expect(current?.failure).toBeNull();
    const failing = host(['stagefail']).bridge;
    const failed = (await failing.call('library.processing', {})).current;
    expect(failed?.recordingId).toBe(Q3);
    expect(failed?.failure?.remedies.map((r) => r.label)).toEqual(['Retry with software encoder', 'Keep raw frames']);
    await failing.call('processing.retry', { recordingId: Q3, stage: 'video', remedyId: 'keepRaw' });
    const kept = await failing.call('library.processing', {});
    expect(kept.current?.failure ?? null).toBeNull();
    expect(kept.current?.stages.find((s) => s.stage === 'video')?.state).toBe('done');
  });

  it('lists recovered video with the recovered recording', async () => {
    const { bridge } = host(['recovered'], { recovery: true });
    const { items } = await bridge.call('recovery.list', {});
    expect(items[0]?.videoTracks).toEqual([{ trackId: 'camera', sourceName: 'Integrated camera', recoveredDurationMs: expect.any(Number) as number }]);
    expect((await host([], { recovery: true }).bridge.call('recovery.list', {})).items[0]?.videoTracks).toEqual([]);
  });

  it('estimates the Video row: one file, two files, the original container, and why nothing can be written', async () => {
    const { bridge } = host();
    const one = await bridge.call('export.estimate', { recordingId: DESIGN, selection: { ...DEFAULT_EXPORT_SELECTION, video: { on: true, format: 'mp4', layout: 'oneFile' } } });
    const oneFile = one.items.filter((i) => i.component === 'video');
    expect(oneFile.map((i) => i.name)).toEqual([expect.stringMatching(/^Design review - library screen.*[^a]\.mp4$/) as string]);
    expect(oneFile[0]?.name).not.toMatch(/ - (screen|camera)\.mp4$/);
    const two = await bridge.call('export.estimate', { recordingId: DESIGN, selection: { ...DEFAULT_EXPORT_SELECTION, video: { on: true, format: 'original', layout: 'twoFiles' } } });
    const names = two.items.filter((i) => i.component === 'video').map((i) => i.name);
    expect(names).toHaveLength(2);
    expect(names[0]).toMatch(/ - screen\.mp4$/);
    expect(names[1]).toMatch(/ - camera\.mp4$/);
    const refused = await failure(bridge.call('export.estimate', { recordingId: DESIGN, selection: { ...DEFAULT_EXPORT_SELECTION, video: { on: true, format: 'original', layout: 'oneFile' } } }));
    expect(refused.code).toBe('bridge.invalidParams');
    const removed = await bridge.call('export.estimate', { recordingId: BOARD, selection: DEFAULT_EXPORT_SELECTION });
    expect(removed.unavailable).toContainEqual({ component: 'video', reason: 'Video was removed' });
    const none = await bridge.call('export.estimate', { recordingId: Q3, selection: DEFAULT_EXPORT_SELECTION });
    expect(none.unavailable).toContainEqual({ component: 'video', reason: 'No video' });
  });

  it('validates Remove video older than and clears it with null', async () => {
    const { bridge } = host();
    const set = await bridge.call('settings.set', { storage: { removeVideoOlderThanDays: 90 } });
    expect(set.storage).toEqual({ reclaimOlderThanDays: null, removeVideoOlderThanDays: 90, deleteAudioOlderThanDays: null });
    expect((await failure(bridge.call('settings.set', { storage: { removeVideoOlderThanDays: 45 } }))).code).toBe('settings.invalidValue');
    expect((await bridge.call('settings.set', { storage: { reclaimOlderThanDays: 30 } })).storage).toEqual({ reclaimOlderThanDays: 30, removeVideoOlderThanDays: 90, deleteAudioOlderThanDays: null });
    expect((await bridge.call('settings.set', { storage: { removeVideoOlderThanDays: null } })).storage.removeVideoOlderThanDays).toBeNull();
  });
});

describe('the preview video file', () => {
  it('writes a WebM with an EBML header, the VP8 track, clusters at key frames and their cues', () => {
    const chunks = [0, 1000, 2000, 3000].map((atMs, i) => ({ data: Uint8Array.from([i, i, i]), atMs, key: i % 2 === 0 }));
    const file = muxWebm(chunks, 320, 180, 4000);
    expect([...file.slice(0, 4)]).toEqual([0x1a, 0x45, 0xdf, 0xa3]);
    const text = new TextDecoder('latin1').decode(file);
    expect(text).toContain('webm');
    expect(text).toContain('V_VP8');
    // Two key frames: two clusters and two cue points.
    const count = (bytes: number[]): number => {
      let n = 0;
      for (let i = 0; i + bytes.length <= file.length; i++) {
        if (bytes.every((b, j) => file[i + j] === b)) {
          n++;
        }
      }
      return n;
    };
    expect(count([0x1f, 0x43, 0xb6, 0x75])).toBe(2);
    // The Cues element and the SeekHead entry that points at it.
    expect(count([0x1c, 0x53, 0xbb, 0x6b])).toBe(2);
  });
});
