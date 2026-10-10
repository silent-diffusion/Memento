import { describe, expect, it } from 'vitest';
import type { BridgeClient } from '../bridge/client';
import type { EventName, EventPayload, RecordingStatePayload } from '../bridge/types';
import { connectEvents, createStore } from './store';

/** A bridge that only records event handlers, so a test can deliver host events by hand. */
function fakeBridge(): { bridge: BridgeClient; emit: <E extends EventName>(event: E, payload: EventPayload<E>) => void } {
  const handlers = new Map<string, ((payload: unknown) => void)[]>();
  const bridge = {
    on: (event: string, handler: (payload: unknown) => void) => {
      handlers.set(event, [...(handlers.get(event) ?? []), handler]);
      return () => undefined;
    },
  } as unknown as BridgeClient;
  return {
    bridge,
    emit: (event, payload) => {
      for (const handler of handlers.get(event) ?? []) {
        handler(payload);
      }
    },
  };
}

const session = (state: RecordingStatePayload['state']): RecordingStatePayload => ({
  sessionId: 's1',
  recordingId: 'r1',
  state,
  startedAt: '2026-10-10T10:00:00+01:00',
  elapsedMs: 61_000,
  tracks: [],
  lastCheckpointAt: null,
  highlightsCount: 0,
});

describe('the tray’s Record item (app.openScreen, 2.0)', () => {
  it('opens the Recording session ready to record when nothing records', () => {
    const { bridge, emit } = fakeBridge();
    const store = createStore(false);
    connectEvents(bridge, store);
    store.route.value = { name: 'settings', section: 'general' };

    emit('app.openScreen', { screen: 'record' });

    expect(store.route.value).toEqual({ name: 'record', sessionId: null });
  });

  it('rejoins the session in progress, also while it is paused', () => {
    const { bridge, emit } = fakeBridge();
    const store = createStore(false);
    connectEvents(bridge, store);
    store.recording.value = session('paused');

    emit('app.openScreen', { screen: 'record' });

    expect(store.route.value).toEqual({ name: 'record', sessionId: 's1' });
  });

  it('does not rejoin a session that is already being saved', () => {
    const { bridge, emit } = fakeBridge();
    const store = createStore(false);
    connectEvents(bridge, store);
    store.recording.value = session('finalizing');

    emit('app.openScreen', { screen: 'record' });

    expect(store.route.value).toEqual({ name: 'record', sessionId: null });
  });
});
