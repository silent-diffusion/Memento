import { describe, expect, it } from 'vitest';
import { createBridgeClient, type BridgeClient } from './client';
import type { BridgeError } from './types';

// 2.8.0: video.addSource and video.removeSource in the preview host (BRIDGE.md "Add and remove screens while recording").

const quiet = { info: () => undefined, warn: () => undefined };
const DISPLAY1 = 'display:\\\\.\\DISPLAY1';
const DISPLAY2 = 'display:\\\\.\\DISPLAY2';
const ZOOM = 'window:1A2B3C';
const FIGMA = 'window:2B3C4D';
const EDGE = 'window:3C4D5E';
const BRIO = 'camera:usb-brio';

function host(): { bridge: BridgeClient; advance: (ms: number) => void } {
  let t = Date.parse('2026-10-10T16:00:00+02:00');
  const bridge = createBridgeClient({ logger: quiet, mock: { live: false, recovery: false, stepMs: 5, video: [], now: () => t } });
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

const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 5));

async function startWith(bridge: BridgeClient, video: string[]): Promise<string> {
  const { sessionId } = await bridge.call('recording.start', { title: 'Screens test', type: 'meeting', sourceIds: ['mic:usb-mv7', ...video] });
  return sessionId;
}

async function tick(bridge: BridgeClient, sessionId: string): Promise<void> {
  await bridge.call('recording.resume', { sessionId });
  await settle();
}

describe('screens added and removed while recording (2.8.0)', () => {
  it('adds a screen as a track of its own, removes it, and keeps each range in the project', async () => {
    const { bridge, advance } = host();
    const sessionId = await startWith(bridge, [DISPLAY1, BRIO]);
    advance(4_000);
    await tick(bridge, sessionId);

    const added = await bridge.call('video.addSource', { sessionId, sourceId: ZOOM });
    expect(added.videoTracks.map((t) => [t.trackId, t.role, t.sourceName])).toEqual([
      ['screen', 'screen', 'Display 1'],
      ['screen-2', 'screen', 'Zoom Meeting'],
      ['camera', 'camera', 'Logitech Brio'],
    ]);

    advance(3_000);
    await tick(bridge, sessionId);
    const removed = await bridge.call('video.removeSource', { sessionId, trackId: 'screen-2' });
    expect(removed.videoTracks.map((t) => t.trackId)).toEqual(['screen', 'camera']);

    advance(2_000);
    await tick(bridge, sessionId);
    const { recordingId } = await bridge.call('recording.stop', { sessionId });
    await new Promise((resolve) => setTimeout(resolve, 1300));
    const project = await bridge.call('project.get', { recordingId });
    expect(project.summary.video?.roles).toEqual(['screen', 'camera']);
    expect(project.video?.tracks.map((t) => [t.id, t.trackId, t.startOffsetMs, t.stoppedAtMs])).toEqual([
      ['screen', 'screen', 0, 9_000],
      ['screen', 'screen-2', 4_000, 7_000],
      ['camera', 'camera', 0, 9_000],
    ]);
  });

  it('refuses a camera, a source already recorded, a fifth screen and an unknown track, changing nothing', async () => {
    const { bridge } = host();
    const sessionId = await startWith(bridge, [DISPLAY1]);
    expect((await failure(bridge.call('video.addSource', { sessionId, sourceId: BRIO }))).code).toBe('bridge.invalidParams');
    const twice = await failure(bridge.call('video.addSource', { sessionId, sourceId: DISPLAY1 }));
    expect(twice.code).toBe('video.sourceAlreadyRecorded');
    expect(twice.message).toBe('Display 1 is already being recorded. Nothing changed; choose another display or window.');
    expect((await failure(bridge.call('video.addSource', { sessionId, sourceId: 'window:4D5E6F' }))).code).toBe('video.protectedContent');
    await bridge.call('video.addSource', { sessionId, sourceId: DISPLAY2 });
    await bridge.call('video.addSource', { sessionId, sourceId: ZOOM });
    await bridge.call('video.addSource', { sessionId, sourceId: FIGMA });
    const tooMany = await failure(bridge.call('video.addSource', { sessionId, sourceId: EDGE }));
    expect(tooMany.code).toBe('video.tooManySources');
    expect(tooMany.message).toContain('remove one to add DESIGN.md — Microsoft Edge');
    expect((await failure(bridge.call('video.removeSource', { sessionId, trackId: 'screen-9' }))).code).toBe('video.sourceNotFound');
    expect((await failure(bridge.call('video.addSource', { sessionId: 'nope', sourceId: EDGE }))).code).toBe('recording.noSession');
  });

  it('lets the last screen go while audio records, and a screen added then is the screen track again', async () => {
    const { bridge } = host();
    const sessionId = await startWith(bridge, [DISPLAY1]);
    expect((await bridge.call('video.removeSource', { sessionId, trackId: 'screen' })).videoTracks).toEqual([]);
    expect((await bridge.call('recording.current')).session?.state).toBe('recording');
    const again = await bridge.call('video.addSource', { sessionId, sourceId: DISPLAY2 });
    expect(again.videoTracks.map((t) => [t.trackId, t.sourceName])).toEqual([['screen', 'Display 2']]);
  });
});
