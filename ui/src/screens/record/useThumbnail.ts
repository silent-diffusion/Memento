// Live thumbnails for the Recording session (BRIDGE.md additions, "Thumbnail cadence"): the contract
// has no preview stream, so the page asks video.thumbnail again and again, never two at once, only
// while the page is visible and the source is on. The preview uses 500 ms, rows and the inset 1000 ms.
import { useEffect, useState } from 'preact/hooks';
import type { BridgeClient } from '../../bridge/client';

export const PREVIEW_EVERY_MS = 500;
export const ROW_EVERY_MS = 1000;

export interface Thumbnail {
  dataUrl: string | null;
  /** The bridge error code of the last attempt ("video.cameraInUse"), null after a picture. */
  errorCode: string | null;
  /** The error's detail: the other app, the privacy switch. */
  errorDetail: string | null;
}

const EMPTY: Thumbnail = { dataUrl: null, errorCode: null, errorDetail: null };

function errorOf(error: unknown): { code: string; detail: string | null } {
  if (typeof error === 'object' && error !== null && 'code' in error) {
    const { code, detail } = error as { code: unknown; detail?: unknown };
    return { code: typeof code === 'string' ? code : 'bridge.internal', detail: typeof detail === 'string' ? detail : null };
  }
  return { code: 'bridge.internal', detail: null };
}

/** Pixels the host should send for `cssWidth` on this screen. */
export function thumbnailWidth(cssWidth: number): number {
  const ratio = typeof window === 'undefined' ? 1 : window.devicePixelRatio || 1;
  // The host answers 16 to 640 pixels (BRIDGE.md, video.thumbnail).
  return Math.min(640, Math.max(16, Math.round(cssWidth * Math.min(2, Math.max(1, ratio)))));
}

/**
 * The latest picture of `sourceId` (null: nothing is asked). `everyMs` null asks once (the picker's
 * tiles); `retryKey` asks again now (Retry camera).
 */
export function useThumbnail(bridge: BridgeClient, sourceId: string | null, cssWidth: number, everyMs: number | null, retryKey = 0): Thumbnail {
  const [thumbnail, setThumbnail] = useState<Thumbnail>(EMPTY);
  const width = thumbnailWidth(cssWidth);

  useEffect(() => {
    setThumbnail(EMPTY);
    if (sourceId === null) {
      return undefined;
    }
    let live = true;
    let timer: ReturnType<typeof setTimeout> | undefined;
    const ask = (): void => {
      timer = undefined;
      if (typeof document !== 'undefined' && document.visibilityState === 'hidden') {
        schedule();
        return;
      }
      bridge
        .call('video.thumbnail', { sourceId, width })
        .then(({ dataUrl }) => {
          if (live) {
            setThumbnail({ dataUrl, errorCode: null, errorDetail: null });
          }
        })
        .catch((error: unknown) => {
          if (live) {
            const { code, detail } = errorOf(error);
            setThumbnail((before) => ({ dataUrl: code === 'bridge.timeout' ? before.dataUrl : null, errorCode: code, errorDetail: detail }));
          }
        })
        .finally(() => {
          if (live) {
            schedule();
          }
        });
    };
    const schedule = (): void => {
      if (everyMs !== null && live) {
        timer = setTimeout(ask, everyMs);
      }
    };
    ask();
    return () => {
      live = false;
      clearTimeout(timer);
    };
  }, [bridge, sourceId, width, everyMs, retryKey]);

  return thumbnail;
}
