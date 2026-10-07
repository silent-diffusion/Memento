// The Builder's structure (DESIGN.md §10, §5.17 and renders/Builder.dc.html): a list of rows, each
// holding one to three modules side by side. Every change that moves, adds or removes a module can
// be undone with Ctrl+Z; editing a module's settings does not touch the undo history (the text
// fields keep their own).
import type { ContentShape, ModuleInfo, ModuleSettings, Template } from '../../bridge/types';

/** At most three modules share a row. */
export const MAX_PER_ROW = 3;

const HISTORY_LIMIT = 50;

export type Rows = ModuleSettings[][];

/** What is being dragged: a palette entry (a new module) or a card by its handle. */
export type DragPayload = { kind: 'palette'; module: ModuleInfo } | { kind: 'card'; id: string };

export interface StructureState {
  rows: Rows;
  selectedId: string | null;
  /** Earlier row layouts, newest last. */
  history: Rows[];
}

export type StructureAction =
  | { type: 'load'; rows: Rows; selectedId?: string | null }
  /** Palette click or "+ Add a module": a new row at the end. */
  | { type: 'append'; module: ModuleInfo }
  /** A drop in the gap before row `index` (rows.length: after the last row). */
  | { type: 'dropRow'; index: number; payload: DragPayload }
  /** A drop in the slot at the right end of row `rowIndex`. */
  | { type: 'dropBeside'; rowIndex: number; payload: DragPayload }
  /** The card's up and down buttons (and Alt+Up, Alt+Down). */
  | { type: 'up'; id: string }
  | { type: 'down'; id: string }
  /** Alt+Left, Alt+Right: the column order inside a shared row. */
  | { type: 'left'; id: string }
  | { type: 'right'; id: string }
  | { type: 'remove'; id: string }
  | { type: 'update'; id: string; patch: Partial<Omit<ModuleSettings, 'id' | 'module'>> }
  | { type: 'select'; id: string | null }
  | { type: 'undo' };

export function rowsOf(template: Template): Rows {
  return template.rows.map((r) => r.modules.map((m) => ({ ...m }))).filter((r) => r.length > 0);
}

export function initialStructure(rows: Rows, selectedId: string | null = null): StructureState {
  return { rows, selectedId, history: [] };
}

export function moduleCount(rows: Rows): number {
  return rows.reduce((n, r) => n + r.length, 0);
}

/** "m01", "m02"…: the next id no module in the template uses. */
export function nextModuleId(rows: Rows): string {
  const used = new Set(rows.flat().map((m) => m.id));
  let n = 1;
  while (used.has(`m${String(n).padStart(2, '0')}`)) {
    n += 1;
  }
  return `m${String(n).padStart(2, '0')}`;
}

const LINKABLE: ReadonlySet<ContentShape> = new Set<ContentShape>(['paragraph', 'list', 'table', 'quote', 'timeline']);

/** A module as the palette adds it: the catalog's defaults. */
export function newModule(info: ModuleInfo, id: string): ModuleSettings {
  return {
    id,
    module: info.id,
    instructions: info.generated ? info.description : '',
    length: info.defaultLength,
    textSize: 'normal',
    linkToTranscript: info.generated && LINKABLE.has(info.shape),
    customTitle: null,
    customText: info.id === 'customText' ? '' : null,
  };
}

function without(rows: Rows, id: string): Rows {
  return rows.map((r) => r.filter((m) => m.id !== id)).filter((r) => r.length > 0);
}

function find(rows: Rows, id: string): { row: number; col: number; module: ModuleSettings } | null {
  for (let row = 0; row < rows.length; row++) {
    const col = rows[row]?.findIndex((m) => m.id === id) ?? -1;
    const module = rows[row]?.[col];
    if (col >= 0 && module !== undefined) {
      return { row, col, module };
    }
  }
  return null;
}

/** The module a drop places: the dragged card, or a new one for a palette entry. */
function payloadModule(rows: Rows, payload: DragPayload): ModuleSettings | null {
  if (payload.kind === 'card') {
    return find(rows, payload.id)?.module ?? null;
  }
  return newModule(payload.module, nextModuleId(rows));
}

/** Would dropping `payload` beside row `rowIndex` be accepted (fewer than three, not already in it)? */
export function canDropBeside(rows: Rows, rowIndex: number, payload: DragPayload | null): boolean {
  const row = rows[rowIndex];
  if (row === undefined || payload === null || row.length >= MAX_PER_ROW) {
    return false;
  }
  return payload.kind === 'palette' || !row.some((m) => m.id === payload.id);
}

function changed(state: StructureState, rows: Rows, selectedId: string | null = state.selectedId): StructureState {
  return { rows, selectedId, history: [...state.history, state.rows].slice(-HISTORY_LIMIT) };
}

export function structureReducer(state: StructureState, action: StructureAction): StructureState {
  switch (action.type) {
    case 'load':
      return initialStructure(action.rows, action.selectedId ?? null);
    case 'append': {
      const module = newModule(action.module, nextModuleId(state.rows));
      return changed(state, [...state.rows, [module]], module.id);
    }
    case 'dropRow': {
      const module = payloadModule(state.rows, action.payload);
      if (module === null) {
        return state;
      }
      // Rows before the gap that still exist once the dragged card has left its place.
      const before = state.rows.slice(0, action.index).map((r) => r.filter((m) => m.id !== module.id)).filter((r) => r.length > 0).length;
      const rest = without(state.rows, module.id);
      const rows = [...rest.slice(0, before), [module], ...rest.slice(before)];
      return changed(state, rows, module.id);
    }
    case 'dropBeside': {
      if (!canDropBeside(state.rows, action.rowIndex, action.payload)) {
        return state;
      }
      const module = payloadModule(state.rows, action.payload);
      const target = state.rows[action.rowIndex];
      if (module === null || target === undefined) {
        return state;
      }
      const targetIds = target.map((m) => m.id).join('|');
      const rows = without(state.rows, module.id).map((r) => (r.map((m) => m.id).join('|') === targetIds ? [...r, module] : r));
      return changed(state, rows, module.id);
    }
    case 'up':
    case 'down': {
      const at = find(state.rows, action.id);
      if (at === null) {
        return state;
      }
      const row = state.rows[at.row] ?? [];
      const dir = action.type === 'up' ? -1 : 1;
      if (row.length > 1) {
        // In a shared row: pull it out into its own row above or below.
        const rest = without(state.rows, action.id);
        const index = dir < 0 ? at.row : at.row + 1;
        return changed(state, [...rest.slice(0, index), [at.module], ...rest.slice(index)]);
      }
      const target = at.row + dir;
      if (target < 0 || target >= state.rows.length) {
        return state;
      }
      const rows = state.rows.slice();
      const moved = rows[at.row];
      const other = rows[target];
      if (moved === undefined || other === undefined) {
        return state;
      }
      rows[at.row] = other;
      rows[target] = moved;
      return changed(state, rows);
    }
    case 'left':
    case 'right': {
      const at = find(state.rows, action.id);
      const row = at === null ? undefined : state.rows[at.row];
      if (at === null || row === undefined) {
        return state;
      }
      const target = at.col + (action.type === 'left' ? -1 : 1);
      if (target < 0 || target >= row.length) {
        return state;
      }
      const next = row.slice();
      const other = next[target];
      if (other === undefined) {
        return state;
      }
      next[target] = at.module;
      next[at.col] = other;
      return changed(
        state,
        state.rows.map((r, i) => (i === at.row ? next : r)),
      );
    }
    case 'remove': {
      if (find(state.rows, action.id) === null) {
        return state;
      }
      return changed(state, without(state.rows, action.id), state.selectedId === action.id ? null : state.selectedId);
    }
    case 'update':
      return {
        ...state,
        rows: state.rows.map((r) => r.map((m) => (m.id === action.id ? { ...m, ...action.patch } : m))),
      };
    case 'select':
      return state.selectedId === action.id ? state : { ...state, selectedId: action.id };
    case 'undo': {
      const previous = state.history[state.history.length - 1];
      if (previous === undefined) {
        return state;
      }
      // The layout goes back; settings edited since keep their latest values.
      const current = new Map(state.rows.flat().map((m) => [m.id, m]));
      const rows = previous.map((r) => r.map((m) => current.get(m.id) ?? m));
      const selectedId = rows.some((r) => r.some((m) => m.id === state.selectedId)) ? state.selectedId : null;
      return { rows, selectedId, history: state.history.slice(0, -1) };
    }
  }
}

/** Where a module sits, in words for screen readers: "row 3", "row 2, column 1 of 2". */
export function positionWords(rows: Rows, id: string): string {
  const at = find(rows, id);
  if (at === null) {
    return '';
  }
  const width = rows[at.row]?.length ?? 1;
  return width > 1 ? `row ${at.row + 1}, column ${at.col + 1} of ${width}` : `row ${at.row + 1}`;
}
