import { signal } from '@preact/signals';
import { describe, expect, it } from 'vitest';
import type { RecordingStatePayload } from '../bridge/types';
import { adoptCurrentSession } from './currentSession';

const payload = (state: RecordingStatePayload['state']): RecordingStatePayload => ({
  sessionId: 's1',
  recordingId: 'r1',
  state,
  startedAt: '2026-10-07T13:27:08Z',
  elapsedMs: 56_936,
  tracks: [],
  lastCheckpointAt: null,
  highlightsCount: 0,
});

describe('recording.current answers', () => {
  it('are kept when no event came while the question was out', () => {
    const recording = signal<RecordingStatePayload | null>(null);
    const finalizing = payload('finalizing');

    expect(adoptCurrentSession(recording, null, finalizing)).toBe(true);
    expect(recording.value).toBe(finalizing);
  });

  it('never put a finished session back to Finalizing', () => {
    const recording = signal<RecordingStatePayload | null>(null);
    const before = recording.value;
    const ready = payload('ready');
    recording.value = ready; // the "ready" event arrives before the answer

    expect(adoptCurrentSession(recording, before, payload('finalizing'))).toBe(false);
    expect(recording.value).toBe(ready);
  });
});
