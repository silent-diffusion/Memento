import { describe, expect, it } from 'vitest';
import { createFollowPlayhead, FOLLOW_PAUSE_MS, followScrollDelta, PROGRAMMATIC_GRACE_MS } from './followPlayhead';

describe('the transcript follows the playhead', () => {
  const clock = (): { now: () => number; advance: (ms: number) => void } => {
    let t = 1_000;
    return {
      now: () => t,
      advance: (ms) => {
        t += ms;
      },
    };
  };

  it('follows until the person scrolls, then pauses for a few seconds', () => {
    const time = clock();
    const follow = createFollowPlayhead(time.now);
    expect(follow.shouldFollow()).toBe(true);
    follow.noteUserScroll();
    expect(follow.shouldFollow()).toBe(false);
    time.advance(FOLLOW_PAUSE_MS - 1);
    expect(follow.shouldFollow()).toBe(false);
    time.advance(1);
    expect(follow.shouldFollow()).toBe(true);
  });

  it('does not count its own scrolling as the person scrolling', () => {
    const time = clock();
    const follow = createFollowPlayhead(time.now);
    follow.noteProgrammaticScroll();
    follow.noteScrollEvent();
    time.advance(PROGRAMMATIC_GRACE_MS / 2);
    follow.noteScrollEvent();
    expect(follow.shouldFollow()).toBe(true);
    time.advance(PROGRAMMATIC_GRACE_MS);
    follow.noteScrollEvent();
    expect(follow.shouldFollow()).toBe(false);
  });

  it('resumes at once on a click or jump', () => {
    const follow = createFollowPlayhead(clock().now);
    follow.noteUserScroll();
    follow.resume();
    expect(follow.shouldFollow()).toBe(true);
  });

  it('scrolls only when the row is out of view, and then to a third of the way down', () => {
    expect(followScrollDelta(300, 360, 200, 800)).toBeNull();
    expect(followScrollDelta(900, 960, 200, 800)).toBe(900 - (200 + 200));
    expect(followScrollDelta(100, 160, 200, 800)).toBe(100 - 400);
    // A row tucked under the sticky player strip counts as out of view.
    expect(followScrollDelta(195, 260, 200, 800)).not.toBeNull();
  });
});
