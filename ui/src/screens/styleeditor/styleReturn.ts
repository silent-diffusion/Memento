// Where the Style editor goes back to (DESIGN.md §13: "back to the template it came from"): the
// Builder that opened it, or Settings › Documents. A reload lands in Settings.
import type { Style } from '../../bridge/types';
import type { Route } from '../../state/router';
import type { AppStore } from '../../state/store';

export interface StyleReturn {
  /** The back control's text: the template's name, or "Settings". */
  label: string;
  route: Route;
  /** A saved style (a built-in saved becomes a copy): the Builder switches to it. */
  onSaved?: (style: Style) => void;
}

const returns = new WeakMap<AppStore, { current: StyleReturn | null }>();

export function styleReturnOf(store: AppStore): { current: StyleReturn | null } {
  let target = returns.get(store);
  if (target === undefined) {
    target = { current: null };
    returns.set(store, target);
  }
  return target;
}

export const SETTINGS_RETURN: StyleReturn = { label: 'Settings', route: { name: 'settings', section: 'documents' } };

/** Opens the Style editor for a style, remembering where Back goes. */
export function openStyleEditor(store: AppStore, navigate: (route: Route) => void, styleId: string, back: StyleReturn): void {
  styleReturnOf(store).current = back;
  navigate({ name: 'style', styleId });
}
