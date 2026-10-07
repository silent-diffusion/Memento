import { describe, expect, it } from 'vitest';
import type { ModuleId, ModuleInfo, ModuleSettings } from '../../bridge/types';
import { paletteGroups } from './Palette';
import { canDropBeside, initialStructure, MAX_PER_ROW, nextModuleId, positionWords, structureReducer, type Rows, type StructureState } from './structureState';

const info = (id: ModuleId, name: string, group: ModuleInfo['group'] = 'structure', generated = true): ModuleInfo => ({
  id,
  name,
  group,
  shape: 'paragraph',
  generated,
  defaultLength: 'medium',
  groundingRule: null,
  description: `${name} described.`,
});

const mod = (id: string, module: ModuleId = 'notes'): ModuleSettings => ({
  id,
  module,
  instructions: '',
  length: 'medium',
  textSize: 'normal',
  linkToTranscript: false,
  customTitle: null,
  customText: null,
});

/** [["m01"], ["m02","m03"], …] → rows. */
const rows = (...ids: string[][]): Rows => ids.map((row) => row.map((id) => mod(id)));
const shape = (state: StructureState): string[][] => state.rows.map((r) => r.map((m) => m.id));
const start = (...ids: string[][]): StructureState => initialStructure(rows(...ids));

describe('Builder structure (DESIGN.md §10, §5.17)', () => {
  it('appends a palette module as a new row with the next free id and selects it', () => {
    const state = structureReducer(start(['m01'], ['m03']), { type: 'append', module: info('decisions', 'Decisions') });
    expect(shape(state)).toEqual([['m01'], ['m03'], ['m02']]);
    expect(state.selectedId).toBe('m02');
    expect(state.rows[2]?.[0]).toMatchObject({ module: 'decisions', instructions: 'Decisions described.', length: 'medium', textSize: 'normal' });
    expect(nextModuleId(rows(['m01', 'm02']))).toBe('m03');
  });

  it('drops a card in the gap before a row, collapsing the row it left', () => {
    const before = start(['m01'], ['m02', 'm03'], ['m04']);
    const moved = structureReducer(before, { type: 'dropRow', index: 0, payload: { kind: 'card', id: 'm04' } });
    expect(shape(moved)).toEqual([['m04'], ['m01'], ['m02', 'm03']]);
    // A lone card moved down: the gap index counts the rows that remain once it has left.
    const down = structureReducer(before, { type: 'dropRow', index: 3, payload: { kind: 'card', id: 'm01' } });
    expect(shape(down)).toEqual([['m02', 'm03'], ['m04'], ['m01']]);
    // Out of a shared row into its own row; the shared row keeps the other module.
    const out = structureReducer(before, { type: 'dropRow', index: 2, payload: { kind: 'card', id: 'm02' } });
    expect(shape(out)).toEqual([['m01'], ['m03'], ['m02'], ['m04']]);
    // A palette entry dropped in a gap is a new module there.
    const added = structureReducer(before, { type: 'dropRow', index: 1, payload: { kind: 'palette', module: info('quote', 'Quote', 'detail') } });
    expect(shape(added)).toEqual([['m01'], ['m05'], ['m02', 'm03'], ['m04']]);
    expect(added.selectedId).toBe('m05');
  });

  it('drops beside a row: side by side, at most three, never twice in one row', () => {
    const before = start(['m01'], ['m02', 'm03'], ['m04']);
    const beside = structureReducer(before, { type: 'dropBeside', rowIndex: 1, payload: { kind: 'card', id: 'm01' } });
    expect(shape(beside)).toEqual([['m02', 'm03', 'm01'], ['m04']]);
    expect(canDropBeside(beside.rows, 0, { kind: 'card', id: 'm04' })).toBe(false);
    expect(structureReducer(beside, { type: 'dropBeside', rowIndex: 0, payload: { kind: 'card', id: 'm04' } })).toBe(beside);
    expect(MAX_PER_ROW).toBe(3);
    // Already in the row: refused.
    expect(canDropBeside(before.rows, 1, { kind: 'card', id: 'm02' })).toBe(false);
    expect(structureReducer(before, { type: 'dropBeside', rowIndex: 1, payload: { kind: 'card', id: 'm03' } })).toBe(before);
    // A palette module beside a single.
    const fromPalette = structureReducer(before, { type: 'dropBeside', rowIndex: 2, payload: { kind: 'palette', module: info('owner', 'Owner', 'detail') } });
    expect(shape(fromPalette)).toEqual([['m01'], ['m02', 'm03'], ['m04', 'm05']]);
  });

  it('moves with the up and down buttons: a lone row swaps, a shared module comes out above or below', () => {
    const before = start(['m01'], ['m02', 'm03'], ['m04']);
    expect(shape(structureReducer(before, { type: 'down', id: 'm01' }))).toEqual([['m02', 'm03'], ['m01'], ['m04']]);
    expect(shape(structureReducer(before, { type: 'up', id: 'm03' }))).toEqual([['m01'], ['m03'], ['m02'], ['m04']]);
    expect(shape(structureReducer(before, { type: 'down', id: 'm02' }))).toEqual([['m01'], ['m03'], ['m02'], ['m04']]);
    expect(structureReducer(before, { type: 'up', id: 'm01' })).toBe(before);
    expect(structureReducer(before, { type: 'down', id: 'm04' })).toBe(before);
    expect(shape(structureReducer(before, { type: 'right', id: 'm02' }))).toEqual([['m01'], ['m03', 'm02'], ['m04']]);
    expect(structureReducer(before, { type: 'left', id: 'm02' })).toBe(before);
    expect(positionWords(before.rows, 'm03')).toBe('row 2, column 2 of 2');
  });

  it('removes and undoes, keeping settings edited since', () => {
    let state = start(['m01'], ['m02', 'm03']);
    state = structureReducer(state, { type: 'select', id: 'm02' });
    state = structureReducer(state, { type: 'remove', id: 'm02' });
    expect(shape(state)).toEqual([['m01'], ['m03']]);
    expect(state.selectedId).toBeNull();
    state = structureReducer(state, { type: 'update', id: 'm03', patch: { instructions: 'Shorter.', textSize: 'smaller' } });
    expect(state.history).toHaveLength(1);
    state = structureReducer(state, { type: 'undo' });
    expect(shape(state)).toEqual([['m01'], ['m02', 'm03']]);
    expect(state.rows[1]?.[1]).toMatchObject({ instructions: 'Shorter.', textSize: 'smaller' });
    // Nothing left to undo.
    expect(structureReducer(state, { type: 'undo' })).toBe(state);
    // Removing the last module of a row collapses the row.
    expect(shape(structureReducer(start(['m01'], ['m02']), { type: 'remove', id: 'm02' }))).toEqual([['m01']]);
  });

  it('undoes moves and drops one step at a time', () => {
    let state = start(['m01'], ['m02'], ['m03']);
    state = structureReducer(state, { type: 'dropBeside', rowIndex: 0, payload: { kind: 'card', id: 'm03' } });
    state = structureReducer(state, { type: 'up', id: 'm02' });
    expect(shape(state)).toEqual([['m02'], ['m01', 'm03']]);
    state = structureReducer(state, { type: 'undo' });
    expect(shape(state)).toEqual([['m01', 'm03'], ['m02']]);
    state = structureReducer(state, { type: 'undo' });
    expect(shape(state)).toEqual([['m01'], ['m02'], ['m03']]);
  });
});

describe('palette search', () => {
  const catalog = [info('summary', 'Summary'), info('decisions', 'Decisions'), info('quote', 'Quote', 'detail'), info('customText', 'Custom text', 'custom', false)];

  it('keeps the groups in order and hides empty ones', () => {
    expect(paletteGroups(catalog, '').map((g) => [g.label, g.items.map((m) => m.name)])).toEqual([
      ['Structure', ['Summary', 'Decisions']],
      ['Detail', ['Quote']],
      ['Custom', ['Custom text']],
    ]);
    expect(paletteGroups(catalog, 'QUO').map((g) => g.label)).toEqual(['Detail']);
    // Every word must match, in the name or the description.
    expect(paletteGroups(catalog, 'custom described').flatMap((g) => g.items.map((m) => m.id))).toEqual(['customText']);
    expect(paletteGroups(catalog, 'nothing like this')).toEqual([]);
  });
});
