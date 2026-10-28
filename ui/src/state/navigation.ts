// Where Back goes (DESIGN.md §3, hub and spoke). Every spoke has a designed parent: Review and Settings go to the
// Library, the Builder and the Document viewer to their recording's Review, the Style editor to the template it came
// from. A few spokes are also opened from another spoke by a cross-screen link, and then Back returns there first:
//   - Settings, from any spoke (Choose a model in the Builder, Open Settings › Transcription in Review, the agenda's
//     AI link in the Details sheet): Back returns to that screen, then that screen's Back works as before;
//   - Review, from a document's timestamp chip: Back returns to the document;
//   - the Builder, from the Document viewer's Regenerate: Back returns to the document.
// Anything else falls back to the designed parent, so the person never walks back through screens they only passed
// (Record after Stop, the Builder after Generate). Going to the Library clears the trail: it is home.
//
// The trail lives beside the store (one per app) and only in memory; after a reload every Back is the designed one.
import type { Route, SettingsSection } from './router';

/** A screen the person came from, with the words a back control to it reads. */
export interface TrailEntry {
  route: Route;
  /** What a back control to it says ("Design review", "Meeting minutes"); null: the screen's generic name. */
  label: string | null;
}

export interface BackTarget {
  route: Route;
  label: string;
}

/** How a move happened: a link or button (forward), a back control, or a hashchange (the browser's own history). */
export type MoveKind = 'forward' | 'back' | 'history';

export interface NavigationState {
  trail: TrailEntry[];
  /** The open screen's own name, set by its header, pushed with it when the person moves on. */
  label: string | null;
  /** The Settings section last shown (Settings opens there from the Library's gear). */
  settingsSection: SettingsSection;
  /** The open spoke's back control (the header registers it); Esc, Alt+← and the mouse's back button use it. */
  back: (() => void) | null;
  /** False while Esc must not go back (the Recording session). */
  escapeBack: boolean;
  /**
   * Asked before a move by the page's own history (a hashchange) leaves the open screen: true holds the move (the
   * router puts the hash back) and the screen calls `proceed` once the person agrees to leave. The Builder sets it
   * while the template has unsaved changes (2.4.0); its back control asks the same question.
   */
  leaveGuard: ((proceed: () => void) => boolean) | null;
}

/** The longest trail kept; older entries are dropped (Back then falls back to the designed parent). */
export const TRAIL_LIMIT = 20;

/** Keyed by the app's store (or, in router tests, the router's store slice): one trail per app. */
const states = new WeakMap<object, NavigationState>();

export function navigationOf(store: object): NavigationState {
  let state = states.get(store);
  if (state === undefined) {
    state = { trail: [], label: null, settingsSection: 'general', back: null, escapeBack: false, leaveGuard: null };
    states.set(store, state);
  }
  return state;
}

/** The screen a route shows, ignoring the moment (`atMs`) and version it opens at. */
export function screenIdentity(route: Route): string {
  switch (route.name) {
    case 'library':
      return 'library';
    case 'record':
      return 'record';
    case 'review':
      return `review:${route.recordingId}`;
    case 'settings':
      return 'settings';
    case 'builder':
      return `builder:${route.recordingId ?? '-'}:${route.templateId ?? '-'}:${route.documentId ?? '-'}`;
    case 'document':
      return `document:${route.recordingId}:${route.documentId}`;
    case 'style':
      return `style:${route.styleId}`;
    case 'ask':
      return `ask:${route.recordingId}`;
  }
}

/** The generic name of a screen, for a back control when the screen gave none. */
export function genericLabel(route: Route): string {
  switch (route.name) {
    case 'library':
      return 'Library';
    case 'record':
      return 'Recording';
    case 'review':
      return 'Recording';
    case 'settings':
      return 'Settings';
    case 'builder':
      return 'Document builder';
    case 'document':
      return 'Document';
    case 'style':
      return 'Style';
    case 'ask':
      return 'Ask';
  }
}

/** Whether `current`'s Back may return to `origin` instead of its designed parent (see the header comment). */
export function acceptsOrigin(current: Route, origin: Route): boolean {
  switch (current.name) {
    case 'settings':
      return origin.name !== 'settings' && origin.name !== 'library';
    case 'review':
      // 2.9.0: a citation in Ask opens Review at its line; Back returns to the conversation.
      return (origin.name === 'document' || origin.name === 'ask') && origin.recordingId === current.recordingId;
    case 'builder':
      return origin.name === 'document' && current.recordingId !== null && origin.recordingId === current.recordingId;
    default:
      return false;
  }
}

/** Where `current`'s Back goes: the screen it was opened from when that is accepted, else `fallback`. */
export function backTargetOf(state: Pick<NavigationState, 'trail'>, current: Route, fallback: BackTarget): BackTarget {
  const top = state.trail.at(-1);
  if (top !== undefined && acceptsOrigin(current, top.route)) {
    return { route: top.route, label: top.label ?? genericLabel(top.route) };
  }
  return fallback;
}

/** Keeps the trail in step with a move from `from` to `to` (both routers call it before the route changes). */
export function recordMove(state: NavigationState, from: Route, to: Route, kind: MoveKind): void {
  if (to.name === 'settings') {
    state.settingsSection = to.section;
  }
  if (to.name === 'library') {
    state.trail = [];
    return;
  }
  const toId = screenIdentity(to);
  // The same screen (another Settings section, the style a duplicate opened, Record gaining its session): Back is
  // unchanged.
  if (from.name === to.name && (from.name === 'settings' || from.name === 'record' || screenIdentity(from) === toId)) {
    return;
  }
  const at = findLast(state.trail, toId);
  const isBack = kind === 'back' || (kind === 'history' && at !== -1 && at === state.trail.length - 1);
  if (isBack) {
    // Back to a screen on the trail: everything after it goes. Back to a designed parent that is not on the trail
    // (after a reload): nothing to keep.
    state.trail = at === -1 ? [] : state.trail.slice(0, at);
    return;
  }
  // Forward to a screen already on the trail (a loop: Review → document → its chip → Review): cut the loop so the
  // trail never grows round it, then remember where this move came from.
  const kept = at === -1 ? state.trail : state.trail.slice(0, at);
  state.trail = [...kept, { route: from, label: from.name === 'library' ? null : state.label }].slice(-TRAIL_LIMIT);
  // The next screen's header names itself once it has rendered.
  state.label = null;
}

function findLast(trail: readonly TrailEntry[], identity: string): number {
  for (let i = trail.length - 1; i >= 0; i--) {
    const entry = trail[i];
    if (entry !== undefined && screenIdentity(entry.route) === identity) {
      return i;
    }
  }
  return -1;
}
