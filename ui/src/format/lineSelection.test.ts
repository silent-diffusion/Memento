import { describe, expect, it } from 'vitest';
import { extendSelection, keepExisting, NO_SELECTION, selectAll, selectionWording, selectRange, toggleLine } from './lineSelection';

const ORDER = ['a', 'b', 'c', 'd', 'e'];

describe('line selection (2.0 selection mode)', () => {
  it('toggles a line and makes it the anchor', () => {
    const one = toggleLine(NO_SELECTION, 'b');
    expect([...one.ids]).toEqual(['b']);
    expect(one.anchor).toBe('b');
    const none = toggleLine(one, 'b');
    expect([...none.ids]).toEqual([]);
    expect(none.anchor).toBe('b');
  });

  it('adds the range from the anchor to the clicked line, either way', () => {
    expect([...selectRange(toggleLine(NO_SELECTION, 'b'), ORDER, 'd').ids].sort()).toEqual(['b', 'c', 'd']);
    expect([...selectRange(toggleLine(NO_SELECTION, 'd'), ORDER, 'a').ids].sort()).toEqual(['a', 'b', 'c', 'd']);
    // Without an anchor, Shift+click selects just that line.
    expect([...selectRange(NO_SELECTION, ORDER, 'c').ids]).toEqual(['c']);
    // A line not shown (filtered out) changes nothing.
    const kept = toggleLine(NO_SELECTION, 'a');
    expect(selectRange(kept, ORDER, 'z')).toBe(kept);
  });

  it('selects every line shown', () => {
    expect([...selectAll(['b', 'd']).ids]).toEqual(['b', 'd']);
  });

  it('extends from the anchor with Shift+arrows and says which line takes focus', () => {
    const start = toggleLine(NO_SELECTION, 'b');
    const down = extendSelection(start, ORDER, 'b', 1);
    expect([...down.selection.ids]).toEqual(['b', 'c']);
    expect(down.focus).toBe('c');
    const back = extendSelection(down.selection, ORDER, 'c', -1);
    expect([...back.selection.ids]).toEqual(['b']);
    const up = extendSelection(back.selection, ORDER, 'b', -1);
    expect([...up.selection.ids]).toEqual(['a', 'b']);
    // At the top it stays.
    expect(extendSelection(up.selection, ORDER, 'a', -1).focus).toBe('a');
  });

  it('drops lines that no longer exist', () => {
    const selection = selectAll(ORDER);
    expect(keepExisting(selection, new Set(ORDER))).toBe(selection);
    const kept = keepExisting(selection, new Set(['b', 'c']));
    expect([...kept.ids]).toEqual(['b', 'c']);
    expect(kept.anchor).toBeNull();
  });

  it('words the count and the speech', () => {
    expect(selectionWording(1, 4.4)).toBe('1 line selected · 4 s of speech');
    expect(selectionWording(3, 102)).toBe('3 lines selected · 1:42 of speech');
  });
});
