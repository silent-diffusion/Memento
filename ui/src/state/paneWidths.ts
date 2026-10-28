// 2.7.0: the widths of Review's side panes (Outline with People, Details), set by dragging the
// splitters beside them and remembered on this PC (uiPrefs.ts). Kept within bounds so the centre pane
// always keeps room for the transcript.
import { readUiPref, writeUiPref } from './uiPrefs';

export type ReviewPane = 'outline' | 'details';

export interface PaneWidths {
  outline: number;
  details: number;
}

export interface PaneBounds {
  min: number;
  max: number;
  initial: number;
}

export const PANE_BOUNDS: Record<ReviewPane, PaneBounds> = {
  outline: { min: 220, max: 480, initial: 260 },
  details: { min: 260, max: 560, initial: 320 },
};

export const DEFAULT_PANE_WIDTHS: PaneWidths = { outline: PANE_BOUNDS.outline.initial, details: PANE_BOUNDS.details.initial };

/** One arrow-key step; Shift moves further. */
export const PANE_STEP = 16;
export const PANE_STEP_LARGE = 64;

export function clampPaneWidth(pane: ReviewPane, width: number): number {
  const { min, max } = PANE_BOUNDS[pane];
  return Math.round(Math.min(max, Math.max(min, width)));
}

export function parsePaneWidths(value: unknown): PaneWidths | null {
  if (typeof value !== 'object' || value === null) {
    return null;
  }
  const { outline, details } = value as Record<string, unknown>;
  if (typeof outline !== 'number' || typeof details !== 'number' || !Number.isFinite(outline) || !Number.isFinite(details)) {
    return null;
  }
  return { outline: clampPaneWidth('outline', outline), details: clampPaneWidth('details', details) };
}

export function readPaneWidths(): PaneWidths {
  return readUiPref('review.paneWidths', DEFAULT_PANE_WIDTHS, parsePaneWidths);
}

export function writePaneWidths(widths: PaneWidths): void {
  writeUiPref('review.paneWidths', widths);
}

/** The width a key on the splitter moves to, or null for a key the splitter does not use. `grows` is the key that widens the pane. */
export function paneWidthForKey(pane: ReviewPane, width: number, key: string, shift: boolean, grows: 'ArrowRight' | 'ArrowLeft'): number | null {
  const step = shift ? PANE_STEP_LARGE : PANE_STEP;
  const shrinks = grows === 'ArrowRight' ? 'ArrowLeft' : 'ArrowRight';
  switch (key) {
    case grows:
      return clampPaneWidth(pane, width + step);
    case shrinks:
      return clampPaneWidth(pane, width - step);
    case 'Home':
      return PANE_BOUNDS[pane].min;
    case 'End':
      return PANE_BOUNDS[pane].max;
    case 'Enter':
      return PANE_BOUNDS[pane].initial;
    default:
      return null;
  }
}
