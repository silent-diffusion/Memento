// Slaves one <video> to the mix (videoSync.ts decides; this applies): after every render (the player
// re-renders on every frame while it plays, and on every seek, speed change, play and pause) and when
// the video finishes loading or seeking. The video never moves the playhead.
import { useEffect, useRef } from 'preact/hooks';
import { syncAction, type MixState, type TrackPosition } from './videoSync';

function startPlaying(video: HTMLVideoElement): void {
  try {
    // play() answers a promise in browsers; a muted video is always allowed to play.
    void Promise.resolve(video.play()).catch((error: unknown) => {
      console.warn('[review] the video could not play', error);
    });
  } catch (error) {
    console.warn('[review] the video could not play', error);
  }
}

/** How long this element's last seek took, measured from `seeking` to `seeked`. */
const seekLatency = new WeakMap<HTMLVideoElement, number>();

/** Applies one sync step to `video`. Exported for tests. */
export function applySync(video: HTMLVideoElement, position: TrackPosition, mix: MixState): void {
  if (position.kind !== 'play') {
    if (!video.paused) {
      video.pause();
    }
    return;
  }
  const action = syncAction(
    position.localMs,
    mix,
    { currentTimeMs: video.currentTime * 1000, paused: video.paused, seeking: video.seeking, playbackRate: video.playbackRate },
    seekLatency.get(video) ?? 0,
    position.lastMs,
  );
  if (action.rate !== null) {
    video.playbackRate = action.rate;
  }
  if (action.seekToMs !== null) {
    video.currentTime = action.seekToMs / 1000;
  }
  if (action.play === true) {
    startPlaying(video);
  } else if (action.play === false) {
    video.pause();
  }
}

export function useVideoSync(ref: { current: HTMLVideoElement | null }, position: TrackPosition, mix: MixState): void {
  const latest = useRef({ position, mix });
  latest.current = { position, mix };

  useEffect(() => {
    const video = ref.current;
    if (video !== null) {
      applySync(video, position, mix);
    }
  });

  const url = position.kind === 'play' ? position.url : null;
  useEffect(() => {
    const video = ref.current;
    if (video === null) {
      return undefined;
    }
    let seekStarted: number | null = null;
    const again = (): void => {
      applySync(video, latest.current.position, latest.current.mix);
    };
    const onSeeking = (): void => {
      seekStarted = performance.now();
    };
    const onSeeked = (): void => {
      if (seekStarted !== null) {
        seekLatency.set(video, performance.now() - seekStarted);
        seekStarted = null;
      }
      again();
    };
    video.addEventListener('loadedmetadata', again);
    video.addEventListener('seeking', onSeeking);
    video.addEventListener('seeked', onSeeked);
    return () => {
      video.removeEventListener('loadedmetadata', again);
      video.removeEventListener('seeking', onSeeking);
      video.removeEventListener('seeked', onSeeked);
    };
  }, [ref, url]);
}
