// The transcript follows the playhead (DESIGN.md §9): the current segment is scrolled into view as
// playback moves on, unless the person is scrolling the transcript themselves. Scrolling by hand
// pauses following for a few seconds; the page's own scrolls do not count as theirs.

/** How long following stays off after the last scroll by hand. */
export const FOLLOW_PAUSE_MS = 4000;

/** Scroll events this soon after the page scrolled itself are the page's own. */
export const PROGRAMMATIC_GRACE_MS = 600;

export interface FollowPlayhead {
  /** Wheel, touch, scrollbar or keyboard scrolling by the person. */
  noteUserScroll(): void;
  /** The page is about to scroll to the current segment. */
  noteProgrammaticScroll(): void;
  /** A `scroll` event: counts as the person's unless the page just scrolled itself. */
  noteScrollEvent(): void;
  /** Whether a change of current segment should scroll it into view now. */
  shouldFollow(): boolean;
  /** Following resumes at once (a click on a segment or chapter, a search jump). */
  resume(): void;
}

export function createFollowPlayhead(now: () => number = () => Date.now()): FollowPlayhead {
  let userUntil = Number.NEGATIVE_INFINITY;
  let programmaticUntil = Number.NEGATIVE_INFINITY;
  return {
    noteUserScroll: () => {
      userUntil = now() + FOLLOW_PAUSE_MS;
    },
    noteProgrammaticScroll: () => {
      programmaticUntil = now() + PROGRAMMATIC_GRACE_MS;
    },
    noteScrollEvent: () => {
      if (now() > programmaticUntil) {
        userUntil = now() + FOLLOW_PAUSE_MS;
      }
    },
    shouldFollow: () => now() >= userUntil,
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
