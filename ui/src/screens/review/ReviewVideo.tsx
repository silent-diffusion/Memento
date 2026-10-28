// Review with video (DESIGN.md §18.3), transcribed from renders/ReviewVideo.dc.html: the 16:9 well above
// the waveform with the screen track and the camera inset, its overlay row (Camera inset and its corner,
// Audio only, Fullscreen), the pressed-in Audio only and Video removed line, the outline's Video block
// and the Remove video dialog (§18.6). The mix stays the one playhead: every <video> follows it
// (videoSync.ts) and is muted; the inset corner is view state and is never saved.
import type { JSX } from 'preact';
import { useEffect, useRef, useState } from 'preact/hooks';
import type { PipCorner, ProjectVideo, ProjectVideoTrack, StageStatus } from '../../bridge/types';
import { Dialog } from '../../components/Overlay';
import { CameraIcon, ExitFullscreenIcon, FullscreenIcon, MinimizeIcon, RestoreIcon } from '../../components/icons';
import { formatDuration } from '../../format/duration';
import { PIP_CORNERS } from '../../format/video';
import type { AppStore } from '../../state/store';
import { useUiFlag } from '../../state/uiPrefs';
import { HIDDEN_LINE, outlineFacts, removedLine, removeVideoCopy } from './videoCopy';
import { useVideoSync } from './useVideoSync';
import { activeVideoStage, stillMessage, trackPosition, videoLayers, type MixState } from './videoSync';
import './reviewVideo.css';

// ---------------------------------------------------------------------------------------------
// View state: inset, corner, Audio only, Fullscreen (never saved; there is no contract method for it)
// ---------------------------------------------------------------------------------------------

export interface VideoView {
  inset: boolean;
  corner: PipCorner;
  audioOnly: boolean;
  full: boolean;
  /** 2.7.0: the video collapsed to a small corner player, so the transcript gets the space back (remembered on this PC). */
  mini: boolean;
  setMini: (on: boolean) => void;
  setInset: (on: boolean) => void;
  setCorner: (corner: PipCorner) => void;
  setAudioOnly: (on: boolean) => void;
  setFull: (on: boolean) => void;
  /** The control to focus when the area it lives in appears (after Audio only, Show video). */
  focusNext: { current: 'show' | 'audioOnly' | null };
}

function isTyping(target: EventTarget | null): boolean {
  return (
    target instanceof HTMLInputElement ||
    target instanceof HTMLTextAreaElement ||
    target instanceof HTMLSelectElement ||
    (target instanceof HTMLElement && target.isContentEditable)
  );
}

/**
 * The player's view of the video, starting where the inset was when recording ended (`pip`). Esc
 * leaves Fullscreen, and does only that: it is caught before the transcript's own Esc handlers, but a
 * menu, a field or a dialog keeps its Esc.
 */
export function useVideoView(video: ProjectVideo | null, store: AppStore): VideoView {
  const [inset, setInset] = useState(video?.pip.enabled ?? true);
  const [corner, setCorner] = useState<PipCorner>(video?.pip.corner ?? 'bottomRight');
  const [audioOnly, setAudioOnlyState] = useState(false);
  const [full, setFull] = useState(false);
  const [mini, setMiniFlag] = useUiFlag('review.videoMini', false);
  const focusNext = useRef<'show' | 'audioOnly' | null>(null);
  const known = useRef(video !== null);
  // The project arrives after the first render: start from its inset once.
  useEffect(() => {
    if (video !== null && !known.current) {
      known.current = true;
      setInset(video.pip.enabled);
      setCorner(video.pip.corner);
    }
  }, [video]);
  const playable = video !== null && video.removed === null && video.tracks.length > 0;
  const isFull = full && playable && !audioOnly;

  // Listening the whole time (not only from Fullscreen on) so an Esc pressed the moment Fullscreen opens is never lost:
  // effects run after paint, which WebView2 can hold back while its window is covered (found by m8-video-ui).
  const fullNow = useRef(isFull);
  fullNow.current = isFull;
  useEffect(() => {
    const onKey = (event: KeyboardEvent): void => {
      if (!fullNow.current || event.key !== 'Escape' || event.defaultPrevented || store.dialog.value !== null || store.overlays.value > 0) {
        return;
      }
      const target = event.target;
      const inPopover = target instanceof Element && target.closest('[role="menu"],[role="dialog"],[role="listbox"],.popover,.speaker-menu') !== null;
      if (isTyping(target) || inPopover) {
        return;
      }
      event.preventDefault();
      event.stopPropagation();
      setFull(false);
    };
    // Window capture runs before the document-level Esc of selection mode and the transcript filter.
    window.addEventListener('keydown', onKey, true);
    return () => {
      window.removeEventListener('keydown', onKey, true);
    };
  }, [store]);

  return {
    inset,
    corner,
    audioOnly,
    full: isFull,
    // Fullscreen wins over the mini-player; leaving Fullscreen goes back to it.
    mini: mini && !isFull,
    setMini: (on) => {
      setMiniFlag(on);
      if (on) {
        setFull(false);
      }
    },
    setInset,
    setCorner,
    setAudioOnly: (on) => {
      focusNext.current = on ? 'show' : 'audioOnly';
      setAudioOnlyState(on);
      if (on) {
        setFull(false);
      }
    },
    setFull,
    focusNext,
  };
}

// ---------------------------------------------------------------------------------------------
// The well
// ---------------------------------------------------------------------------------------------

function SyncedVideo({ track, timeMs, recordingMs, mix, class: className }: { track: ProjectVideoTrack; timeMs: number; recordingMs: number; mix: MixState; class: string }): JSX.Element | null {
  const ref = useRef<HTMLVideoElement | null>(null);
  const position = trackPosition(track, timeMs, recordingMs);
  useVideoSync(ref, position, mix);
  if (position.kind !== 'play') {
    return null;
  }
  // Muted (the sound is the mix's), no controls (the player strip's are the only ones), never the playhead.
  return <video ref={ref} class={className} src={position.url} muted playsInline preload="auto" aria-hidden="true" data-track={track.id} />;
}

interface VideoAreaProps {
  video: ProjectVideo;
  view: VideoView;
  positionMs: number;
  /** The recording's length: a track that ended with it holds its last frame. */
  recordingMs: number;
  mix: MixState;
  /** The current chapter's title, or null without chapters. */
  chapter: string | null;
  /** The recording's stages, for "The video is being prepared" with the video stage's label. */
  stages: readonly StageStatus[];
}

function Still({ track, timeMs, recordingMs, stage }: { track: ProjectVideoTrack; timeMs: number; recordingMs: number; stage: StageStatus | null }): JSX.Element | null {
  const message = stillMessage(track, trackPosition(track, timeMs, recordingMs), stage);
  if (message === null) {
    return null;
  }
  return (
    <div class="vid-still" role="status">
      <span class="vid-still-lead">{message.lead}</span>
      {message.detail === null ? null : <span class="vid-still-detail">{message.detail}</span>}
    </div>
  );
}

function VideoWell({ video, view, positionMs, recordingMs, mix, chapter, stages }: VideoAreaProps): JSX.Element {
  const { main, inset } = videoLayers(video);
  const stage = activeVideoStage(stages);
  const audioOnlyRef = useRef<HTMLButtonElement | null>(null);
  useEffect(() => {
    if (view.focusNext.current === 'audioOnly') {
      view.focusNext.current = null;
      audioOnlyRef.current?.focus();
    }
  }, []);
  const showInset = inset !== null && view.inset;
  const caption = chapter === null ? formatDuration(positionMs) : `${chapter} · ${formatDuration(positionMs)}`;
  const insetPlays = inset !== null && trackPosition(inset, positionMs, recordingMs).kind === 'play';
  if (view.mini) {
    return (
      <div class="vid-wrap vid-wrap--mini">
        <div class="vid" role="group" aria-label="Video (minimized)">
          {main === null ? null : (
            <>
              <SyncedVideo track={main} timeMs={positionMs} recordingMs={recordingMs} mix={mix} class="vid-main" />
              <Still track={main} timeMs={positionMs} recordingMs={recordingMs} stage={stage} />
            </>
          )}
          <div class="vid-ovl">
            <span class="vid-spacer" />
            <button
              class="btn vb"
              type="button"
              aria-label="Restore video"
              title="Restore video"
              onClick={() => {
                view.setMini(false);
              }}
            >
              <RestoreIcon size={14} />
            </button>
          </div>
        </div>
      </div>
    );
  }
  return (
    <div class="vid-wrap">
      <div class="vid" role="group" aria-label="Video">
        {main === null ? null : (
          <>
            <SyncedVideo track={main} timeMs={positionMs} recordingMs={recordingMs} mix={mix} class="vid-main" />
            <Still track={main} timeMs={positionMs} recordingMs={recordingMs} stage={stage} />
          </>
        )}
        {showInset ? (
          <div class={`vid-cam vid-cam--${view.corner}${insetPlays ? '' : ' vid-cam--off'}`}>
            <SyncedVideo track={inset} timeMs={positionMs} recordingMs={recordingMs} mix={mix} class="vid-cam-video" />
          </div>
        ) : null}
        <span class="mono vid-caption">{caption}</span>
        <div class="vid-ovl">
          {inset !== null && main !== null ? (
            <>
              <button
                class={view.inset ? 'btn vb on' : 'btn vb'}
                type="button"
                aria-pressed={view.inset}
                onClick={() => {
                  view.setInset(!view.inset);
                }}
              >
                <CameraIcon size={14} strokeWidth={2} />
                Camera inset
              </button>
              {view.inset ? (
                <div class="vid-corners" role="group" aria-label="Inset corner">
                  {PIP_CORNERS.map(({ corner, name }) => (
                    <button
                      key={corner}
                      class={corner === view.corner ? 'btn vid-corner on' : 'btn vid-corner'}
                      type="button"
                      aria-label={name}
                      aria-pressed={corner === view.corner}
                      onClick={() => {
                        view.setCorner(corner);
                      }}
                    />
                  ))}
                </div>
              ) : null}
            </>
          ) : null}
          <span class="vid-spacer" />
          <button
            class="btn vb"
            type="button"
            aria-label="Minimize video"
            title="Minimize to a small corner player"
            onClick={() => {
              view.setMini(true);
            }}
          >
            <MinimizeIcon size={14} />
          </button>
          <button
            ref={audioOnlyRef}
            class="btn vb"
            type="button"
            onClick={() => {
              view.setAudioOnly(true);
            }}
          >
            Audio only
          </button>
          <button
            class="btn vb"
            type="button"
            aria-label={view.full ? 'Exit fullscreen' : 'Fullscreen'}
            onClick={() => {
              view.setFull(!view.full);
            }}
          >
            {view.full ? <ExitFullscreenIcon size={14} /> : <FullscreenIcon size={14} />}
            {view.full ? 'Exit fullscreen' : 'Fullscreen'}
          </button>
        </div>
      </div>
    </div>
  );
}

/** The pressed-in line in place of the well: hidden by Audio only (with Show video), or removed. */
function VideoLine({ text, onShow, focusNext }: { text: string; onShow: (() => void) | null; focusNext: VideoView['focusNext'] | null }): JSX.Element {
  const showRef = useRef<HTMLButtonElement | null>(null);
  useEffect(() => {
    if (focusNext?.current === 'show') {
      focusNext.current = null;
      showRef.current?.focus();
    }
  }, []);
  return (
    <div class="vid-line">
      <span>{text}</span>
      {onShow === null ? null : (
        <button ref={showRef} class="btn ghost ghost-main" type="button" onClick={onShow}>
          Show video
        </button>
      )}
    </div>
  );
}

/**
 * The video area of the player strip: the well, or the line. Nothing for a recording that never had
 * video, and nothing for one whose tracks list is empty without a removal (it cannot be played).
 */
export function VideoArea(props: VideoAreaProps & { now: Date }): JSX.Element | null {
  const { video, view, now } = props;
  if (video.removed !== null) {
    return <VideoLine text={removedLine(video.removed, now)} onShow={null} focusNext={null} />;
  }
  if (video.tracks.length === 0) {
    return null;
  }
  if (view.audioOnly) {
    return (
      <VideoLine
        text={HIDDEN_LINE}
        focusNext={view.focusNext}
        onShow={() => {
          view.setAudioOnly(false);
        }}
      />
    );
  }
  return <VideoWell {...props} />;
}

// ---------------------------------------------------------------------------------------------
// Outline › Video, and the Remove video dialog
// ---------------------------------------------------------------------------------------------

/** The outline's Video block after Chapters: Sources, Resolution, Frame rate, Size, and Remove video, keep audio…. */
export function VideoOutlineBlock({ video, onRemove }: { video: ProjectVideo; onRemove: () => void }): JSX.Element | null {
  if (video.removed !== null || video.tracks.length === 0) {
    return null;
  }
  return (
    <div class="outline-video" role="group" aria-labelledby="review-video-label">
      <div class="outline-head">
        <span id="review-video-label" class="lbl">Video</span>
      </div>
      <dl class="vid-facts">
        {outlineFacts(video).map((fact) => (
          <div key={fact.label} class="vid-fact">
            <dt>{fact.label}</dt>
            <dd class={fact.mono ? 'mono' : undefined}>{fact.value}</dd>
          </div>
        ))}
      </dl>
      <button class="btn ghost ghost-main" type="button" aria-haspopup="dialog" onClick={onRemove}>
        Remove video, keep audio…
      </button>
    </div>
  );
}

/** §18.6 Remove video: names the recording and the size freed; Cancel first, Remove video in `danger`. */
export function RemoveVideoDialog({ title, video, onCancel, onConfirm }: { title: string; video: ProjectVideo; onCancel: () => void; onConfirm: () => Promise<void> }): JSX.Element {
  const [busy, setBusy] = useState(false);
  const copy = removeVideoCopy(title, video);
  return (
    <Dialog
      titleId="remove-video-title"
      title={copy.title}
      onEscape={onCancel}
      actions={
        <>
          <button class="btn g" type="button" data-autofocus onClick={onCancel}>
            Cancel
          </button>
          <button
            class="btn d"
            type="button"
            disabled={busy}
            onClick={() => {
              setBusy(true);
              void onConfirm().finally(() => {
                setBusy(false);
              });
            }}
          >
            Remove video
          </button>
        </>
      }
    >
      <p class="dialog-body">{copy.body}</p>
    </Dialog>
  );
}
