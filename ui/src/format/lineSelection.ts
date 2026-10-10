// Selection mode in Review's transcript (2.0, DESIGN.md §19 adapted to transcript lines): which lines are selected
// and the line a Shift+click or Shift+arrow extends from. Pure; `order` is always the lines shown (a filter hides the
// others, and Ctrl+A selects only what is shown).

export interface LineSelection {
  ids: ReadonlySet<string>;
  /** Where a range starts: the last line clicked or toggled. */
  anchor: string | null;
}

export const NO_SELECTION: LineSelection = { ids: new Set(), anchor: null };

/** Ctrl+click, Space, a click while selecting: the line joins or leaves the selection and becomes the anchor. */
export function toggleLine(selection: LineSelection, id: string): LineSelection {
  const ids = new Set(selection.ids);
  if (ids.has(id)) {
    ids.delete(id);
  } else {
    ids.add(id);
  }
  return { ids, anchor: id };
}

/** Shift+click: the lines from the anchor to this one (as shown), added to the selection; the anchor stays. */
export function selectRange(selection: LineSelection, order: readonly string[], id: string): LineSelection {
  const to = order.indexOf(id);
  const from = selection.anchor === null ? -1 : order.indexOf(selection.anchor);
  if (to < 0) {
    return selection;
  }
  if (from < 0) {
    return { ids: new Set([...selection.ids, id]), anchor: id };
  }
  const [start, end] = from <= to ? [from, to] : [to, from];
  return { ids: new Set([...selection.ids, ...order.slice(start, end + 1)]), anchor: selection.anchor };
}

/** Ctrl+A: every line shown. */
export function selectAll(order: readonly string[]): LineSelection {
  return { ids: new Set(order), anchor: order[0] ?? null };
}

/**
 * Shift+↑/↓ from the focused line: the range from the anchor to the next line in that direction (the anchor is the
 * focused line when there is none). Returns the selection and the line that takes focus.
 */
export function extendSelection(selection: LineSelection, order: readonly string[], focusedId: string, direction: 1 | -1): { selection: LineSelection; focus: string } {
  const at = order.indexOf(focusedId);
  const next = order[Math.max(0, Math.min(order.length - 1, at + direction))] ?? focusedId;
  const anchor = selection.anchor !== null && order.includes(selection.anchor) ? selection.anchor : focusedId;
  const a = order.indexOf(anchor);
  const b = order.indexOf(next);
  const [start, end] = a <= b ? [a, b] : [b, a];
  return { selection: { ids: new Set(order.slice(start, end + 1)), anchor }, focus: next };
}

/** Lines that no longer exist (transcribed again) leave the selection. */
export function keepExisting(selection: LineSelection, existing: ReadonlySet<string>): LineSelection {
  if ([...selection.ids].every((id) => existing.has(id))) {
    return selection;
  }
  return { ids: new Set([...selection.ids].filter((id) => existing.has(id))), anchor: selection.anchor !== null && existing.has(selection.anchor) ? selection.anchor : null };
}

/** "3 lines selected · 1:42 of speech". */
export function selectionWording(count: number, seconds: number): string {
  const total = Math.round(seconds);
  const time = total >= 60 ? `${Math.floor(total / 60)}:${String(total % 60).padStart(2, '0')}` : `${total} s`;
  return `${count} ${count === 1 ? 'line' : 'lines'} selected · ${time} of speech`;
}
