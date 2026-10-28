// The transcript follows the playhead (DESIGN.md §9): the current segment is scrolled into view as
// playback moves on, unless the person is working in the transcript themselves. Scrolling by hand,
// and (2.7.0) any interaction with the transcript or People (clicks, typing, menus, selection), holds
// following; it resumes after FOLLOW_PAUSE_MS without any, and never while a field in the transcript
// has focus. The page's own scrolls do not count as theirs. "Jump to playing" resumes at once.

/** How long following stays off after the last scroll or interaction by hand. */
export const FOLLOW_PAUSE_MS = 5000;

/** Scroll events this soon after the page scrolled itself are the page's own. */
export const PROGRAMMATIC_GRACE_MS = 600;

export interface FollowPlayhead {
  /** Wheel, touch, scrollbar or keyboard scrolling by the person. */
  noteUserScroll(): void;
  /** The page is about to scroll to the current segment. */
  noteProgrammaticScroll(): void;
  /** A `scroll` event: counts as the person's unless the page just scrolled itself. */
  noteScrollEvent(): void;
  /** 2.7.0: a click, key, menu, edit or selection in the transcript or People holds following. */
  noteInteraction(): void;
  /** 2.7.0: a field in the transcript (a line being edited, a rename) took or lost focus; following waits for it. */
  setFieldFocused(focused: boolean): void;
  /** Whether a change of current segment should scroll it into view now. */
  shouldFollow(): boolean;
  /** Following resumes at once (Jump to playing, a search jump, Hear {name}). */
  resume(): void;
}

export function createFollowPlayhead(now: () => number = () => Date.now()): FollowPlayhead {
  let userUntil = Number.NEGATIVE_INFINITY;
  let programmaticUntil = Number.NEGATIVE_INFINITY;
  let fieldFocused = false;
  const hold = (): void => {
    userUntil = now() + FOLLOW_PAUSE_MS;
  };
  return {
    noteUserScroll: hold,
    noteInteraction: hold,
    setFieldFocused: (focused) => {
      fieldFocused = focused;
      // The quiet time counts from leaving the field.
      hold();
    },
    noteProgrammaticScroll: () => {
      programmaticUntil = now() + PROGRAMMATIC_GRACE_MS;
    },
    noteScrollEvent: () => {
      if (now() > programmaticUntil) {
        hold();
      }
    },
    shouldFollow: () => !fieldFocused && now() >= userUntil,
    resume: () => {
      userUntil = Number.NEGATIVE_INFINITY;
    },
  };
}

/**
 * Where to scroll so a row is comfortably visible, or null when it already is. `viewTop` and
 * `viewBottom` bound the visible part of the page (below the sticky header and player strip);
 * `rowTop`/`rowBottom` are the row's edges in the same coordinates. A row out of view lands about a
 * third of the way down the visible part.
 */
export function followScrollDelta(rowTop: number, rowBottom: number, viewTop: number, viewBottom: number): number | null {
  const margin = 12;
  if (rowTop >= viewTop + margin && rowBottom <= viewBottom - margin) {
    return null;
  }
  const target = viewTop + (viewBottom - viewTop) / 3;
  return rowTop - target;
}
