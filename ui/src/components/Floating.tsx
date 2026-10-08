// Popovers inside panes that scroll on their own (Review's outline, transcript and details) would be
// clipped by the pane. Inside such a pane they render in a layer on <body> instead, placed beside
// their trigger with fixed positioning and kept there while anything scrolls or the window resizes.
import { createContext, type ComponentChildren, type JSX } from 'preact';
import { createPortal } from 'preact/compat';
import { useContext, useLayoutEffect } from 'preact/hooks';

/** True inside a container whose popovers must not be clipped by it. */
export const FloatingPopovers = createContext(false);

export function useFloatingPopovers(): boolean {
  return useContext(FloatingPopovers);
}

interface Box {
  top: number;
  bottom: number;
  left: number;
  right: number;
}

/** The space a popover keeps from the trigger and from the window's edges. */
const GAP = 8;
const EDGE = 8;

/**
 * Where a popover of `size` goes for a trigger at `anchor`, in viewport pixels: below the trigger,
 * aligned to its end (right edge) or start, flipped above when it does not fit below but does above,
 * and kept inside the window.
 */
export function floatingPosition(
  anchor: Box,
  size: { width: number; height: number },
  viewport: { width: number; height: number },
  align: 'start' | 'end',
  gap = GAP,
): { top: number; left: number } {
  const below = anchor.bottom + gap;
  const above = anchor.top - gap - size.height;
  const fitsBelow = below + size.height <= viewport.height - EDGE;
  const fitsAbove = above >= EDGE;
  const top = fitsBelow || !fitsAbove ? Math.max(EDGE, Math.min(below, viewport.height - EDGE - size.height)) : above;
  const wanted = align === 'end' ? anchor.right - size.width : anchor.left;
  const left = Math.max(EDGE, Math.min(wanted, viewport.width - EDGE - size.width));
  return { top: Math.round(top), left: Math.round(left) };
}

/**
 * The tallest a popover may be beside a trigger at `anchor`: the room below it or, when larger, above it,
 * less the gap and the window edge, and never more than `cap`. Long lists (speakers) scroll inside that.
 */
export function availableHeight(anchor: Pick<Box, 'top' | 'bottom'>, viewportHeight: number, cap: number, gap = GAP): number {
  const below = viewportHeight - EDGE - (anchor.bottom + gap);
  const above = anchor.top - gap - EDGE;
  return Math.max(120, Math.min(cap, Math.floor(Math.max(below, above))));
}

interface PopoverLayerProps {
  /** The element the popover belongs to (its trigger or the menu root). */
  anchorRef: { current: HTMLElement | null };
  popRef: { current: HTMLElement | null };
  align?: 'start' | 'end';
  gap?: number;
  /** Caps the popover at this height and at the room beside the trigger; its content scrolls (`max-height` is set). */
  capHeight?: number;
  children: ComponentChildren;
}

/** Renders the popover in place, or, inside `FloatingPopovers`, in a fixed layer on <body>. */
export function PopoverLayer({ anchorRef, popRef, align = 'end', gap = GAP, capHeight, children }: PopoverLayerProps): JSX.Element {
  const floating = useFloatingPopovers();
  useLayoutEffect(() => {
    const fit = (): void => {
      const anchor = anchorRef.current;
      const pop = popRef.current;
      if (capHeight !== undefined && anchor !== null && pop !== null) {
        pop.style.maxHeight = `${availableHeight(anchor.getBoundingClientRect(), window.innerHeight, capHeight, gap)}px`;
      }
    };
    if (!floating) {
      fit();
      return undefined;
    }
    const place = (): void => {
      const anchor = anchorRef.current;
      const pop = popRef.current;
      if (anchor === null || pop === null) {
        return;
      }
      fit();
      const { top, left } = floatingPosition(
        anchor.getBoundingClientRect(),
        { width: pop.offsetWidth, height: pop.offsetHeight },
        { width: window.innerWidth, height: window.innerHeight },
        align,
        gap,
      );
      pop.style.top = `${top}px`;
      pop.style.left = `${left}px`;
    };
    place();
    window.addEventListener('scroll', place, { capture: true, passive: true });
    window.addEventListener('resize', place);
    return () => {
      window.removeEventListener('scroll', place, { capture: true });
      window.removeEventListener('resize', place);
    };
  });
  if (!floating) {
    return <>{children}</>;
  }
  return createPortal(<div class="popover-layer">{children}</div>, document.body);
}
