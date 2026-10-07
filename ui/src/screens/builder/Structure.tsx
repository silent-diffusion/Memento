// The structure (DESIGN.md §10.2, §5.14, §5.17): rows of one to three module cards with drop zones
// between rows and a "beside" slot at the end of each row while something is dragged. Keyboard:
// Alt+Up and Alt+Down move a card like its buttons, Alt+Left and Alt+Right swap columns, Delete
// removes, Ctrl+Z undoes; the selected card expands with its settings.
import type { JSX } from 'preact';
import { useEffect, useRef } from 'preact/hooks';
import type { ModuleInfo, ModuleLength, ModuleSettings, TextSize } from '../../bridge/types';
import { Segmented, Toggle } from '../../components/Controls';
import { DragHandleIcon, PlusIcon } from '../../components/icons';
import { SmallCloseIcon, SmallDownIcon, SmallUpIcon } from '../../components/paper/icons';
import { moduleWords, rowWords } from '../../format/documents';
import { startDrag } from './Palette';
import { canDropBeside, moduleCount, type DragPayload, type Rows, type StructureAction } from './structureState';

const LENGTHS: readonly { value: ModuleLength; label: string }[] = [
  { value: 'short', label: 'Short' },
  { value: 'medium', label: 'Medium' },
  { value: 'long', label: 'Long' },
];

const TEXT_SIZES: readonly { value: TextSize; label: string }[] = [
  { value: 'smaller', label: 'Smaller' },
  { value: 'normal', label: 'Normal' },
  { value: 'larger', label: 'Larger' },
];

export function moduleName(module: ModuleSettings, catalog: ReadonlyMap<string, ModuleInfo>): string {
  return module.customTitle ?? catalog.get(module.module)?.name ?? module.module;
}

/** The one-line preview under a card's name. */
function instructionPreview(module: ModuleSettings, info: ModuleInfo | undefined): string {
  if (module.module === 'customText') {
    return module.customText === null || module.customText.trim() === '' ? 'Your own text, placed as written' : module.customText;
  }
  if (info?.generated === false) {
    return info.description;
  }
  return module.instructions.trim() === '' ? (info?.description ?? '') : module.instructions;
}

interface StructureProps {
  rows: Rows;
  selectedId: string | null;
  catalog: ReadonlyMap<string, ModuleInfo>;
  drag: DragPayload | null;
  over: string | null;
  dispatch: (action: StructureAction) => void;
  onDragStart: (payload: DragPayload) => void;
  onDragEnd: () => void;
  onOver: (key: string | null) => void;
  onAddModule: () => void;
  /** A move or removal, in words for screen readers. */
  announce: (message: string) => void;
}

export function Structure(props: StructureProps): JSX.Element {
  const { rows, selectedId, catalog, drag, over, dispatch } = props;
  const root = useRef<HTMLElement | null>(null);
  const count = moduleCount(rows);
  // A moved card is drawn again in its new row: focus goes back to the control that moved it.
  const pendingFocus = useRef<{ id: string | null; control: string } | null>(null);

  useEffect(() => {
    const pending = pendingFocus.current;
    if (pending === null) {
      return;
    }
    pendingFocus.current = null;
    const card = pending.id === null ? null : root.current?.querySelector<HTMLElement>(`[data-card="${pending.id}"]`);
    const target = card?.querySelector<HTMLElement>(pending.control) ?? root.current?.querySelector<HTMLElement>('.add-module');
    target?.focus();
  }, [rows]);

  const refocus = (id: string | null, control: string): void => {
    pendingFocus.current = { id, control };
  };

  const dragging = drag !== null;
  const draggedId = drag?.kind === 'card' ? drag.id : null;

  const dropHandlers = (key: string, action: StructureAction | null): { onDragOver: (event: DragEvent) => void; onDragLeave: () => void; onDrop: (event: DragEvent) => void } => ({
    onDragOver: (event) => {
      if (action === null) {
        return;
      }
      event.preventDefault();
      if (over !== key) {
        props.onOver(key);
      }
    },
    onDragLeave: () => {
      if (over === key) {
        props.onOver(null);
      }
    },
    onDrop: (event) => {
      event.preventDefault();
      if (action !== null) {
        dispatch(action);
      }
      props.onDragEnd();
    },
  });

  const gap = (index: number, last: boolean): JSX.Element => {
    const key = last ? 'last' : `b${index}`;
    return (
      <div
        class={['dz', dragging ? 'live' : '', over === key ? 'hot' : ''].filter((c) => c !== '').join(' ')}
        aria-hidden="true"
        data-gap={index}
        {...dropHandlers(key, drag === null ? null : { type: 'dropRow', index, payload: drag })}
      >
        {dragging ? (last ? 'Drop here to add at the end' : 'Drop here for a new row') : ''}
      </div>
    );
  };

  return (
    <section
      ref={root}
      class="structure"
      aria-label="Document structure"
      onKeyDown={(event) => {
        const target = event.target as HTMLElement;
        if (target.closest('textarea, input') !== null) {
          return;
        }
        if ((event.ctrlKey || event.metaKey) && !event.shiftKey && event.key.toLowerCase() === 'z') {
          event.preventDefault();
          dispatch({ type: 'undo' });
          props.announce('Undone.');
        }
      }}
    >
      <div class="structure-head">
        <span class="lbl" id="structure-label">
          Structure · {moduleWords(count)} in {rowWords(rows.length)}
        </span>
        <span class="structure-hint">Top to bottom, left to right is the order in the document</span>
      </div>

      {rows.length === 0 ? (
        <div class="structure-empty">
          <span class="structure-empty-title">No modules yet</span>
          <span class="structure-empty-text">Click a module in the palette, or drag one here.</span>
        </div>
      ) : null}

      {rows.map((row, rowIndex) => {
        const sideKey = `s${rowIndex}`;
        const sideOk = canDropBeside(rows, rowIndex, drag);
        return (
          <div key={row.map((m) => m.id).join('|')} class="structure-row" data-row={rowIndex}>
            {gap(rowIndex, false)}
            <div class="row-line">
              <div class="row-grid" style={{ gridTemplateColumns: `repeat(${row.length}, minmax(0, 1fr))` }}>
                {row.map((module) => {
                  const info = catalog.get(module.module);
                  const n = rows.flat().indexOf(module) + 1;
                  return (
                    <ModuleCard
                      key={module.id}
                      module={module}
                      info={info}
                      index={n}
                      name={moduleName(module, catalog)}
                      shared={row.length > 1}
                      selected={selectedId === module.id}
                      ghosted={draggedId === module.id}
                      dispatch={dispatch}
                      announce={props.announce}
                      refocus={refocus}
                      nextId={nextAfter(rows, module.id)}
                      onDragStart={props.onDragStart}
                      onDragEnd={props.onDragEnd}
                    />
                  );
                })}
              </div>
              <div
                class={['side', dragging && sideOk ? 'live' : '', over === sideKey && sideOk ? 'hot' : ''].filter((c) => c !== '').join(' ')}
                aria-hidden="true"
                data-side={rowIndex}
                {...dropHandlers(sideKey, drag === null || !sideOk ? null : { type: 'dropBeside', rowIndex, payload: drag })}
              >
                <PlusIcon size={14} />
              </div>
            </div>
          </div>
        );
      })}
      {gap(rows.length, true)}

      <button class="btn ghost add-module" type="button" onClick={props.onAddModule}>
        + Add a module
      </button>
    </section>
  );
}

/** The card that takes focus when this one is removed: the next, else the previous, else none. */
function nextAfter(rows: Rows, id: string): string | null {
  const all = rows.flat();
  const index = all.findIndex((m) => m.id === id);
  return all[index + 1]?.id ?? all[index - 1]?.id ?? null;
}

interface ModuleCardProps {
  module: ModuleSettings;
  info: ModuleInfo | undefined;
  index: number;
  name: string;
  shared: boolean;
  selected: boolean;
  ghosted: boolean;
  dispatch: (action: StructureAction) => void;
  announce: (message: string) => void;
  refocus: (id: string | null, control: string) => void;
  nextId: string | null;
  onDragStart: (payload: DragPayload) => void;
  onDragEnd: () => void;
}

function ModuleCard({ module, info, index, name, shared, selected, ghosted, dispatch, announce, refocus, nextId, onDragStart, onDragEnd }: ModuleCardProps): JSX.Element {
  const id = module.id;
  const update = (patch: Partial<Omit<ModuleSettings, 'id' | 'module'>>): void => {
    dispatch({ type: 'update', id, patch });
  };
  const move = (type: 'up' | 'down' | 'left' | 'right', control: string): void => {
    refocus(id, control);
    dispatch({ type, id });
  };
  const remove = (): void => {
    refocus(nextId, '.modhead');
    dispatch({ type: 'remove', id });
    announce(`${name} removed. Press Ctrl+Z to put it back.`);
  };
  const generated = info?.generated ?? true;
  const linkable = info === undefined || !['chips', 'text', 'transcript'].includes(info.shape);
  const number = String(index).padStart(2, '0');
  return (
    <div class={['mod', selected ? 'on' : '', ghosted ? 'ghosted' : '', shared ? 'mod--shared' : ''].filter((c) => c !== '').join(' ')} data-card={id}>
      <div class="mod-head">
        <span
          class="handle"
          draggable
          aria-hidden="true"
          title="Drag to move"
          onDragStart={(event) => {
            startDrag(event, `mod:${id}`, 'move');
            onDragStart({ kind: 'card', id });
          }}
          onDragEnd={onDragEnd}
        >
          <DragHandleIcon size={16} />
        </span>
        <button
          class="modhead"
          type="button"
          aria-expanded={selected}
          aria-label={`${number} ${name}. ${selected ? 'Selected' : 'Select to change its settings'}. Alt and the arrow keys move it.`}
          onClick={() => {
            dispatch({ type: 'select', id: selected ? null : id });
          }}
          onKeyDown={(event) => {
            if (event.altKey && ['ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight'].includes(event.key)) {
              event.preventDefault();
              move(event.key === 'ArrowUp' ? 'up' : event.key === 'ArrowDown' ? 'down' : event.key === 'ArrowLeft' ? 'left' : 'right', '.modhead');
              announce(`${name} moved.`);
            } else if (event.key === 'Delete') {
              event.preventDefault();
              remove();
            }
          }}
        >
          <span class="mono mod-n">{number}</span>
          <span class="mod-text">
            <span class="mod-name">{name}</span>
            <span class="mod-instr">{instructionPreview(module, info)}</span>
          </span>
        </button>
        <button class="icon-btn mod-icon" type="button" aria-label={shared ? `Move ${name} into its own row above` : `Move ${name} up`} data-move="up" onClick={() => { move('up', '[data-move=up]'); }}>
          <SmallUpIcon size={13} />
        </button>
        <button class="icon-btn mod-icon" type="button" aria-label={shared ? `Move ${name} into its own row below` : `Move ${name} down`} data-move="down" onClick={() => { move('down', '[data-move=down]'); }}>
          <SmallDownIcon size={13} />
        </button>
        <button class="icon-btn mod-icon" type="button" aria-label={`Remove ${name}`} onClick={remove}>
          <SmallCloseIcon size={13} />
        </button>
      </div>
      {selected ? (
        <div class="mod-body">
          <label class="mod-field">
            <span class="lbl">Heading</span>
            <input
              class="field mod-input"
              type="text"
              value={name}
              maxLength={80}
              onInput={(event) => {
                const value = event.currentTarget.value;
                update({ customTitle: value.trim() === '' || value === info?.name ? null : value });
              }}
            />
          </label>
          {module.module === 'customText' ? (
            <label class="mod-field">
              <span class="lbl">Your text</span>
              <textarea
                class="field mod-textarea"
                rows={4}
                value={module.customText ?? ''}
                onInput={(event) => {
                  update({ customText: event.currentTarget.value });
                }}
              />
            </label>
          ) : generated ? (
            <label class="mod-field">
              <span class="lbl">Instructions for this section</span>
              <textarea
                class="field mod-textarea"
                rows={3}
                value={module.instructions}
                onInput={(event) => {
                  update({ instructions: event.currentTarget.value });
                }}
              />
            </label>
          ) : (
            <p class="mod-note">{info?.groundingRule ?? 'Placed from the recording; no AI is involved.'}</p>
          )}
          <div class="mod-options">
            {generated && module.module !== 'customText' ? (
              <div class="mod-option">
                <span class="mod-option-label">Length</span>
                <Segmented<ModuleLength> label={`Length of ${name}`} value={module.length} options={LENGTHS} onChange={(length) => { update({ length }); }} />
              </div>
            ) : null}
            <div class="mod-option">
              <span class="mod-option-label">Text size</span>
              <Segmented<TextSize> label={`Text size of ${name}`} value={module.textSize} options={TEXT_SIZES} onChange={(textSize) => { update({ textSize }); }} />
            </div>
            {linkable ? (
              <div class="mod-option">
                <Toggle label="Link each point to the transcript" checked={module.linkToTranscript} onChange={(linkToTranscript) => { update({ linkToTranscript }); }} />
                <span class="mod-option-text">Link to transcript</span>
              </div>
            ) : null}
          </div>
        </div>
      ) : null}
    </div>
  );
}
