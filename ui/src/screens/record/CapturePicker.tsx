// The display and window picker (DESIGN.md §18.2), transcribed from renders/DisplayPicker.dc.html: a
// 600 px side sheet over the Recording session with "Entire screen | This window", a two-column grid
// of tiles with a still of each (video.thumbnail), Memento's own window and windows that keep
// themselves out of captures listed and explained but not selectable, the note on what is captured,
// and "Use {name}". Arrow keys move between tiles (two columns), Enter or Space chooses one, and
// windows can be searched by title or app.
import type { JSX } from 'preact';
import { useMemo, useRef, useState } from 'preact/hooks';
import type { BridgeClient } from '../../bridge/client';
import type { VideoListSourcesResult, VideoSource } from '../../bridge/types';
import { InfoIcon, SearchIcon } from '../../components/icons';
import { SideSheet } from '../../components/Overlay';
import { useThumbnail } from './useThumbnail';

export type PickerMode = 'screen' | 'window';

const NOTES: Record<PickerMode, string> = {
  screen:
    'Captures everything shown on the chosen display, including other windows and notifications that appear on it. Your other displays are not captured. The mouse pointer is included.',
  window: 'Captures only this window, even when other windows cover it. If it is minimised, the video pauses and resumes when it comes back; audio keeps recording.',
};

const NOTE_WITHOUT_POINTER = 'Captures everything shown on the chosen display, including other windows and notifications that appear on it. Your other displays are not captured. The mouse pointer is not included.';

interface CapturePickerProps {
  bridge: BridgeClient;
  sources: VideoListSourcesResult;
  initialMode: PickerMode;
  displayId: string | null;
  windowId: string | null;
  /** Settings › Recording › Include the mouse pointer. */
  includePointer: boolean;
  onUse: (source: VideoSource) => void;
  onClose: () => void;
}

function Tile({
  bridge,
  source,
  chosen,
  onChoose,
  onUse,
  index,
}: {
  bridge: BridgeClient;
  source: VideoSource;
  chosen: boolean;
  onChoose: () => void;
  onUse: () => void;
  index: number;
}): JSX.Element {
  const { dataUrl } = useThumbnail(bridge, source.excluded ? null : source.id, 270, null);
  const shot = source.excluded ? 'rec-shot rec-shot--excluded' : source.kind === 'window' ? 'rec-shot rec-shot--app' : 'rec-shot';
  const subId = `pick-sub-${index}`;
  return (
    <button
      class={chosen ? 'rec-tile on' : 'rec-tile'}
      type="button"
      aria-pressed={chosen}
      aria-disabled={source.excluded ? 'true' : undefined}
      aria-describedby={subId}
      data-tile={index}
      onClick={() => {
        if (!source.excluded) {
          onChoose();
        }
      }}
      onDblClick={() => {
        if (!source.excluded) {
          onUse();
        }
      }}
    >
      <div class={shot} aria-hidden="true">
        {dataUrl === null ? (
          <>
            <div class="rec-shot-win" />
            <div class="rec-shot-bar" />
          </>
        ) : (
          <img class="rec-shot-img" src={dataUrl} alt="" />
        )}
      </div>
      <span class="rec-tile-text">
        <span class="rec-tile-name">{source.name}</span>
        <span class="rec-tile-sub" id={subId}>
          {source.detail}
        </span>
      </span>
    </button>
  );
}

export function CapturePicker(props: CapturePickerProps): JSX.Element {
  const { sources } = props;
  const [mode, setMode] = useState<PickerMode>(props.initialMode);
  const [query, setQuery] = useState('');
  const firstUsable = (list: readonly VideoSource[]): string | null => list.find((s) => !s.excluded)?.id ?? null;
  const [chosen, setChosen] = useState<Record<PickerMode, string | null>>({
    screen: props.displayId ?? firstUsable(sources.displays.filter((d) => d.primary)) ?? firstUsable(sources.displays),
    window: props.windowId ?? firstUsable(sources.windows),
  });
  const grid = useRef<HTMLDivElement | null>(null);

  const list = useMemo(() => {
    if (mode === 'screen') {
      return sources.displays;
    }
    const needle = query.trim().toLocaleLowerCase();
    return needle === '' ? sources.windows : sources.windows.filter((w) => `${w.name} ${w.detail}`.toLocaleLowerCase().includes(needle));
  }, [mode, query, sources]);

  const pickedId = chosen[mode];
  const picked = [...sources.displays, ...sources.windows].find((s) => s.id === pickedId && !s.excluded) ?? null;

  const use = (source: VideoSource | null): void => {
    if (source !== null && !source.excluded) {
      props.onUse(source);
    }
  };

  // Two columns: Left and Right move by one, Up and Down by a row; Home and End to the ends.
  const onGridKey = (event: KeyboardEvent): void => {
    const tiles = [...(grid.current?.querySelectorAll<HTMLButtonElement>('.rec-tile') ?? [])];
    const current = tiles.findIndex((t) => t === document.activeElement);
    if (current < 0) {
      return;
    }
    const step: Record<string, number> = { ArrowLeft: -1, ArrowRight: 1, ArrowUp: -2, ArrowDown: 2 };
    let next: number | null = null;
    if (event.key in step) {
      next = Math.min(tiles.length - 1, Math.max(0, current + (step[event.key] ?? 0)));
    } else if (event.key === 'Home') {
      next = 0;
    } else if (event.key === 'End') {
      next = tiles.length - 1;
    }
    if (next !== null) {
      event.preventDefault();
      tiles[next]?.focus();
    }
  };

  const tabs: { mode: PickerMode; label: string }[] = [
    { mode: 'screen', label: 'Entire screen' },
    { mode: 'window', label: 'This window' },
  ];

  return (
    <SideSheet
      titleId="pick-title"
      title="Choose what to capture"
      wide
      onClose={props.onClose}
      footer={
        <>
          <span>You can change this while recording; the track continues.</span>
          <span class="rec-pick-actions">
            <button class="btn g" type="button" onClick={props.onClose}>
              Cancel
            </button>
            <button
              class="btn p"
              type="button"
              disabled={picked === null}
              onClick={() => {
                use(picked);
              }}
            >
              {picked === null ? (mode === 'screen' ? 'Choose a display' : 'Choose a window') : `Use ${picked.name}`}
            </button>
          </span>
        </>
      }
    >
      <div class="rec-pick-body">
        <div
          class="rec-pick-tabs"
          role="tablist"
          aria-label="What to capture"
          onKeyDown={(event) => {
            const tabsEls = [...event.currentTarget.querySelectorAll<HTMLButtonElement>('[role="tab"]')];
            const index = tabsEls.findIndex((t) => t === document.activeElement);
            if (index >= 0 && (event.key === 'ArrowLeft' || event.key === 'ArrowRight')) {
              event.preventDefault();
              const next = tabsEls[(index + 1) % tabsEls.length];
              next?.focus();
              next?.click();
            }
          }}
        >
          {tabs.map((tab) => (
            <button
              key={tab.mode}
              class={tab.mode === mode ? 'seg on' : 'seg'}
              type="button"
              role="tab"
              id={`pick-tab-${tab.mode}`}
              aria-selected={tab.mode === mode}
              aria-controls="pick-panel"
              tabIndex={tab.mode === mode ? 0 : -1}
              onClick={() => {
                setMode(tab.mode);
              }}
            >
              {tab.label}
            </button>
          ))}
        </div>
        <div id="pick-panel" role="tabpanel" aria-labelledby={`pick-tab-${mode}`} class="rec-pick-panel">
          {mode === 'window' ? (
            <label class="rec-pick-search">
              <SearchIcon size={16} />
              <span class="sr">Search windows</span>
              <input
                class="field rec-pick-search-input"
                type="search"
                placeholder="Search windows by title or app"
                value={query}
                onInput={(event) => {
                  setQuery(event.currentTarget.value);
                }}
              />
            </label>
          ) : null}
          {list.length === 0 ? (
            <p class="rec-pick-empty">
              {mode === 'screen'
                ? 'No display was found. Connect one, or check that it is turned on; it appears here by itself.'
                : query.trim() === ''
                  ? 'No window is open that can be captured. Open the window you want, then choose it here; it appears by itself.'
                  : `No open window matches "${query.trim()}".`}
            </p>
          ) : (
            <div ref={grid} class="rec-tiles" onKeyDown={onGridKey}>
              {list.map((source, index) => (
                <Tile
                  key={source.id}
                  bridge={props.bridge}
                  source={source}
                  index={index}
                  chosen={source.id === pickedId}
                  onChoose={() => {
                    setChosen((before) => ({ ...before, [mode]: source.id }));
                  }}
                  onUse={() => {
                    use(source);
                  }}
                />
              ))}
            </div>
          )}
          <div class="rec-pick-note">
            <InfoIcon size={16} class="rec-pick-note-icon" />
            <span>{mode === 'screen' && !props.includePointer ? NOTE_WITHOUT_POINTER : NOTES[mode]}</span>
          </div>
        </div>
      </div>
    </SideSheet>
  );
}
