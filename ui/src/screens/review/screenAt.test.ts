import { describe, expect, it } from 'vitest';
import type { ProjectVideoTrack } from '../../bridge/types';
import { covers, screenAt, screenGapMessage, videoLayers } from './videoSync';

// 2.8.0: Review shows the screen recorded at the playhead, and a calm placeholder where none was.

function track(trackId: string, startOffsetMs: number, stoppedAtMs: number, id: 'screen' | 'camera' = 'screen'): ProjectVideoTrack {
  return {
    id,
    trackId,
    sourceKind: id === 'camera' ? 'camera' : 'display',
    sourceName: trackId,
    width: 1920,
    height: 1080,
    frameRate: 30,
    encoder: 'x',
    hardware: true,
    framesWritten: 0,
    framesDropped: 0,
    sizeBytes: 0,
    startOffsetMs,
    durationMs: stoppedAtMs - startOffsetMs,
    endReason: null,
    parts: [{ url: `blob:${trackId}`, startOffsetMs, durationMs: stoppedAtMs - startOffsetMs }],
    stoppedAtMs,
  };
}

describe('the screen at the playhead', () => {
  // Display 1 from 0 to 10 s; a window added at 4 s and removed at 7 s; nothing from 10 to 15 s; Display 2 from 15 s.
  const tracks = [track('screen', 0, 10_000), track('screen-2', 4_000, 7_000), track('screen-3', 15_000, 30_000), track('camera', 0, 30_000, 'camera')];

  it('picks the most recently started screen that covers the moment', () => {
    expect(screenAt(tracks, 1_000)?.trackId).toBe('screen');
    expect(screenAt(tracks, 5_000)?.trackId).toBe('screen-2');
    expect(screenAt(tracks, 7_000)?.trackId).toBe('screen');
    expect(screenAt(tracks, 20_000)?.trackId).toBe('screen-3');
    expect(covers(track('screen-2', 4_000, 7_000), 7_000)).toBe(false);
  });

  it('in a gap shows the next screen, else the last one, and says so calmly', () => {
    expect(screenAt(tracks, 12_000)?.trackId).toBe('screen-3');
    expect(screenGapMessage({ tracks }, 12_000)).toEqual({ lead: 'No screen was recorded here.', detail: 'Screen recording resumes at 00:00:15. Audio and the transcript continue.' });
    expect(screenGapMessage({ tracks }, 5_000)).toBeNull();
    const ended = [track('screen', 0, 5_000), track('screen-2', 1_000, 6_000)];
    expect(screenAt(ended, 9_000)?.trackId).toBe('screen-2');
    expect(screenGapMessage({ tracks: ended }, 9_000)?.lead).toBe('No screen was recorded from here.');
  });

  it('keeps one screen and the camera as before', () => {
    const one = [track('screen', 0, 10_000), track('camera', 0, 10_000, 'camera')];
    expect(videoLayers({ tracks: one }, 50_000)).toEqual({ main: one[0], inset: one[1] });
    expect(screenGapMessage({ tracks: one }, 50_000)).toBeNull();
    expect(videoLayers({ tracks: tracks }, 5_000).main?.trackId).toBe('screen-2');
  });
});
