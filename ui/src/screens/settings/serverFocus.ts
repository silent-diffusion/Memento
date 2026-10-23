// 2.5: the Builder's "Set the address" and "Choose a model" open Settings › AI and privacy at Your own server's field.
// The request waits beside the store until the server card mounts and takes it (once).
import type { AppStore } from '../../state/store';

export type ServerFocusTarget = 'address' | 'model' | 'key';

const pending = new WeakMap<AppStore, ServerFocusTarget>();

export function requestServerFocus(store: AppStore, target: ServerFocusTarget): void {
  pending.set(store, target);
}

/** The field the card should focus now, if one was asked for; asking again needs a new request. */
export function takeServerFocus(store: AppStore): ServerFocusTarget | null {
  const target = pending.get(store) ?? null;
  pending.delete(store);
  return target;
}
