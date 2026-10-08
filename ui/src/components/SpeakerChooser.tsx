// A popover for choosing a speaker (DESIGN.md §9): the transcript line's speaker menu and the People
// list's merge menu. A "Find or add a speaker" field at the top filters as you type (case and accents
// ignored, names that start with the text first); the list under it scrolls inside the room the
// window has beside the trigger; the arrow keys move through it, Enter picks, Esc closes. When the
// typed name matches nobody, a last row adds it as a new speaker.
import type { JSX } from 'preact';
import { useEffect, useLayoutEffect, useMemo, useRef, useState } from 'preact/hooks';
import type { Speaker } from '../bridge/types';
import { filterByName, hasExactName } from '../format/speakerSearch';
import { speakerColourVar } from '../format/transcript';
import './editing.css';
import { PopoverLayer, useFloatingPopovers } from './Floating';
import { CheckIcon, PlusIcon } from './icons';

/** The speaker menu never grows past this; a longer list scrolls. */
export const SPEAKER_MENU_MAX_HEIGHT = 420;

export interface ChooserAction {
  id: string;
  label: string;
  run: () => void;
}

export interface SpeakerChooserProps {
  /** The trigger: the popover is placed beside it and focus returns to it. */
  anchorRef: { current: HTMLElement | null };
  /** Clicks inside this element (the trigger's wrapper) do not close the popover. */
  rootRef: { current: HTMLElement | null };
  /** Accessible name of the popover: "Speaker for the line at 1:33". */
  label: string;
  /** The heading over the list: "This line is said by". */
  heading: string;
  speakers: readonly Speaker[];
  /** Marked with a check. */
  currentId: string | null;
  onPick: (speaker: Speaker) => void;
  /** Offer `Add "{name}" as a new speaker` when the typed name matches nobody. */
  onAdd?: (name: string) => void;
  /** Rows after the speakers while nothing is typed ("Rename Sam Okafor…"). */
  actions?: readonly ChooserAction[];
  /** `focusTrigger`: Esc and a pick return focus to the trigger; a click elsewhere does not. */
  onClose: (focusTrigger: boolean) => void;
  align?: 'start' | 'end';
  gap?: number;
  /** Shown when nothing matches and nothing can be added. */
  emptyText?: string;
}

type Row = { kind: 'speaker'; speaker: Speaker } | { kind: 'add'; name: string } | { kind: 'action'; action: ChooserAction };

let nextId = 0;

export function SpeakerChooser({
  anchorRef,
  rootRef,
  label,
  heading,
  speakers,
  currentId,
  onPick,
  onAdd,
  actions = [],
  onClose,
  align = 'start',
  gap = 6,
  emptyText = 'No speaker has that name.',
}: SpeakerChooserProps): JSX.Element {
  const ids = useMemo(() => {
    nextId += 1;
    return { list: `speaker-list-${nextId}`, heading: `speaker-heading-${nextId}`, option: `speaker-option-${nextId}` };
  }, []);
  const [query, setQuery] = useState('');
  const pop = useRef<HTMLDivElement | null>(null);
  const input = useRef<HTMLInputElement | null>(null);
  const list = useRef<HTMLUListElement | null>(null);
  const floating = useFloatingPopovers();
  const closeRef = useRef(onClose);
  closeRef.current = onClose;

  const typed = query.trim().replace(/\s+/g, ' ');
  const rows = useMemo((): Row[] => {
    const matches = filterByName(speakers, typed, (s) => s.name).map((speaker): Row => ({ kind: 'speaker', speaker }));
    const add: Row[] = onAdd !== undefined && typed !== '' && !hasExactName(speakers, typed, (s) => s.name) ? [{ kind: 'add', name: typed.slice(0, 100) }] : [];
    const extra: Row[] = typed === '' ? actions.map((action): Row => ({ kind: 'action', action })) : [];
    return [...matches, ...add, ...extra];
  }, [speakers, typed, onAdd !== undefined, actions]);

  const initial = typed === '' ? Math.max(0, rows.findIndex((r) => r.kind === 'speaker' && r.speaker.id === currentId)) : 0;
  const [active, setActive] = useState(initial);
  useEffect(() => {
    setActive(initial);
  }, [typed]);
  const activeIndex = Math.min(active, rows.length - 1);

  // The field takes focus as the popover opens, whether by keyboard or by click, so typing filters at once.
  useEffect(() => {
    input.current?.focus({ preventScroll: true });
    const away = (event: PointerEvent): void => {
      if (event.target instanceof Node && rootRef.current?.contains(event.target) !== true && pop.current?.contains(event.target) !== true) {
        closeRef.current(false);
      }
    };
    document.addEventListener('pointerdown', away);
    return () => {
      document.removeEventListener('pointerdown', away);
    };
  }, []);

  // Keep the active row in view inside the scrolling list.
  useLayoutEffect(() => {
    const el = list.current?.querySelector<HTMLElement>(`#${ids.option}-${activeIndex}`);
    if (el !== null && el !== undefined && typeof el.scrollIntoView === 'function') {
      el.scrollIntoView({ block: 'nearest' });
    }
  }, [activeIndex, rows.length]);

  const choose = (row: Row | undefined): void => {
    if (row === undefined) {
      return;
    }
    if (row.kind === 'speaker') {
      closeRef.current(true);
      onPick(row.speaker);
    } else if (row.kind === 'add') {
      closeRef.current(true);
      onAdd?.(row.name);
    } else {
      row.action.run();
    }
  };

  const optionText = (row: Row): JSX.Element => {
    if (row.kind === 'speaker') {
      const current = row.speaker.id === currentId;
      return (
        <>
          <span class="menu-check" aria-hidden="true">
            {current ? <CheckIcon size={12} /> : null}
          </span>
          <span class="segm-dot" aria-hidden="true" style={{ background: speakerColourVar(row.speaker.color) }} />
          <span class="speaker-option-name">{row.speaker.name}</span>
          {current ? <span class="sr"> (current)</span> : null}
        </>
      );
    }
    if (row.kind === 'add') {
      return (
        <>
          <span class="menu-check" aria-hidden="true">
            <PlusIcon size={12} />
          </span>
          <span class="speaker-option-name">
            Add “{row.name}” as a new speaker
          </span>
        </>
      );
    }
    return (
      <>
        <span class="menu-check" aria-hidden="true" />
        <span class="speaker-option-name">{row.action.label}</span>
      </>
    );
  };

  const firstAction = rows.findIndex((r) => r.kind !== 'speaker');
  return (
    <PopoverLayer anchorRef={anchorRef} popRef={pop} align={align} gap={gap} capHeight={SPEAKER_MENU_MAX_HEIGHT}>
      <div
        ref={pop}
        class="popover speaker-menu"
        role="group"
        aria-label={label}
        onClick={(event) => {
          event.stopPropagation();
        }}
        onDblClick={(event) => {
          event.stopPropagation();
        }}
        onKeyDown={(event) => {
          event.stopPropagation();
        }}
      >
        <input
          ref={input}
          class="field speaker-menu-search"
          type="text"
          role="combobox"
          aria-label="Find or add a speaker"
          aria-expanded="true"
          aria-controls={ids.list}
          aria-autocomplete="list"
          aria-activedescendant={rows.length === 0 ? undefined : `${ids.option}-${activeIndex}`}
          autocomplete="off"
          spellcheck={false}
          placeholder="Find or add a speaker"
          maxLength={100}
          value={query}
          onInput={(event) => {
            setQuery(event.currentTarget.value);
          }}
          onKeyDown={(event) => {
            if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
              event.preventDefault();
              if (rows.length > 0) {
                const step = event.key === 'ArrowDown' ? 1 : -1;
                setActive((activeIndex + step + rows.length) % rows.length);
              }
            } else if (event.key === 'PageDown' || event.key === 'PageUp') {
              event.preventDefault();
              if (rows.length > 0) {
                setActive(Math.max(0, Math.min(rows.length - 1, activeIndex + (event.key === 'PageDown' ? 8 : -8))));
              }
            } else if (event.key === 'Enter') {
              event.preventDefault();
              choose(rows[activeIndex]);
            } else if (event.key === 'Escape') {
              event.preventDefault();
              closeRef.current(true);
            } else if (event.key === 'Tab') {
              // Floating on <body>: back to the trigger first, so Tab moves on from there.
              closeRef.current(floating);
            }
          }}
        />
        <span class="menu-label" id={ids.heading}>
          {heading}
        </span>
        <ul ref={list} id={ids.list} class="speaker-menu-list" role="listbox" aria-labelledby={ids.heading}>
          {rows.map((row, index) => (
            <li
              key={row.kind === 'speaker' ? row.speaker.id : row.kind === 'add' ? 'add' : row.action.id}
              id={`${ids.option}-${index}`}
              class={[
                'item menu-item speaker-option',
                index === activeIndex ? 'speaker-option--active' : '',
                row.kind !== 'speaker' && index === firstAction ? 'speaker-option--after' : '',
                row.kind === 'add' ? 'speaker-option--add' : '',
              ]
                .filter((c) => c !== '')
                .join(' ')}
              role="option"
              aria-selected={row.kind === 'speaker' && row.speaker.id === currentId}
              data-speaker-id={row.kind === 'speaker' ? row.speaker.id : undefined}
              onPointerDown={(event) => {
                // Keep focus in the field while clicking a row.
                event.preventDefault();
              }}
              onPointerMove={() => {
                if (index !== activeIndex) {
                  setActive(index);
                }
              }}
              onClick={() => {
                choose(row);
              }}
            >
              {optionText(row)}
            </li>
          ))}
        </ul>
        {rows.length === 0 ? <p class="speaker-menu-empty">{emptyText}</p> : null}
      </div>
    </PopoverLayer>
  );
}
