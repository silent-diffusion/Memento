import { describe, expect, it } from 'vitest';
import type { ProjectVideo, ProjectVideoTrack, StageStatus } from '../../bridge/types';
import { detailsRemovedRow, detailsVideoRow, metaVideoTail, outlineFacts, removedLine, removeVideoCopy, videoTrackNames } from './videoCopy';
import {
  activeVideoStage,
  MAX_SEEK_LEAD_MS,
  PLAYING_DRIFT_MS,
  SEEKING_DRIFT_MS,
  stillMessage,
  syncAction,
  trackPosition,
  videoLayers,
  type VideoElementState,
} from './videoSync';

function track(overrides: Partial<ProjectVideoTrack> = {}): ProjectVideoTrack {
  return {
    id: 'screen',
    sourceKind: 'display',
    sourceName: 'Display 1',
    width: 2560,
    height: 1440,
    frameRate: 30,
    encoder: 'Hardware encoder',
    hardware: true,
    framesWritten: 1000,
    framesDropped: 0,
    sizeBytes: 1000,
    startOffsetMs: 0,
    durationMs: 60_000,
    endReason: null,
    parts: [{ url: 'blob:screen-0', startOffsetMs: 0, durationMs: 60_000 }],
    ...overrides,
  };
}

const playingVideo = (currentTimeMs: number, more: Partial<VideoElementState> = {}): VideoElementState => ({ currentTimeMs, paused: false, seeking: false, playbackRate: 1, ...more });

describe('trackPosition: which part covers a moment of the recording timeline', () => {
  it('plays the single part from the same local time when the track starts at 0', () => {
    expect(trackPosition(track(), 12_345)).toEqual({ kind: 'play', partIndex: 0, url: 'blob:screen-0', localMs: 12_345, lastMs: 59_999 });
  });

  it('subtracts the part offset: the file itself starts at 0 (ENGINE-NOTES §M.3)', () => {
    const t = track({ startOffsetMs: 70_000, durationMs: 30_000, parts: [{ url: 'blob:a', startOffsetMs: 70_000, durationMs: 30_000 }] });
    expect(trackPosition(t, 75_500)).toMatchObject({ kind: 'play', localMs: 5_500 });
  });

  it('picks the right part when an encoder failure split the track', () => {
    const t = track({
      parts: [
        { url: 'blob:a', startOffsetMs: 0, durationMs: 20_000 },
        { url: 'blob:b', startOffsetMs: 20_000, durationMs: 40_000 },
      ],
    });
    expect(trackPosition(t, 19_999)).toMatchObject({ kind: 'play', partIndex: 0, url: 'blob:a', localMs: 19_999 });
    expect(trackPosition(t, 20_000)).toMatchObject({ kind: 'play', partIndex: 1, url: 'blob:b', localMs: 0 });
    expect(trackPosition(t, 45_000)).toMatchObject({ kind: 'play', partIndex: 1, localMs: 25_000 });
  });

  it('says before, after and between parts instead of a frozen frame', () => {
    const t = track({
      startOffsetMs: 10_000,
      durationMs: 40_000,
      parts: [
        { url: 'blob:a', startOffsetMs: 10_000, durationMs: 10_000 },
        { url: 'blob:b', startOffsetMs: 30_000, durationMs: 20_000 },
      ],
    });
    expect(trackPosition(t, 5_000)).toEqual({ kind: 'before', startsAtMs: 10_000 });
    expect(trackPosition(t, 25_000)).toEqual({ kind: 'gap', resumesAtMs: 30_000 });
    expect(trackPosition(t, 50_000)).toEqual({ kind: 'after', endedAtMs: 50_000 });
    expect(trackPosition(t, 3_600_000)).toEqual({ kind: 'after', endedAtMs: 50_000 });
  });

  it('shows the first frame when the track or a part starts within two seconds (the real host starts video ~200 ms after audio)', () => {
    const t = track({
      startOffsetMs: 180,
      durationMs: 40_000,
      parts: [
        { url: 'blob:a', startOffsetMs: 180, durationMs: 20_000 },
        { url: 'blob:b', startOffsetMs: 21_500, durationMs: 18_680 },
      ],
    });
    expect(trackPosition(t, 0)).toMatchObject({ kind: 'play', partIndex: 0, localMs: 0 });
    expect(trackPosition(t, 20_500)).toMatchObject({ kind: 'play', partIndex: 1, localMs: 0 });
    expect(trackPosition({ ...t, startOffsetMs: 2_500, parts: [{ url: 'blob:a', startOffsetMs: 2_500, durationMs: 20_000 }] }, 0)).toEqual({ kind: 'before', startsAtMs: 2_500 });
  });

  it('marks a part whose file cannot be read yet as preparing', () => {
    const t = track({
      parts: [
        { url: null, startOffsetMs: 0, durationMs: 18_000 },
        { url: null, startOffsetMs: 18_000, durationMs: 42_000 },
      ],
    });
    expect(trackPosition(t, 30_000)).toEqual({ kind: 'preparing', partIndex: 1 });
  });

  it('holds the last frame when the track ended with the recording, but not after a window closed', () => {
    expect(trackPosition(track(), 60_000, 60_400)).toMatchObject({ kind: 'play', localMs: 59_999 });
    expect(trackPosition(track(), 60_400, 60_400)).toMatchObject({ kind: 'play', localMs: 59_999 });
    expect(trackPosition(track({ endReason: "'Zoom Meeting' was closed at 00:01:00." }), 60_000, 60_400)).toEqual({ kind: 'after', endedAtMs: 60_000 });
    expect(trackPosition(track(), 60_000, 3_600_000)).toEqual({ kind: 'after', endedAtMs: 60_000 });
    expect(trackPosition(track(), 60_000)).toEqual({ kind: 'after', endedAtMs: 60_000 });
  });

  it('never aims past the end of a file, and reads a non-number as the start', () => {
    expect(trackPosition(track(), 59_999.9)).toMatchObject({ kind: 'play', localMs: 59_999 });
    expect(trackPosition(track(), Number.NaN)).toMatchObject({ kind: 'play', localMs: 0 });
  });
});

describe('syncAction: the video follows the mix, never the other way round', () => {
  it('leaves a playing video alone while it is within the drift tolerance', () => {
    expect(syncAction(10_000, { playing: true, rate: 1 }, playingVideo(10_000 + PLAYING_DRIFT_MS - 1))).toEqual({ seekToMs: null, play: null, rate: null });
  });

  it('seeks a playing video that drifted further than the tolerance', () => {
    expect(syncAction(10_000, { playing: true, rate: 1 }, playingVideo(10_400)).seekToMs).toBe(10_000);
    expect(syncAction(10_000, { playing: true, rate: 1 }, playingVideo(9_700)).seekToMs).toBe(10_000);
  });

  it('aims a seek made while playing ahead by the last seek latency, at the mix rate and within limits', () => {
    expect(syncAction(10_000, { playing: true, rate: 1 }, playingVideo(0), 200).seekToMs).toBe(10_200);
    expect(syncAction(10_000, { playing: true, rate: 2 }, playingVideo(0, { playbackRate: 2 }), 200).seekToMs).toBe(10_400);
    expect(syncAction(10_000, { playing: true, rate: 1 }, playingVideo(0), 5_000).seekToMs).toBe(10_000 + MAX_SEEK_LEAD_MS);
    expect(syncAction(10_000, { playing: true, rate: 1 }, playingVideo(0), 500, 10_100).seekToMs).toBe(10_100);
    // Paused: the exact frame, no lead.
    expect(syncAction(10_000, { playing: false, rate: 1 }, playingVideo(0, { paused: true }), 500).seekToMs).toBe(10_000);
  });

  it('does not chase the mix with a second seek while one is still running, unless the mix jumped', () => {
    expect(syncAction(10_000, { playing: true, rate: 1 }, playingVideo(10_500, { seeking: true })).seekToMs).toBeNull();
    expect(syncAction(10_000, { playing: true, rate: 1 }, playingVideo(10_000 + SEEKING_DRIFT_MS + 1, { seeking: true })).seekToMs).toBe(10_000);
  });

  it('shows the exact frame while paused', () => {
    expect(syncAction(10_000, { playing: false, rate: 1 }, playingVideo(10_040, { paused: true })).seekToMs).toBe(10_000);
    expect(syncAction(10_000, { playing: false, rate: 1 }, playingVideo(10_002, { paused: true })).seekToMs).toBeNull();
  });

  it('plays, pauses and changes speed with the mix', () => {
    expect(syncAction(0, { playing: true, rate: 1 }, playingVideo(0, { paused: true })).play).toBe(true);
    expect(syncAction(0, { playing: false, rate: 1 }, playingVideo(0)).play).toBe(false);
    expect(syncAction(0, { playing: true, rate: 1.5 }, playingVideo(0)).rate).toBe(1.5);
    expect(syncAction(0, { playing: true, rate: 1.5 }, playingVideo(0, { playbackRate: 1.5 })).rate).toBeNull();
    expect(syncAction(0, { playing: true, rate: 0 }, playingVideo(0)).rate).toBeNull();
  });
});

describe('the well: layers and still states', () => {
  const camera = track({ id: 'camera', sourceKind: 'camera', sourceName: 'Camera', width: 1920, height: 1080, parts: [{ url: 'blob:cam', startOffsetMs: 0, durationMs: 60_000 }] });

  it('draws the screen with the camera inset, or the camera alone', () => {
    expect(videoLayers({ tracks: [track(), camera] })).toEqual({ main: track(), inset: camera });
    expect(videoLayers({ tracks: [track()] })).toEqual({ main: track(), inset: null });
    expect(videoLayers({ tracks: [camera] })).toEqual({ main: camera, inset: null });
    expect(videoLayers({ tracks: [] })).toEqual({ main: null, inset: null });
  });

  it('words each still state, with the end reason when the track has one', () => {
    const t = track({ startOffsetMs: 70_000 });
    expect(stillMessage(t, trackPosition(t, 0), null)?.lead).toBe('Screen recording starts at 00:01:10.');
    expect(stillMessage(t, { kind: 'after', endedAtMs: 3_492_000 }, null)?.lead).toBe('Screen recording ended at 00:58:12.');
    const closed = track({ endReason: "'Zoom Meeting' was closed at 00:58:12." });
    expect(stillMessage(closed, { kind: 'after', endedAtMs: 3_492_000 }, null)).toEqual({ lead: "'Zoom Meeting' was closed at 00:58:12.", detail: 'Audio and the transcript continue.' });
    expect(stillMessage(camera, { kind: 'gap', resumesAtMs: 600_000 }, null)?.lead).toBe('Camera recording resumes at 00:10:00.');
    expect(stillMessage(t, trackPosition(track(), 1000), null)).toBeNull();
  });

  it('says the video is being prepared, with the video stage label while it runs', () => {
    const stages: StageStatus[] = [
      { stage: 'stored', state: 'done', percent: null, label: 'Done' },
      { stage: 'video', state: 'active', percent: 42, label: 'Encoding 42% · GPU' },
    ];
    const stage = activeVideoStage(stages);
    expect(stillMessage(track(), { kind: 'preparing', partIndex: 0 }, stage)).toEqual({ lead: 'The video is being prepared.', detail: 'Encoding 42% · GPU' });
    expect(stillMessage(track(), { kind: 'preparing', partIndex: 0 }, null)).toEqual({ lead: 'The video is being prepared.', detail: null });
    expect(activeVideoStage([{ stage: 'video', state: 'done', percent: null, label: 'Done' }])).toBeNull();
  });
});

describe('video wording', () => {
  const GIB = 1024 ** 3;
  const NOW = new Date(2026, 9, 25, 12, 0);
  const video = (more: Partial<ProjectVideo> = {}): ProjectVideo => ({
    tracks: [
      track({ sizeBytes: 1.4 * GIB, framesDropped: 2 }),
      track({ id: 'camera', sourceKind: 'camera', width: 1920, height: 1080, sizeBytes: 0.5 * GIB, framesDropped: 1 }),
    ],
    pip: { enabled: true, corner: 'bottomRight' },
    sizeBytes: 1.9 * GIB,
    removed: null,
    ...more,
  });
  const removed = { at: '2026-10-20T09:00:00+02:00', bytesFreed: 1.9 * GIB };

  it('ends the header meta with the sources and the size, or says the video was removed', () => {
    expect(metaVideoTail(video())).toBe('screen + camera · 1.9 GB video');
    expect(metaVideoTail(video({ tracks: [track()] }))).toBe('screen · 1.9 GB video');
    expect(metaVideoTail(video({ tracks: [], sizeBytes: 0, removed }))).toBe('video removed');
    expect(metaVideoTail(video({ tracks: [] }))).toBeNull();
  });

  it('words the removed line, the outline facts and the details row in plain numbers', () => {
    expect(removedLine(removed, NOW)).toBe('Video was removed on Oct 20 to free 1.9 GB. Audio tracks, transcript and documents are intact.');
    expect(outlineFacts(video())).toEqual([
      { label: 'Sources', value: 'Screen + camera', mono: false },
      { label: 'Resolution', value: '2560 × 1440', mono: true },
      { label: 'Frame rate', value: '30 fps', mono: true },
      { label: 'Size', value: '1.9 GB', mono: true },
    ]);
    expect(detailsVideoRow(video())).toBe('2560 × 1440 · 30 fps · hardware encoder · 3 dropped frames');
    expect(detailsVideoRow(video({ tracks: [track({ hardware: false, framesDropped: 1 })] }))).toBe('2560 × 1440 · 30 fps · software encoder · 1 dropped frame');
    expect(detailsRemovedRow(removed, NOW)).toBe('Removed on Oct 20 · 1.9 GB freed');
    expect(videoTrackNames(video().tracks)).toEqual(['Screen', 'Camera']);
  });

  it('names the recording and the size freed in the Remove video dialog, and that it cannot be undone', () => {
    expect(removeVideoCopy('Design review', video())).toEqual({
      title: 'Remove the video of "Design review"?',
      body: 'Deletes the screen and camera video from this PC and frees 1.9 GB. The audio tracks, transcript and documents are kept. This cannot be undone.',
    });
  });
});
