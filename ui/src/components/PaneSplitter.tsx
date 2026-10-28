// 2.7.0: the splitter between two side-by-side panes (Review). Drag it, or focus it and use the arrow
// keys (Shift for bigger steps), Home, End and Enter; double-click puts the pane back to its default.
import type { JSX } from 'preact';
import { useRef } from 'preact/hooks';
import { clampPaneWidth, PANE_BOUNDS, paneWidthForKey, type ReviewPane } from '../state/paneWidths';

/** Pointer capture keeps the drag when the pointer leaves the grip; an environment without it just drags less smoothly. */
function capture(element: Element, pointerId: number, on: boolean): void {
  try {
    if (on) {
      element.setPointerCapture(pointerId);
    } else {
      element.releasePointerCapture(pointerId);
    }
  } catch {
    // Not supported here (or the pointer is gone): the drag still works while over the grip.
  }
}

interface PaneSplitterProps {
  pane: ReviewPane;
  label: string;
  width: number;
  /** Which side of the splitter the pane is on: dragging away from it widens it. */
  side: 'left' | 'right';
  /** Every move while dragging or on a key; `done` once a drag ends or a key moved it (the moment to save). */
  onWidth: (width: number, done: boolean) => void;
}

export function PaneSplitter({ pane, label, width, side, onWidth }: PaneSplitterProps): JSX.Element {
  const drag = useRef<{ x: number; width: number; last: number } | null>(null);
  const bounds = PANE_BOUNDS[pane];
  const sign = side === 'left' ? 1 : -1;
  return (
    <div
      class="pane-splitter"
      role="separator"
      aria-orientation="vertical"
      aria-label={label}
      aria-valuemin={bounds.min}
      aria-valuemax={bounds.max}
      aria-valuenow={width}
      tabIndex={0}
      title={`${label}: drag, or use the arrow keys. Double-click to reset.`}
      onPointerDown={(event) => {
        if (event.button !== 0) {
          return;
        }
        event.preventDefault();
        capture(event.currentTarget, event.pointerId, true);
        drag.current = { x: event.clientX, width, last: width };
      }}
      onPointerMove={(event) => {
        const d = drag.current;
        if (d === null) {
          return;
        }
        d.last = clampPaneWidth(pane, d.width + sign * (event.clientX - d.x));
        onWidth(d.last, false);
      }}
      onPointerUp={(event) => {
        const d = drag.current;
        drag.current = null;
        capture(event.currentTarget, event.pointerId, false);
        if (d !== null) {
          onWidth(d.last, true);
        }
      }}
      onPointerCancel={() => {
        const d = drag.current;
        drag.current = null;
        if (d !== null) {
          onWidth(d.last, true);
        }
      }}
      onDblClick={() => {
        onWidth(bounds.initial, true);
      }}
      onKeyDown={(event) => {
        const next = paneWidthForKey(pane, width, event.key, event.shiftKey, side === 'left' ? 'ArrowRight' : 'ArrowLeft');
        if (next !== null) {
          event.preventDefault();
          onWidth(next, true);
        }
      }}
    />
  );
}
