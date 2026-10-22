import { describe, expect, it } from 'vitest';
import { acceptsOrigin, backTargetOf, navigationOf, recordMove, screenIdentity, TRAIL_LIMIT, type BackTarget, type NavigationState } from './navigation';
import type { Route } from './router';

const library: Route = { name: 'library' };
const review = (recordingId = 'r1', atMs?: number): Route => (atMs === undefined ? { name: 'review', recordingId } : { name: 'review', recordingId, atMs });
const builder = (recordingId: string | null = 'r1', documentId: string | null = null): Route => ({ name: 'builder', recordingId, templateId: null, documentId });
const doc = (recordingId = 'r1', documentId = 'd1'): Route => ({ name: 'document', recordingId, documentId });
const settings = (section: 'general' | 'ai-privacy' | 'documents' | 'transcription' = 'general'): Route => ({ name: 'settings', section });
const record: Route = { name: 'record', sessionId: null };

const LIBRARY: BackTarget = { route: library, label: 'Library' };
const REVIEW: BackTarget = { route: review(), label: 'Design review' };

/** A fresh navigation state and a helper that walks it like the router does. */
function walker(): { state: NavigationState; at: () => Route; go: (to: Route, label?: string | null) => void; back: (to: Route) => void } {
  const state = navigationOf({});
  let current: Route = library;
  return {
    state,
    at: () => current,
    go: (to, label = null) => {
      state.label = label;
      recordMove(state, current, to, 'forward');
      current = to;
    },
    back: (to) => {
      recordMove(state, current, to, 'back');
      current = to;
    },
  };
}

describe('which origins a spoke returns to (DESIGN.md §3)', () => {
  it('Settings returns to any spoke that opened it, never to itself or the Library', () => {
    for (const origin of [review(), builder(), doc(), record, { name: 'style', styleId: 'corporate' } as Route]) {
      expect(acceptsOrigin(settings(), origin)).toBe(true);
    }
    expect(acceptsOrigin(settings(), settings('documents'))).toBe(false);
    expect(acceptsOrigin(settings(), library)).toBe(false);
  });

  it('Review returns only to a document of the same recording (a timestamp chip)', () => {
    expect(acceptsOrigin(review('r1'), doc('r1'))).toBe(true);
    expect(acceptsOrigin(review('r2'), doc('r1'))).toBe(false);
    expect(acceptsOrigin(review('r1'), record)).toBe(false);
    expect(acceptsOrigin(review('r1'), builder('r1'))).toBe(false);
  });

  it('the Builder returns to the document whose Regenerate opened it', () => {
    expect(acceptsOrigin(builder('r1', 'd1'), doc('r1'))).toBe(true);
    expect(acceptsOrigin(builder(null), doc('r1'))).toBe(false);
    expect(acceptsOrigin(builder('r1'), settings())).toBe(false);
  });

  it('the Recording session, the viewer and the Style editor keep their designed parent', () => {
    expect(acceptsOrigin(record, review())).toBe(false);
    expect(acceptsOrigin(doc(), builder())).toBe(false);
    expect(acceptsOrigin({ name: 'style', styleId: 's' }, builder())).toBe(false);
  });

  it('ignores the moment and the version a route opens at', () => {
    expect(screenIdentity(review('r1', 5000))).toBe(screenIdentity(review('r1')));
    expect(screenIdentity({ name: 'document', recordingId: 'r1', documentId: 'd1', versionId: 'v1' })).toBe(screenIdentity(doc()));
  });
});

describe('Back targets along real paths', () => {
  it('Builder → Choose a model → Settings: Back returns to the Builder, then to its Review, then to the Library', () => {
    const w = walker();
    w.go(review(), null);
    w.go(builder(), 'Design review');
    w.go(settings('ai-privacy'), 'Meeting minutes');

    const fromSettings = backTargetOf(w.state, w.at(), LIBRARY);
    expect(fromSettings).toEqual({ route: builder(), label: 'Meeting minutes' });
    w.back(fromSettings.route);

    expect(backTargetOf(w.state, w.at(), REVIEW)).toEqual(REVIEW);
    w.back(REVIEW.route);
    expect(backTargetOf(w.state, w.at(), LIBRARY)).toEqual(LIBRARY);
    w.back(library);
    expect(w.state.trail).toEqual([]);
  });

  it('keeps the origin while Settings changes section', () => {
    const w = walker();
    w.go(review(), null);
    w.go(settings('transcription'), 'Design review');
    w.go(settings('general'));
    w.go(settings('documents'));
    expect(backTargetOf(w.state, w.at(), LIBRARY)).toEqual({ route: review(), label: 'Design review' });
  });

  it('a timestamp chip: Review goes back to the document, the document to Review, Review to the Library', () => {
    const w = walker();
    w.go(review(), null);
    w.go(doc(), 'Design review');
    w.go(review('r1', 1_122_000), 'Meeting minutes');

    const fromReview = backTargetOf(w.state, w.at(), LIBRARY);
    expect(fromReview).toEqual({ route: doc(), label: 'Meeting minutes' });
    w.back(fromReview.route);
    expect(backTargetOf(w.state, w.at(), REVIEW)).toEqual(REVIEW);
    w.back(REVIEW.route);
    expect(backTargetOf(w.state, w.at(), LIBRARY)).toEqual(LIBRARY);
  });

  it('never walks back through a screen only passed: Record → Stop → Review goes back to the Library', () => {
    const w = walker();
    w.go(record);
    w.go(review(), 'Untitled meeting');
    expect(backTargetOf(w.state, w.at(), LIBRARY)).toEqual(LIBRARY);
  });

  it('after Generate the viewer goes back to Review, not to the Builder', () => {
    const w = walker();
    w.go(review(), null);
    w.go(builder(), 'Design review');
    w.go(doc(), 'Meeting minutes');
    expect(backTargetOf(w.state, w.at(), REVIEW)).toEqual(REVIEW);
    w.back(REVIEW.route);
    expect(w.state.trail).toEqual([{ route: library, label: null }]);
  });

  it('Regenerate: the Builder goes back to the document it came from', () => {
    const w = walker();
    w.go(review(), null);
    w.go(doc(), 'Design review');
    w.go(builder('r1', 'd1'), 'Meeting minutes');
    expect(backTargetOf(w.state, w.at(), REVIEW)).toEqual({ route: doc(), label: 'Meeting minutes' });
  });

  it('falls back to the designed parent after a reload (empty trail)', () => {
    const w = walker();
    expect(backTargetOf(w.state, settings(), LIBRARY)).toEqual(LIBRARY);
    expect(backTargetOf(w.state, builder(), REVIEW)).toEqual(REVIEW);
  });

  it('uses the generic name when the screen gave none', () => {
    const w = walker();
    w.go(review(), null);
    w.go(settings(), null);
    expect(backTargetOf(w.state, w.at(), LIBRARY).label).toBe('Recording');
  });

  it('going to the Library clears the trail; the trail never grows past its limit', () => {
    const w = walker();
    for (let i = 0; i < TRAIL_LIMIT + 10; i++) {
      w.go(review(`r${i}`), `r${i}`);
    }
    expect(w.state.trail.length).toBe(TRAIL_LIMIT);
    w.go(library);
    expect(w.state.trail).toEqual([]);
  });

  it('a history move to the screen on top of the trail counts as Back', () => {
    const state = navigationOf({});
    state.label = null;
    recordMove(state, library, review(), 'forward');
    state.label = 'Design review';
    recordMove(state, review(), settings(), 'forward');
    recordMove(state, settings(), review(), 'history');
    expect(state.trail).toEqual([{ route: library, label: null }]);
  });

  it('remembers the Settings section last shown', () => {
    const w = walker();
    w.go(settings('documents'));
    w.go(library);
    expect(w.state.settingsSection).toBe('documents');
  });
});
