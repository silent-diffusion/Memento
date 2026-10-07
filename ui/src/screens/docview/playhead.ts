// Where Review's player last was for each recording, so the viewer's "Insert timestamp" starts at
// the moment the user was listening to. Kept beside the store; nothing is saved.
import type { AppStore } from '../../state/store';

const registry = new WeakMap<AppStore, Map<string, number>>();

export function rememberPlayhead(store: AppStore, recordingId: string, positionMs: number): void {
  let map = registry.get(store);
  if (map === undefined) {
    map = new Map();
    registry.set(store, map);
  }
  map.set(recordingId, Math.max(0, positionMs));
}

/** Milliseconds, or null when Review has not played this recording in this session. */
export function playheadOf(store: AppStore, recordingId: string): number | null {
  return registry.get(store)?.get(recordingId) ?? null;
}
