// The VIDEO section of the Sources card (DESIGN.md §18.1), transcribed from renders/RecordVideo.dc.html:
// Screen, Window and Camera rows (toggle, name, sub-line, a 72 × 42 live thumbnail, a selector chip),
// the Screen + camera inset with its corner picker, and the §18.6 inline and permission cards.
import type { JSX } from 'preact';
import type { PipState, VideoListSourcesResult, VideoSource } from '../../bridge/types';
import { Toggle } from '../../components/Controls';
import { ChevronDownIcon } from '../../components/icons';
import { moveFocus } from '../../components/keyboard';
import { SelectMenu } from '../../components/Menus';
import { cameraLabel, PIP_CORNERS } from '../../format/video';
import type { VideoProblemCard } from '../../format/videoProblems';
import type { Thumbnail } from './useThumbnail';
import { findSource, type VideoSelection } from './videoSelection';

export type VideoRow = 'screen' | 'window' | 'camera';

export type VideoCardAction = 'retry' | 'withoutCamera' | 'pickCamera' | 'openSettings' | 'audioOnly';

/** A row's sub-line while its track waits or after it ended: "Disconnected at 00:23:40 · waiting". */
export interface RowNote {
  text: string;
  tone: 'accent' | 'danger';
}

interface VideoSourcesProps {
  available: boolean;
  sources: VideoListSourcesResult | null;
  loadError: string | null;
  selection: VideoSelection | null;
  /** The Screen row's sub-line ("Display 1 · 2560 × 1440 · 30 fps"), worked out by the screen. */
  screenLine: string | null;
  /** A track is recording: the header's "Recording video" pill. */
  recordingVideo: boolean;
  notes: Partial<Record<VideoRow, RowNote>>;
  busy: ReadonlySet<VideoRow | 'pip'>;
  thumbs: { screen: Thumbnail; camera: Thumbnail };
  cards: VideoProblemCard[];
  onToggle: (row: VideoRow, on: boolean) => void;
  onOpenPicker: (mode: 'screen' | 'window') => void;
  onCamera: (cameraId: string) => void;
  onPip: (pip: PipState) => void;
  onCardAction: (card: VideoProblemCard, action: VideoCardAction) => void;
}

function Thumb({ dataUrl, kind }: { dataUrl: string | null; kind: 'win' | 'cam' }): JSX.Element {
  return (
    <div class="rec-thumb" aria-hidden="true">
      {dataUrl === null ? <div class={kind === 'cam' ? 'rec-thumb-cam' : 'rec-thumb-win'} /> : <img class="rec-thumb-img" src={dataUrl} alt="" />}
    </div>
  );
}

function PickChip({ text, label, disabled, onClick }: { text: string; label: string; disabled: boolean; onClick: () => void }): JSX.Element {
  return (
    <button class="btn ghost rec-pick" type="button" aria-haspopup="dialog" aria-label={label} disabled={disabled} onClick={onClick}>
      <span class="rec-pick-text">{text}</span>
      <ChevronDownIcon size={12} strokeWidth={2.25} />
    </button>
  );
}

const CARD_ACTIONS: Record<VideoProblemCard['kind'], { action: VideoCardAction; label: string }[]> = {
  cameraInUse: [
    { action: 'retry', label: 'Retry camera' },
    { action: 'withoutCamera', label: 'Continue without camera' },
    { action: 'pickCamera', label: 'Pick another camera' },
  ],
  cameraPrivacy: [
    { action: 'openSettings', label: 'Open Windows settings' },
    { action: 'withoutCamera', label: 'Continue without camera' },
  ],
  captureBlocked: [
    { action: 'openSettings', label: 'Open Windows settings' },
    { action: 'audioOnly', label: 'Record audio only' },
  ],
  encoderUnavailable: [{ action: 'audioOnly', label: 'Record audio only' }],
};

/** §18.6: the inline card (camera in use, no encoder) and the permission card (privacy settings). */
export function VideoProblemCardView({ card, otherCameras, onAction }: { card: VideoProblemCard; otherCameras: boolean; onAction: (action: VideoCardAction) => void }): JSX.Element {
  const actions = CARD_ACTIONS[card.kind].filter((a) => a.action !== 'pickCamera' || otherCameras);
  return (
    <div class="rec-video-card" role="alert">
      <span class="rec-video-card-text">
        <span class="rec-video-card-lead">{card.lead}</span> <span class="rec-video-card-rest">{card.rest}</span>
      </span>
      <div class="rec-video-card-actions">
        {actions.map((a, i) => (
          <button
            key={a.action}
            class={i === 0 && card.kind !== 'encoderUnavailable' ? 'btn p' : 'btn g'}
            type="button"
            onClick={() => {
              onAction(a.action);
            }}
          >
            {a.label}
          </button>
        ))}
      </div>
    </div>
  );
}

function CornerPicker({ pip, onPip, disabled }: { pip: PipState; onPip: (pip: PipState) => void; disabled: boolean }): JSX.Element {
  return (
    <div
      class="rec-corners"
      role="group"
      aria-label="Inset corner"
      onKeyDown={(event) => {
        moveFocus(event, event.currentTarget, '.rec-corner', 'both');
      }}
    >
      {PIP_CORNERS.map(({ corner, name }) => (
        <button
          key={corner}
          class={pip.corner === corner ? 'rec-corner on' : 'rec-corner'}
          type="button"
          aria-label={name}
          aria-pressed={pip.corner === corner}
          disabled={disabled}
          onClick={() => {
            if (pip.corner !== corner) {
              onPip({ ...pip, corner: corner });
            }
          }}
        />
      ))}
    </div>
  );
}

export function VideoSources(props: VideoSourcesProps): JSX.Element {
  const { sources, selection, thumbs, notes, busy } = props;
  const list = sources ?? { displays: [], windows: [], cameras: [] };
  const display = findSource(list, selection?.displayId ?? null);
  const window = findSource(list, selection?.windowId ?? null);
  const camera = findSource(list, selection?.cameraId ?? null);
  const unavailable = !props.available || (sources !== null && list.displays.length === 0 && list.cameras.length === 0);
  const cardsFor = (role: 'screen' | 'camera'): JSX.Element[] =>
    props.cards
      .filter((c) => c.role === role)
      .map((card) => (
        <VideoProblemCardView
          key={card.kind}
          card={card}
          otherCameras={list.cameras.length > 1}
          onAction={(action) => {
            props.onCardAction(card, action);
          }}
        />
      ));

  const row = (
    id: VideoRow,
    name: string,
    on: boolean,
    sub: string,
    thumb: JSX.Element,
    selector: JSX.Element,
  ): JSX.Element => {
    const note = notes[id];
    return (
      <div key={id} class={on ? 'src rec-video-src' : 'src rec-video-src off'} data-row={id}>
        <div class="src-row">
          <Toggle
            label={name}
            checked={on}
            disabled={unavailable || busy.has(id) || selection === null}
            onChange={(next) => {
              props.onToggle(id, next);
            }}
          />
          <div class="src-text">
            <span class="src-name">{name}</span>
            {note === undefined ? <span class="src-sub">{sub}</span> : <span class={`src-sub rec-video-note rec-video-note--${note.tone}`}>{note.text}</span>}
          </div>
          {thumb}
        </div>
        <div class="rec-pick-row">{selector}</div>
      </div>
    );
  };

  const screenOn = selection?.screen === true;
  const windowOn = selection?.window === true;
  const cameraOn = selection?.camera === true;
  const pip = selection?.pip ?? { enabled: false, corner: 'bottomRight' };
  const pipUseful = (screenOn || windowOn) && cameraOn;

  return (
    <>
      <div class="rec-divider" />
      <div class="rec-card-head">
        <span class="lbl" id="rec-video-label">
          Video
        </span>
        {props.recordingVideo ? <span class="pill active">Recording video</span> : null}
      </div>
      <div class="rec-source-list rec-video-list" role="group" aria-labelledby="rec-video-label">
        {props.loadError !== null ? (
          <p class="rec-note rec-note--error" role="alert">
            {props.loadError}
          </p>
        ) : !props.available ? (
          <p class="rec-note">Screen and camera capture are not available in this copy of Memento. Audio records as usual.</p>
        ) : sources === null ? (
          <p class="rec-note">Looking for displays, windows and cameras…</p>
        ) : (
          <>
            {row(
              'screen',
              'Screen',
              screenOn,
              props.screenLine ?? 'Capture a whole display',
              <Thumb dataUrl={screenOn ? thumbs.screen.dataUrl : null} kind="win" />,
              <PickChip
                text={display?.name ?? 'Choose a display'}
                label={`Choose the display to capture: ${display?.name ?? 'none chosen'}`}
                disabled={list.displays.length === 0}
                onClick={() => {
                  props.onOpenPicker('screen');
                }}
              />,
            )}
            {row(
              'window',
              'Window',
              windowOn,
              window === null ? 'Capture one window instead of a whole display' : window.detail,
              <Thumb dataUrl={windowOn ? thumbs.screen.dataUrl : null} kind="win" />,
              <PickChip
                text={window?.name ?? 'Pick a window'}
                label={`Choose the window to capture: ${window?.name ?? 'none chosen'}`}
                disabled={list.windows.length === 0}
                onClick={() => {
                  props.onOpenPicker('window');
                }}
              />,
            )}
            {cardsFor('screen')}
            {row(
              'camera',
              'Camera',
              cameraOn,
              camera === null ? (list.cameras.length === 0 ? 'No camera was found. Connect one, then press Add.' : 'Choose a camera') : cameraLabel(camera),
              <Thumb dataUrl={cameraOn ? thumbs.camera.dataUrl : null} kind="cam" />,
              list.cameras.length === 0 ? (
                <PickChip text="No camera found" label="No camera found" disabled onClick={() => undefined} />
              ) : (
                <div class="rec-camera-select">
                  <SelectMenu
                    label="Camera"
                    variant="chip"
                    value={camera?.id ?? ''}
                    buttonText={camera?.name ?? 'Choose a camera'}
                    options={list.cameras.map((c: VideoSource) => ({ value: c.id, label: cameraLabel(c) }))}
                    disabled={busy.has('camera')}
                    onChange={(id) => {
                      props.onCamera(id);
                    }}
                  />
                </div>
              ),
            )}
            {cardsFor('camera')}
            <div class={pipUseful ? 'src rec-pip' : 'src rec-pip off'}>
              <div class="src-row">
                <Toggle
                  label="Screen plus camera"
                  checked={pip.enabled}
                  disabled={busy.has('pip') || selection === null}
                  onChange={(enabled) => {
                    props.onPip({ ...pip, enabled });
                  }}
                />
                <div class="src-text">
                  <span class="src-name">Screen + camera</span>
                  <span class="src-sub">Camera as a small inset on the screen recording</span>
                </div>
              </div>
              {pip.enabled ? (
                <div class="rec-inset-row">
                  <span class="rec-inset-label">Inset corner</span>
                  <CornerPicker pip={pip} onPip={props.onPip} disabled={busy.has('pip')} />
                  <span class="rec-inset-note">Both are also kept as separate tracks.</span>
                </div>
              ) : null}
            </div>
          </>
        )}
      </div>
    </>
  );
}
