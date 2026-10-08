// The modules palette (DESIGN.md §10.1, §5.15): a search field, then Structure, Detail and Custom.
// Click adds the module as a new row at the end; drag places it.
import type { JSX } from 'preact';
import { useState } from 'preact/hooks';
import type { ModuleGroup, ModuleInfo } from '../../bridge/types';
import { DragHandleIcon, PlusIcon, SearchIcon } from '../../components/icons';
import { moveFocus } from '../../components/keyboard';
import type { DragPayload } from './structureState';

export const PALETTE_GROUPS: readonly { id: ModuleGroup; label: string }[] = [
  { id: 'structure', label: 'Structure' },
  { id: 'detail', label: 'Detail' },
  { id: 'custom', label: 'Custom' },
];

export interface PaletteGroup {
  id: ModuleGroup;
  label: string;
  items: ModuleInfo[];
}

/** The groups and the modules in them that match every word of the query (name or description). */
export function paletteGroups(modules: readonly ModuleInfo[], query: string): PaletteGroup[] {
  const words = query.toLocaleLowerCase().split(/\s+/).filter((w) => w !== '');
  const matches = (m: ModuleInfo): boolean => {
    const text = `${m.name} ${m.description}`.toLocaleLowerCase();
    return words.every((w) => text.includes(w));
  };
  return PALETTE_GROUPS.map((g) => ({ ...g, items: modules.filter((m) => m.group === g.id && matches(m)) })).filter((g) => g.items.length > 0);
}

/** Starts a drag with something in the DataTransfer (WebView2 and other engines need it). */
export function startDrag(event: DragEvent, text: string, effect: 'copy' | 'move'): void {
  try {
    event.dataTransfer?.setData('text/plain', text);
    if (event.dataTransfer !== null) {
      event.dataTransfer.effectAllowed = effect;
    }
  } catch {
    // Some test environments have no DataTransfer; the drag still works from state.
  }
}

interface PaletteProps {
  modules: readonly ModuleInfo[];
  /** Modules already on the template: shown greyed with "in use", still addable (a second copy). */
  inUse: ReadonlySet<ModuleInfo['id']>;
  onAdd: (module: ModuleInfo) => void;
  onDragStart: (payload: DragPayload) => void;
  onDragEnd: () => void;
}

/** The palette item's accessible name: "Add Decisions", or "Add Decisions, in use" once it is placed. */
export function paletteItemLabel(module: Pick<ModuleInfo, 'name'>, inUse: boolean): string {
  return inUse ? `Add ${module.name}, in use` : `Add ${module.name}`;
}

export function Palette({ modules, inUse, onAdd, onDragStart, onDragEnd }: PaletteProps): JSX.Element {
  const [query, setQuery] = useState('');
  const groups = paletteGroups(modules, query);
  return (
    <section class="palette" aria-label="Modules">
      <div class="palette-search">
        <label class="sr" for="mod-search">
          Find a module
        </label>
        <SearchIcon size={16} class="palette-search-icon" />
        <input
          id="mod-search"
          class="field palette-field"
          type="search"
          placeholder="Find a module"
          autocomplete="off"
          value={query}
          onInput={(event) => {
            setQuery(event.currentTarget.value);
          }}
          onKeyDown={(event) => {
            if (event.key === 'ArrowDown') {
              event.preventDefault();
              event.currentTarget.closest('.palette')?.querySelector<HTMLElement>('.pal')?.focus();
            } else if (event.key === 'Escape' && query !== '') {
              event.preventDefault();
              setQuery('');
            }
          }}
        />
      </div>
      <div
        class="palette-groups"
        onKeyDown={(event) => {
          moveFocus(event, event.currentTarget, '.pal', 'vertical');
        }}
      >
        {groups.length === 0 ? (
          <p class="palette-empty" role="status">
            No module matches “{query.trim()}”.
          </p>
        ) : (
          groups.map((group) => (
            <div key={group.id} class="palette-group" role="group" aria-labelledby={`pal-${group.id}`}>
              <span id={`pal-${group.id}`} class="lbl palette-label">
                {group.label}
              </span>
              {group.items.map((m) => (
                <button
                  key={m.id}
                  class={inUse.has(m.id) ? 'pal pal--in-use' : 'pal'}
                  type="button"
                  draggable
                  aria-label={paletteItemLabel(m, inUse.has(m.id))}
                  title={inUse.has(m.id) ? `${m.description} Already in this template; adding it again places a second copy.` : m.description}
                  data-module={m.id}
                  onClick={() => {
                    onAdd(m);
                  }}
                  onDragStart={(event) => {
                    startDrag(event, `new:${m.id}`, 'copy');
                    onDragStart({ kind: 'palette', module: m });
                  }}
                  onDragEnd={onDragEnd}
                >
                  <DragHandleIcon size={14} class="pal-handle" />
                  <span class="pal-name">{m.name}</span>
                  {inUse.has(m.id) ? (
                    <span class="pal-in-use" aria-hidden="true">
                      in use
                    </span>
                  ) : null}
                  <PlusIcon size={14} class="pal-plus" />
                </button>
              ))}
            </div>
          ))
        )}
      </div>
      <p class="palette-hint">Drag onto the structure. Drop beside a module to put them side by side. Click to add at the end.</p>
    </section>
  );
}
