import type { JSX } from 'preact';
import { useState } from 'preact/hooks';
import type { FooterStatusPayload, StorageLowSpacePayload, VideoQuality } from '../bridge/types';
import { lowSpaceCopy } from '../format/messages';
import { encoderFallbackBanner, lowSpaceVideoBanner, videoDiskFullBanner } from '../format/videoProblems';
import { openExternal } from '../state/actions';
import { useServices } from '../state/context';
import { endVideoTracks } from '../state/videoEvents';
import { InfoIcon } from './icons';

/** Windows storage settings, where Storage Sense and cleanup recommendations live. */
export const STORAGE_SETTINGS_URL = 'ms-settings:storagesense';

/**
 * What the low-space banner says: the host's storage.lowSpace (sent while recording), else the footer's own reading,
 * so the banner also shows when space runs low with no recording and when Memento starts with a full drive
 * (DESIGN.md §17). Whether recording continues follows the footer, which knows when the recording ended.
 */
export function lowSpaceNow(lowSpace: StorageLowSpacePayload | null, footer: FooterStatusPayload | null): StorageLowSpacePayload | null {
  if (lowSpace !== null) {
    return footer === null ? lowSpace : { ...lowSpace, recordingContinues: footer.recording.active };
  }
  if (footer?.storage.lowSpace === true && footer.storage.freeBytes !== null) {
    return { freeBytes: footer.storage.freeBytes, thresholdBytes: 0, recordingContinues: footer.recording.active, transcriptionPaused: true };
  }
  return null;
}

/** 2.0 video: the Recording session's video, so the banners can speak in video figures (DESIGN.md §18.6). */
export interface VideoBannerContext {
  sessionId: string;
  /** Audio and video together, per second: how fast the drive fills. */
  bytesPerSecond: number;
  /** A video track is recording or waiting. */
  videoRecording: boolean;
  /** The quality the session started with (Settings › Recording). */
  quality: VideoQuality;
}

const LOWER: Record<VideoQuality, VideoQuality | null> = { best: 'balanced', balanced: 'smaller', smaller: null };

function Banner({ lead, rest, children }: { lead: string; rest: string; children?: JSX.Element | JSX.Element[] | null }): JSX.Element {
  return (
    <div class="banner" role="status">
      <InfoIcon size={18} class="banner-icon" />
      <span class="banner-text">
        <span class="banner-lead">{lead}</span>
        {rest}
      </span>
      {children}
    </div>
  );
}

function VideoBanners({ video }: { video: VideoBannerContext }): JSX.Element | null {
  const services = useServices();
  const { bridge, store } = services;
  const [quality, setQuality] = useState<VideoQuality>(video.quality);
  const [busy, setBusy] = useState(false);
  const problems = store.videoProblems.value.filter((p) => p.sessionId === video.sessionId);
  const fallback = problems.find((p) => p.code === 'video.encoderFallback');
  const diskFull = problems.find((p) => p.code === 'video.diskFull');
  const lower = LOWER[quality];
  const fail = (title: string) => (error: unknown) => {
    store.toasts.show({ tone: 'warning', title, body: `${error instanceof Error ? error.message : 'Memento did not answer.'} Recording continues as it was.` });
  };
  if (fallback === undefined && diskFull === undefined) {
    return null;
  }
  return (
    <>
      {fallback === undefined ? null : (
        <Banner {...encoderFallbackBanner(fallback.atMs)}>
          {lower === null ? null : (
            <button
              class="btn banner-action"
              type="button"
              disabled={busy}
              onClick={() => {
                setBusy(true);
                bridge
                  .call('recording.setVideoQuality', { sessionId: video.sessionId, quality: lower })
                  .then(() => {
                    setQuality(lower);
                  })
                  .catch(fail('The quality could not be lowered'))
                  .finally(() => {
                    setBusy(false);
                  });
              }}
            >
              Lower quality
            </button>
          )}
        </Banner>
      )}
      {diskFull === undefined ? null : (
        <Banner {...videoDiskFullBanner(diskFull.atMs)}>
          <button
            class="btn banner-action"
            type="button"
            onClick={() => {
              openExternal(services, STORAGE_SETTINGS_URL, 'Storage settings');
            }}
          >
            Free up space
          </button>
        </Banner>
      )}
    </>
  );
}

/**
 * The banner slot at the top of a content column (DESIGN.md §5.19). Shows conditions that are still
 * safe and stay until resolved: low disk space; in the Recording session with video (2.0, §18.6) also
 * the encoder that fell back to software and video stopped by a full drive, and the low-space banner
 * in video figures with "Stop video now".
 */
export function BannerSlot({ video = null }: { video?: VideoBannerContext | null }): JSX.Element | null {
  const services = useServices();
  const lowSpace = lowSpaceNow(services.store.lowSpace.value, services.store.footer.value);
  const videoBanners = video === null ? null : <VideoBanners video={video} />;
  if (lowSpace === null) {
    return videoBanners;
  }
  const freeUp = (
    <button
      class="btn banner-action"
      type="button"
      onClick={() => {
        openExternal(services, STORAGE_SETTINGS_URL, 'Storage settings');
      }}
    >
      Free up space
    </button>
  );
  if (video?.videoRecording === true && lowSpace.recordingContinues) {
    const copy = lowSpaceVideoBanner(lowSpace.freeBytes, video.bytesPerSecond);
    return (
      <>
        {videoBanners}
        <Banner lead={copy.lead} rest={copy.rest}>
          <span class="banner-actions">
            <button
              class="btn banner-action"
              type="button"
              onClick={() => {
                endVideoTracks(services.bridge, services.store, video.sessionId).catch((error: unknown) => {
                  services.store.toasts.show({
                    tone: 'warning',
                    title: 'Video could not be stopped',
                    body: `${error instanceof Error ? error.message : 'Memento did not answer.'} Recording continues as it was.`,
                  });
                });
              }}
            >
              Stop video now
            </button>
            {freeUp}
          </span>
        </Banner>
      </>
    );
  }
  const copy = lowSpaceCopy(lowSpace);
  return (
    <>
      {videoBanners}
      <Banner lead={copy.lead} rest={copy.rest}>
        {freeUp}
      </Banner>
    </>
  );
}
