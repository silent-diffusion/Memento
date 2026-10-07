// Simple windowing for long lists of rows with different heights (the transcript): rows are laid out
// from measured heights where known and an estimate otherwise, and only the rows near the viewport
// are rendered, with spacers standing in for the rest.

/** Heights by row index; unknown rows use `estimate`. */
export class RowHeights {
  private readonly measured = new Map<number, number>();
  private offsetsCache: Float64Array | null = null;

  constructor(
    private count: number,
    private readonly estimate: number,
  ) {}

  get length(): number {
    return this.count;
  }

  /** Changing the count keeps the heights already measured for the rows that remain. */
  resize(count: number): void {
    if (count === this.count) {
      return;
    }
    this.count = count;
    for (const index of [...this.measured.keys()]) {
      if (index >= count) {
        this.measured.delete(index);
      }
    }
    this.offsetsCache = null;
  }

  /** Forgets every measurement (the rows' content changed throughout). */
  clear(): void {
    this.measured.clear();
    this.offsetsCache = null;
  }

  height(index: number): number {
    return this.measured.get(index) ?? this.estimate;
  }

  /** Records a measured height; 0 (not laid out, e.g. in tests) is ignored. Returns true if it changed. */
  set(index: number, height: number): boolean {
    if (!(height > 0) || index < 0 || index >= this.count || this.measured.get(index) === height) {
      return false;
    }
    this.measured.set(index, height);
    this.offsetsCache = null;
    return true;
  }

  /** offsets[i] is the top of row i; offsets[count] is the total height. */
  offsets(): Float64Array {
    if (this.offsetsCache === null) {
      const out = new Float64Array(this.count + 1);
      for (let i = 0; i < this.count; i++) {
        out[i + 1] = (out[i] ?? 0) + this.height(i);
      }
      this.offsetsCache = out;
    }
    return this.offsetsCache;
  }

  total(): number {
    return this.offsets()[this.count] ?? 0;
  }
}

export interface ListWindow {
  /** First rendered row. */
  start: number;
  /** One past the last rendered row. */
  end: number;
  /** Space above the first rendered row. */
  padTop: number;
  /** Space below the last rendered row. */
  padBottom: number;
}

/** The first row whose bottom is below `y` (binary search over the offsets). */
export function rowAt(offsets: Float64Array, count: number, y: number): number {
  let lo = 0;
  let hi = count - 1;
  let found = count;
  while (lo <= hi) {
    const mid = (lo + hi) >> 1;
    if ((offsets[mid + 1] ?? 0) > y) {
      found = mid;
      hi = mid - 1;
    } else {
      lo = mid + 1;
    }
  }
  return found;
}

/**
 * Which rows to render when the list's visible part runs from `viewTop` to `viewBottom` (in list
 * coordinates, 0 = the list's top edge), with `overscan` rows either side. Rows in `keep` (the one
 * being edited, the focused one) stay rendered by widening the window when they are within
 * `KEEP_REACH` rows of it; further away they are let go (their state lives with the list).
 */
export const KEEP_REACH = 40;

export function computeWindow(heights: RowHeights, viewTop: number, viewBottom: number, overscan = 6, keep: readonly number[] = []): ListWindow {
  const count = heights.length;
  if (count === 0) {
    return { start: 0, end: 0, padTop: 0, padBottom: 0 };
  }
  const offsets = heights.offsets();
  const total = offsets[count] ?? 0;
  const top = Math.max(0, Math.min(viewTop, total));
  const bottom = Math.max(top, Math.min(viewBottom, total));
  let start = Math.max(0, Math.min(count - 1, rowAt(offsets, count, top)) - overscan);
  let end = Math.min(count, rowAt(offsets, count, bottom) + 1 + overscan);
  for (const index of keep) {
    if (index >= 0 && index < count && index >= start - KEEP_REACH && index < end + KEEP_REACH) {
      start = Math.min(start, index);
      end = Math.max(end, index + 1);
    }
  }
  return {
    start,
    end,
    padTop: offsets[start] ?? 0,
    padBottom: total - (offsets[end] ?? total),
  };
}
