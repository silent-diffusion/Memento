// The stage's live preview and the right column's Encoder card (DESIGN.md §18.1), transcribed from
// renders/RecordVideo.dc.html. The preview is the latest thumbnail of the screen track (500 ms) with
// the camera's (1000 ms) drawn as the inset in the chosen corner: both tracks are always recorded,
// only the preview and Review draw the inset (BRIDGE.md, recording.setPip).
import type { JSX } from 'preact';
import type { PipState, VideoTrackStatus } from '../../bridge/types';
import { formatSize } from '../../format/storage';
import { cornerName, encoderKind, formatBitrate, formatFrameRate, formatResolution } from '../../format/video';
import type { RecordPhase } from '../../format/recordFooter';

interface LivePreviewProps {
  phase: RecordPhase;
  /** The main picture: the screen track's source, else the camera's. */
  main: string | null;
  /** The camera is drawn as the inset (its picture may not have arrived yet). */
  insetOn: boolean;
  /** The camera's picture for the inset. */
  inset: string | null;
  pip: PipState;
  caption: string;
  /** A sentence over the picture while the main track waits or after it stopped. */
  notice: string | null;
}

export function LivePreview({ phase, main, insetOn, inset, pip, caption, notice }: LivePreviewProps): JSX.Element {
  const live = phase === 'recording' || phase === 'paused';
  const tag = phase === 'paused' ? 'PAUSED' : live ? 'LIVE' : 'PREVIEW';
  const label = `${live ? 'Live preview' : 'Preview'}: ${caption}${insetOn ? `, camera inset ${cornerName(pip.corner).toLocaleLowerCase()}` : ''}`;
  return (
    <div class="rec-preview" role="img" aria-label={label}>
      {main === null ? (
        <>
          <div class="rec-preview-win" aria-hidden="true" />
          <div class="rec-preview-bar" aria-hidden="true" />
        </>
      ) : (
        <img class="rec-preview-main" src={main} alt="" aria-hidden="true" />
      )}
      {!insetOn ? null : inset === null ? (
        <div class={`rec-preview-cam rec-preview-cam--empty rec-preview-cam--${pip.corner}`} aria-hidden="true" />
      ) : (
        <img class={`rec-preview-cam rec-preview-cam--${pip.corner}`} src={inset} alt="" aria-hidden="true" />
      )}
      {notice === null ? null : (
        <div class="rec-preview-notice" aria-hidden="true">
          {notice}
        </div>
      )}
      <span class="mono rec-preview-caption" aria-hidden="true">
        {caption}
      </span>
      <span class="mono rec-preview-tag" aria-hidden="true">
        {tag}
      </span>
    </div>
  );
}

/** The Encoder card's facts from the session's live tracks: the screen's picture first, sums for the rest. */
export function encoderFacts(tracks: readonly VideoTrackStatus[]): { kind: string; hardware: boolean; resolution: string; frameRate: string; bitrate: string; size: string } | null {
  const shown = tracks.filter((t) => t.state !== 'ended');
  const main = shown.find((t) => t.trackId === 'screen') ?? shown[0];
  if (main === undefined) {
    return null;
  }
  const hardware = shown.every((t) => t.hardware);
  const dropped = shown.reduce((sum, t) => sum + t.framesDropped, 0);
  return {
    kind: encoderKind(hardware),
    hardware,
    resolution: formatResolution(main.width, main.height),
    frameRate: `${formatFrameRate(main.frameRate)} · ${dropped} dropped`,
    bitrate: formatBitrate(shown.reduce((sum, t) => sum + t.actualBitsPerSecond, 0)),
    size: formatSize(tracks.reduce((sum, t) => sum + t.bytesWritten, 0)),
  };
}

export function EncoderCard({ tracks }: { tracks: readonly VideoTrackStatus[] }): JSX.Element | null {
  const facts = encoderFacts(tracks);
  if (facts === null) {
    return null;
  }
  return (
    <section class="rec-card rec-side-card rec-encoder" aria-label="Encoder">
      <div class="rec-card-head">
        <span class="lbl">Encoder</span>
        <span class={facts.hardware ? 'pill done' : 'pill queued'}>{facts.kind}</span>
      </div>
      <dl class="rec-encoder-facts">
        <dt>Resolution</dt>
        <dd class="mono">{facts.resolution}</dd>
        <dt>Frame rate</dt>
        <dd class="mono">{facts.frameRate}</dd>
        <dt>Bitrate</dt>
        <dd class="mono">{facts.bitrate}</dd>
        <dt>Video so far</dt>
        <dd class="mono">{facts.size}</dd>
      </dl>
      <p class="rec-encoder-note">Change resolution and quality in Settings › Recording. Changes apply to the next recording.</p>
    </section>
  );
}
